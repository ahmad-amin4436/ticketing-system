using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Net;

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

public class TrainScraper : IDisposable
{
    private ChromeDriver? _driver;
    private bool _disposed;

    // ── public API ───────────────────────────────────────────────────────────
    public async Task<List<TrainInfo>> SearchAsync(
        string fromName, string fromCode,
        string toName, string toCode,
        string date, IProgress<string>? progress = null)
    {
        var fromSlug = StationData.ToSlug(fromName, fromCode);
        var toSlug   = StationData.ToSlug(toName,   toCode);
        return await Task.Run(() => Search(fromSlug, toSlug, fromCode, toCode, date, progress));
    }

    // ── private implementation ───────────────────────────────────────────────
    private List<TrainInfo> Search(
        string fromSlug, string toSlug,
        string fromCode, string toCode,
        string date, IProgress<string>? progress)
    {
        if (_driver == null)
        {
            progress?.Report("Starting browser…");
            var opts = new ChromeOptions();
            opts.AddArgument("--headless=new");
            opts.AddArgument("--no-sandbox");
            opts.AddArgument("--disable-dev-shm-usage");
            opts.AddArgument("--disable-gpu");
            opts.AddArgument("--disable-software-rasterizer");
            opts.AddArgument("--disable-background-networking");
            opts.AddArgument("--disable-extensions");
            opts.AddArgument("--no-first-run");
            opts.AddArgument("--window-size=1920,1080");
            opts.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            var svc = ChromeDriverService.CreateDefaultService();
            svc.HideCommandPromptWindow = true;
            _driver = new ChromeDriver(svc, opts);
            _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        }

        progress?.Report($"Loading erail.in for {fromCode} → {toCode}…");

        // Clean erail.in URL: trains-between-stations/{fromSlug}/{toSlug}
        var url = $"https://erail.in/trains-between-stations/{fromSlug}/{toSlug}";

        if (!string.IsNullOrWhiteSpace(date))
            url += $"?Date={Uri.EscapeDataString(date)}";

        _driver.Navigate().GoToUrl(url);

        progress?.Report("Waiting for train rows to render…");

        // Wait using Selenium FindElements (C#-side, no JS injection risk)
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
        try
        {
            wait.Until(d => d.FindElements(By.CssSelector("tr[data-id]")).Count > 0);
        }
        catch (WebDriverTimeoutException)
        {
            progress?.Report("Page load slow — parsing whatever loaded…");
        }

        // Extra pause so any late AJAX finishes
        Thread.Sleep(2000);

        progress?.Report("Parsing HTML…");

        // ── KEY FIX: use driver.PageSource (C#) + HtmlAgilityPack ────────────
        // Never inject JS for data extraction — large erail.in pages cause 60s timeout.
        var html = _driver.PageSource;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        return ParseTrains(doc);
    }

    // ── HTML parsing (HtmlAgilityPack) ───────────────────────────────────────
    private static List<TrainInfo> ParseTrains(HtmlDocument doc)
    {
        var trains = new List<TrainInfo>();

        // Only rows with data-id are actual train rows (not headers or separators)
        var rows = doc.DocumentNode.SelectNodes("//tr[@data-id]");
        if (rows == null) return trains;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 9) continue;

            static string Cell(HtmlNode td)
                => WebUtility.HtmlDecode(td.InnerText).Replace("\r", "").Replace("\n", " ")
                             .Replace("&nbsp;", " ").Trim();

            static string LinkText(HtmlNode td)
            {
                var a = td.SelectSingleNode(".//a");
                return a != null
                    ? WebUtility.HtmlDecode(a.InnerText).Trim()
                    : Cell(td);
            }

            static string CellStyle(HtmlNode td)
                => td.GetAttributeValue("style", "");

            static string LinkStyle(HtmlNode td)
                => td.SelectSingleNode(".//a")?.GetAttributeValue("style", "") ?? "";

            // day cell: "Y" or "x"
            static string Day(HtmlNode td)
                => td.InnerText.Trim() == "Y" ? "Y" : "x";

            // availability cell: text of inner <a> if present, else cell text
            static string Avl(HtmlNode td)
            {
                var a = td.SelectSingleNode(".//a");
                var v = a != null
                    ? WebUtility.HtmlDecode(a.InnerText).Trim()
                    : Cell(td);
                return v == "" || v == " " ? "x" : v;
            }

            var t = new TrainInfo
            {
                TrainNo   = LinkText(cells[0]),
                TrainName = LinkText(cells[1]),
                From      = LinkText(cells[2]),
                DepTime   = Cell(cells[3]),
                DepDate   = Cell(cells[4]),
                To        = LinkText(cells[5]),
                ArrTime   = Cell(cells[6]),
                ArrDate   = Cell(cells[7]),
                Duration  = LinkText(cells[8]),

                Mon = cells.Count > 10 ? Day(cells[10]) : "",
                Tue = cells.Count > 11 ? Day(cells[11]) : "",
                Wed = cells.Count > 12 ? Day(cells[12]) : "",
                Thu = cells.Count > 13 ? Day(cells[13]) : "",
                Fri = cells.Count > 14 ? Day(cells[14]) : "",
                Sat = cells.Count > 15 ? Day(cells[15]) : "",
                Sun = cells.Count > 16 ? Day(cells[16]) : "",

                Avl1A = cells.Count > 17 ? Avl(cells[17]) : "",
                Avl2A = cells.Count > 18 ? Avl(cells[18]) : "",
                Avl3A = cells.Count > 19 ? Avl(cells[19]) : "",
                AvlCC = cells.Count > 20 ? Avl(cells[20]) : "",
                AvlSL = cells.Count > 21 ? Avl(cells[21]) : "",
                Avl2S = cells.Count > 22 ? Avl(cells[22]) : "",
                Avl3E = cells.Count > 23 ? Avl(cells[23]) : "",

                // Colour for the train number cell (matches erail.in)
                TrainColor = ExtractColor(LinkStyle(cells[0])),

                // Green = same day, Red = next day
                DepSameDay = CellStyle(cells[4]).Contains("green"),
                ArrSameDay = CellStyle(cells[7]).Contains("green"),
            };

            if (!string.IsNullOrWhiteSpace(t.TrainNo) && t.TrainNo.Any(char.IsDigit))
                trains.Add(t);
        }

        return trains;
    }

    private static string ExtractColor(string style)
    {
        // style="color:#D56A00" → "#D56A00"
        var idx = style.IndexOf("color:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = style[(idx + 6)..].Trim().TrimEnd(';').Trim();
        return rest.Split(' ')[0];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _driver?.Quit();
        _driver?.Dispose();
        _driver = null;
        _disposed = true;
    }
}
