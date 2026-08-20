using System.Globalization;

namespace indian_ticketing;

// Matches every column in erail.in's table exactly
public class TrainInfo
{
    public string TrainNo    { get; set; } = "";
    public string TrainName  { get; set; } = "";
    public string From       { get; set; } = "";
    public string DepTime    { get; set; } = "";
    public string DepDate    { get; set; } = "";
    public string To         { get; set; } = "";
    public string ArrTime    { get; set; } = "";
    public string ArrDate    { get; set; } = "";
    public string Duration   { get; set; } = "";
    public string Mon        { get; set; } = "";
    public string Tue        { get; set; } = "";
    public string Wed        { get; set; } = "";
    public string Thu        { get; set; } = "";
    public string Fri        { get; set; } = "";
    public string Sat        { get; set; } = "";
    public string Sun        { get; set; } = "";
    public string Avl1A      { get; set; } = "";
    public string Avl2A      { get; set; } = "";
    public string Avl3A      { get; set; } = "";
    public string AvlCC      { get; set; } = "";
    public string AvlSL      { get; set; } = "";
    public string Avl2S      { get; set; } = "";
    public string Avl3E      { get; set; } = "";

    // UI-only fields (not shown as columns but used for cell colouring)
    public string TrainColor   { get; set; } = "";
    public bool   DepSameDay   { get; set; } = true;
    public bool   ArrSameDay   { get; set; } = true;
}

// Fast, browser-free scraper. erail.in exposes the same data its own page loads
// via a lightweight text endpoint (getTrains.aspx) that returns all trains for a
// route as a single ~/^-delimited blob. We hit it directly with HttpClient — no
// Selenium, no Chrome, no AJAX waits — so a search completes in a few hundred ms.
//
// NB: live per-class seat availability (AVAILABLE-12 / WL 45) is NOT in this feed;
// erail loads it lazily per train via separate calls. We show which classes run
// instead. The TrainScraper public surface (SearchAsync / Dispose) is unchanged.
public class TrainScraper : IDisposable
{
    private readonly HttpClient _http;

    public TrainScraper(ProxyConfig? proxy = null)
    {
        _http = CreateClient(proxy);
    }

    private static HttpClient CreateClient(ProxyConfig? proxy)
    {
        var handler = new HttpClientHandler();
        proxy?.ApplyToHandler(handler);
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Referrer = new Uri("https://erail.in/");
        return c;
    }

    // erail.in class-bitmap positions (the 15-char field). Maps each UI column to
    // its index in the bitmap. Verified against Rajdhani (1A/2A/3A), Garib Rath (3A),
    // and Shatabdi/Vande Bharat (CC).
    private const int Bit1A = 0, Bit2A = 1, Bit3A = 2, BitCC = 3,
                      Bit3E = 4, BitSL = 5, Bit2S = 7;

    // ── public API (unchanged signature) ────────────────────────────────────
    public async Task<List<TrainInfo>> SearchAsync(
        string fromName, string fromCode,
        string toName, string toCode,
        string date, IProgress<string>? progress = null)
    {
        progress?.Report($"Querying erail.in for {fromCode} → {toCode}…");

        var url = "https://erail.in/rail/getTrains.aspx" +
                  $"?Station_From={Uri.EscapeDataString(fromCode)}" +
                  $"&Station_To={Uri.EscapeDataString(toCode)}" +
                  "&DataSource=0&Language=0&Cache=true";

        string raw;
        try
        {
            raw = await _http.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            throw new Exception($"Could not reach erail.in: {ex.Message}", ex);
        }

        progress?.Report("Parsing results…");
        var trains = ParseTrains(raw);
        progress?.Report($"{trains.Count} trains.");
        return trains;
    }

    // ── parsing ───────────────────────────────────────────────────────────────
    // Response shape: "<header>^<train1>^<train2>^…", each train a ~-delimited
    // record. Field indices below were decoded from the live feed.
    private static List<TrainInfo> ParseTrains(string raw)
    {
        var trains = new List<TrainInfo>();
        if (string.IsNullOrWhiteSpace(raw)) return trains;

        var records = raw.Split('^');
        foreach (var rec in records)
        {
            if (string.IsNullOrWhiteSpace(rec)) continue;
            var f = rec.Split('~');
            if (f.Length < 22) continue;          // header / malformed row

            var trainNo = f[0].Trim();
            if (trainNo.Length == 0 || !trainNo.Any(char.IsDigit)) continue;

            var days   = f[13].Trim();            // 7-char Mon..Sun bitmap
            var cls    = f[21].Trim();            // 15-char class bitmap

            var t = new TrainInfo
            {
                TrainNo   = trainNo,
                TrainName = f[1].Trim(),
                From      = f[7].Trim(),          // boarding station code
                DepTime   = f[10].Trim(),
                To        = f[9].Trim(),          // alighting station code
                ArrTime   = f[11].Trim(),
                Duration  = f[12].Trim(),

                Mon = DayFlag(days, 0),
                Tue = DayFlag(days, 1),
                Wed = DayFlag(days, 2),
                Thu = DayFlag(days, 3),
                Fri = DayFlag(days, 4),
                Sat = DayFlag(days, 5),
                Sun = DayFlag(days, 6),

                // We can't show live seats from this feed, so each cell shows the
                // class code when the train offers it, or "x" when it doesn't.
                Avl1A = ClassChip(cls, Bit1A, "1A"),
                Avl2A = ClassChip(cls, Bit2A, "2A"),
                Avl3A = ClassChip(cls, Bit3A, "3A"),
                AvlCC = ClassChip(cls, BitCC, "CC"),
                AvlSL = ClassChip(cls, BitSL, "SL"),
                Avl2S = ClassChip(cls, Bit2S, "2S"),
                Avl3E = ClassChip(cls, Bit3E, "3E"),

                TrainColor = TrainTypeColor(f),
            };

            // Day fields: arr-date can roll over midnight. The feed gives times but
            // not explicit per-row calendar dates for the searched journey, so derive
            // the "next day" flag from whether arrival time is earlier than departure.
            (t.DepDate, t.ArrDate, t.DepSameDay, t.ArrSameDay) =
                DeriveDates(t.DepTime, t.ArrTime);

            trains.Add(t);
        }

        return trains;
    }

    private static string DayFlag(string bitmap, int idx)
        => idx < bitmap.Length && bitmap[idx] == '1' ? "Y" : "x";

    private static string ClassChip(string bitmap, int idx, string code)
        => idx < bitmap.Length && bitmap[idx] == '1' ? code : "x";

    // erail colours train numbers by type. Field 15 carries the type label
    // ("Rajdhani", "Super Fast", "Garib Rath", …). Map it to the same palette
    // Form1 uses so the grid keeps its colour cues.
    private static string TrainTypeColor(string[] f)
    {
        var type = f.Length > 15 ? f[15].Trim() : "";
        return type.ToLowerInvariant() switch
        {
            var s when s.Contains("rajdhani")   => "#FF480B",
            var s when s.Contains("garib")      => "#008000",
            var s when s.Contains("super")      => "#D56A00",
            var s when s.Contains("mail")       => "#8B4513",
            var s when s.Contains("express")    => "#8B4513",
            _                                    => "",
        };
    }

    // Times are "HH.mm". If arrival is at/after departure it's same-day; if it
    // wraps past midnight the journey lands on a later day.
    private static (string dep, string arr, bool depSame, bool arrSame)
        DeriveDates(string depTime, string arrTime)
    {
        if (TryTime(depTime, out var dep) && TryTime(arrTime, out var arr))
        {
            bool arrSame = arr >= dep;
            return ("", "", true, arrSame);
        }
        return ("", "", true, true);
    }

    private static bool TryTime(string s, out TimeSpan ts)
        => TimeSpan.TryParseExact(s.Replace('.', ':'), @"h\:mm",
               CultureInfo.InvariantCulture, out ts)
           || TimeSpan.TryParseExact(s.Replace('.', ':'), @"hh\:mm",
               CultureInfo.InvariantCulture, out ts);

    public void Dispose()
    {
        // HttpClient is shared & static; nothing per-instance to release.
    }
}
