using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace indian_ticketing;

public partial class Form1 : Form
{
    // Background WebView2 used to search IRCTC directly. Two things it must
    // NOT be, both confirmed to actually matter here:
    //  1. Occluded — Chromium throttles timers/rendering for a fully-covered
    //     surface the same way it throttles a backgrounded tab (this used to
    //     be SendToBack'd, fully covered by the grid).
    //  2. Tiny — IRCTC's page leans heavily on responsive breakpoints
    //     (hidden-xs vs. hidden-lg/md/sm duplicate elements, seen repeatedly
    //     while reverse-engineering its markup). A 4x4px control renders at
    //     a 4x4 CSS viewport, which media queries treat as narrower than any
    //     real mobile screen — selectors built against the desktop markup
    //     (verified against a normal-sized browser window) can end up
    //     targeting hidden elements while the mobile duplicates are the ones
    //     actually shown.
    // Fixed by giving it a real desktop-class size and moving it off-screen
    // (not zero-sized/hidden) — a WebView2 hosts an actual child HWND, so an
    // off-canvas position still renders and executes normally; it's just
    // never inside the form's visible client area.
    private WebView2 _webView = new() { Size = new Size(1280, 900), Location = new Point(-3000, -3000) };
    private readonly ProxyConfig _proxy = ProxyConfig.Load();

    // Serializes all use of _webView — both the From/To autocomplete's live
    // station lookups and the actual train search drive the same background
    // browser, and running two at once would mean two overlapping scripts
    // typing into (and reading) the same page at the same time.
    private readonly SemaphoreSlim _webViewGate = new(1, 1);

    // erail.in train-type colours
    private static readonly Color ColRajdhani  = Color.FromArgb(0xFF, 0x48, 0x0B);
    private static readonly Color ColSuperfast = Color.FromArgb(0xD5, 0x6A, 0x00);
    private static readonly Color ColGaribRath = Color.FromArgb(0x00, 0x80, 0x00);
    private static readonly Color ColMail      = Color.FromArgb(0x8B, 0x45, 0x13);
    private static readonly Color ColDefault   = Color.Black;

    // Selected station state
    private (string Name, string Code)? _fromStn;
    private (string Name, string Code)? _toStn;

    // Autocomplete drop-down controls (created in code, not designer)
    private Panel   _fromDropPanel = null!;
    private ListBox _fromDropList  = null!;
    private Panel   _toDropPanel   = null!;
    private ListBox _toDropList    = null!;
    private bool    _suppressAC    = false;

    public Form1()
    {
        InitializeComponent();
        BuildColumns();
        dgvTrains.CellFormatting += DgvTrains_CellFormatting;
        InitAutocomplete();

        Controls.Add(_webView);
        _webView.BringToFront();

        // Cold-starting WebView2 and loading IRCTC's search page is the
        // single biggest cost in a station lookup — pay for it now, in the
        // background, while the form is still opening and nobody has typed
        // anything yet, instead of making the very first character typed
        // into From/To wait for all of it.
        _ = PrewarmStationLookupAsync();
    }

    // ── Autocomplete ─────────────────────────────────────────────────────────
    private void InitAutocomplete()
    {
        (_fromDropPanel, _fromDropList) = CreateDropdown();
        (_toDropPanel,   _toDropList)   = CreateDropdown();

        WireAutocomplete(txtFrom, _fromDropPanel, _fromDropList,
            v => { _fromStn = v; });
        WireAutocomplete(txtTo,   _toDropPanel,   _toDropList,
            v => { _toStn   = v; });
    }

    private (Panel panel, ListBox list) CreateDropdown()
    {
        var list = new ListBox
        {
            Dock        = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font        = new Font("Segoe UI", 9F),
            ItemHeight  = 20,
        };
        var panel = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = Color.White,
            Visible     = false,
        };
        panel.Controls.Add(list);
        Controls.Add(panel);
        panel.BringToFront();
        return (panel, list);
    }

    private void WireAutocomplete(
        TextBox txt, Panel panel, ListBox list,
        Action<(string Name, string Code)?> setStation)
    {
        CancellationTokenSource? debounceCts = null;

        txt.TextChanged += (_, _) =>
        {
            if (_suppressAC) return;
            setStation(null);

            debounceCts?.Cancel();
            var query = txt.Text.Trim();
            if (query.Length < 2) { panel.Visible = false; return; }

            debounceCts = new CancellationTokenSource();
            _ = ShowLiveSuggestionsAsync(txt, panel, list, query, debounceCts.Token);
        };

        txt.KeyDown += (_, e) =>
        {
            if (!panel.Visible) return;
            switch (e.KeyCode)
            {
                case Keys.Down:
                    list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1);
                    e.Handled = true; break;
                case Keys.Up:
                    list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0);
                    e.Handled = true; break;
                case Keys.Enter when list.SelectedIndex >= 0:
                    PickItem(panel, list, txt, setStation);
                    e.SuppressKeyPress = true; break;
                case Keys.Escape:
                    panel.Visible = false;
                    e.Handled = true; break;
            }
        };

        list.MouseClick += (_, _) => PickItem(panel, list, txt, setStation);

        txt.Leave += async (_, _) =>
        {
            await Task.Delay(180);
            if (!IsDisposed) panel.Invoke(() => panel.Visible = false);
        };
    }

    // Looks up suggestions from IRCTC's own live autocomplete (see
    // IrctcWebViewSession.GetStationSuggestionsAsync) instead of a
    // hardcoded local list, so results always match what the real site
    // would offer. Debounced (200ms of no further typing) so a fast typist
    // doesn't queue up a script execution per keystroke, and the result is
    // dropped if the field's text has since changed or typing was cancelled.
    private async Task ShowLiveSuggestionsAsync(
        TextBox txt, Panel panel, ListBox list, string query, CancellationToken token)
    {
        try { await Task.Delay(200, token); }
        catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested || IsDisposed) return;

        // Live lookups normally settle in ~1-1.5s, but the first one after a
        // completed search (or the very first of the session) can take
        // noticeably longer while the background browser gets back to
        // IRCTC's search page — show a placeholder immediately so typing
        // doesn't look like it did nothing, instead of leaving the box empty
        // until the real results arrive.
        ShowItems(txt, panel, list, new[] { "Searching IRCTC..." });

        List<(string Name, string Code)> results;
        await _webViewGate.WaitAsync(token);
        try
        {
            if (token.IsCancellationRequested) return;
            var session = new IrctcWebViewSession(_webView, _proxy, "WebView2-Search");
            results = await session.GetStationSuggestionsAsync(query);
        }
        catch (OperationCanceledException) { return; }
        finally { _webViewGate.Release(); }

        if (token.IsCancellationRequested || IsDisposed) return;
        if (!txt.Text.Trim().Equals(query, StringComparison.OrdinalIgnoreCase)) return; // stale

        if (results.Count == 0) { panel.Visible = false; return; }

        ShowItems(txt, panel, list, results.Select(r => $"{r.Name} ({r.Code})"));
    }

    private void ShowItems(TextBox txt, Panel panel, ListBox list, IEnumerable<string> items)
    {
        list.Items.Clear();
        list.Items.AddRange(items.Cast<object>().ToArray());
        if (list.Items.Count == 0) { panel.Visible = false; return; }

        var pt  = txt.Parent!.PointToScreen(new Point(txt.Left, txt.Bottom));
        var fp  = PointToClient(pt);
        int h   = Math.Min(list.Items.Count * list.ItemHeight + 4, 200);
        panel.SetBounds(fp.X, fp.Y, Math.Max(txt.Width + 30, 260), h);
        panel.Visible = true;
        panel.BringToFront();
    }

    private void PickItem(
        Panel panel, ListBox list, TextBox txt,
        Action<(string Name, string Code)?> setStation)
    {
        if (list.SelectedIndex < 0) return;
        var text = list.Items[list.SelectedIndex]!.ToString()!;
        var op   = text.LastIndexOf(" (");
        if (op > 0 && text.EndsWith(")"))
        {
            var name = text[..op];
            var code = text[(op + 2)..^1];
            setStation((name, code));
            _suppressAC = true;
            txt.Text    = text;
            _suppressAC = false;
        }
        panel.Visible = false;
    }

    // ── Parse station from textbox text (fallback when user types code only) ──
    private static (string Name, string Code) ResolveStation(
        TextBox txt, (string Name, string Code)? stored)
    {
        if (stored.HasValue) return stored.Value;

        var t  = txt.Text.Trim();
        var op = t.LastIndexOf(" (");
        if (op > 0 && t.EndsWith(")"))
            return (t[..op], t[(op + 2)..^1].ToUpperInvariant());

        var code  = t.ToUpperInvariant();
        var found = StationData.Stations.FirstOrDefault(s => s.Code == code);
        return found != default ? found : (code, code);
    }

    // ── Column definitions ────────────────────────────────────────────────────
    private void BuildColumns()
    {
        dgvTrains.AutoGenerateColumns = false;
        dgvTrains.Columns.AddRange(
            TC("TrainNo",   "Train",     65),
            TC("TrainName", "Train Name",170),
            TC("From",      "From",      55),
            TC("DepTime",   "Dep.",       55),
            TC("DepDate",   "Date",       55),
            TC("To",        "To",         55),
            TC("ArrTime",   "Arr.",       55),
            TC("ArrDate",   "Date",       55),
            TC("Duration",  "Travel",     60),
            TC("Mon", "M", 26), TC("Tue", "T", 26), TC("Wed", "W", 26),
            TC("Thu", "T", 26), TC("Fri", "F", 26), TC("Sat", "S", 26),
            TC("Sun", "S", 26),
            TC("Avl1A", "1A", 70), TC("Avl2A", "2A", 70),
            TC("Avl3A", "3A", 70), TC("AvlCC", "CC", 70),
            TC("AvlSL", "SL", 70), TC("Avl2S", "2S", 70),
            TC("Avl3E", "3E", 70)
        );
    }

    private static DataGridViewTextBoxColumn TC(string prop, string hdr, int w) => new()
    {
        DataPropertyName = prop,
        HeaderText       = hdr,
        Width            = w,
        ReadOnly         = true,
        SortMode         = DataGridViewColumnSortMode.Automatic,
        DefaultCellStyle = new DataGridViewCellStyle
            { Alignment = DataGridViewContentAlignment.MiddleCenter }
    };

    // ── Cell colour formatting ────────────────────────────────────────────────
    private void DgvTrains_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || dgvTrains.DataSource is not List<TrainInfo> list) return;
        if (e.CellStyle is null) return;
        var train = list[e.RowIndex];
        var col   = dgvTrains.Columns[e.ColumnIndex].DataPropertyName;

        switch (col)
        {
            case "TrainNo":
            case "TrainName":
                e.CellStyle.ForeColor = ParseTrainColor(train.TrainColor);
                e.CellStyle.Font = new Font(dgvTrains.Font, FontStyle.Bold);
                break;

            case "DepDate":
                e.CellStyle.ForeColor = train.DepSameDay ? Color.Green : Color.Red;
                break;

            case "ArrDate":
                e.CellStyle.ForeColor = train.ArrSameDay ? Color.Green : Color.Red;
                break;

            case "Mon": case "Tue": case "Wed": case "Thu":
            case "Fri": case "Sat": case "Sun":
                if (e.Value?.ToString() == "Y")
                {
                    e.CellStyle.ForeColor = Color.Green;
                    e.CellStyle.Font = new Font(dgvTrains.Font, FontStyle.Bold);
                }
                else
                    e.CellStyle.ForeColor = Color.LightGray;
                break;

            case "Avl1A": case "Avl2A": case "Avl3A": case "AvlCC":
            case "AvlSL": case "Avl2S": case "Avl3E":
                var avl = e.Value?.ToString() ?? "";
                if (avl.StartsWith("AVAIL", StringComparison.OrdinalIgnoreCase))
                    e.CellStyle.ForeColor = Color.Green;
                else if (avl.StartsWith("WL", StringComparison.OrdinalIgnoreCase))
                    e.CellStyle.ForeColor = Color.DarkOrange;
                else if (avl == "x" || avl == "")
                    e.CellStyle.ForeColor = Color.LightGray;
                else
                    e.CellStyle.ForeColor = Color.DimGray;
                break;
        }
    }

    private static Color ParseTrainColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return ColDefault;
        try { return ColorTranslator.FromHtml(hex); }
        catch { return ColDefault; }
    }

    // ── Search button ─────────────────────────────────────────────────────────
    private async void btnGetTrains_Click(object? sender, EventArgs e)
    {
        var (_, fromCode) = ResolveStation(txtFrom, _fromStn);
        var (_, toCode)   = ResolveStation(txtTo,   _toStn);

        if (string.IsNullOrWhiteSpace(fromCode) || string.IsNullOrWhiteSpace(toCode))
        {
            MessageBox.Show("Enter both station codes (e.g. NDLS, BCT).",
                "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnGetTrains.Enabled = false;
        dgvTrains.DataSource = null;
        lblStatus.Text       = "Searching IRCTC…";

        var date     = dtpDate.Value.ToString("dd-MMM-yyyy");
        var progress = new Progress<string>(msg =>
            { if (!IsDisposed) lblStatus.Text = msg; });

        try
        {
            var trains = await SearchTrainsWithProxyFallbackAsync(fromCode, toCode, date, progress);
            dgvTrains.DataSource = trains;
            lblStatus.Text = trains.Count > 0
                ? $"{trains.Count} trains found  |  {fromCode} → {toCode}  |  {date}"
                : $"No trains found for {fromCode} → {toCode}.";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Error.";
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnGetTrains.Enabled = true;
            // A completed search leaves the WebView2 sitting on the results
            // page, not train-search — get it back there now (off the UI
            // thread's critical path) so the next From/To autocomplete
            // lookup doesn't have to pay for that re-navigation itself.
            _ = PrewarmStationLookupAsync();
        }
    }

    private async Task PrewarmStationLookupAsync()
    {
        await _webViewGate.WaitAsync();
        try
        {
            var session = new IrctcWebViewSession(_webView, _proxy, "WebView2-Search");
            await session.PrewarmSearchPageAsync();
        }
        catch { /* best-effort */ }
        finally { _webViewGate.Release(); }
    }

    // Tries IRCTC direct first; if its edge WAF blocks the connection
    // outright (IrctcBlockedException), tears down the background browser
    // control and retries once through the configured proxy. WebView2's
    // proxy is fixed for the life of a browser process, so a proxy retry
    // needs a fresh control, not just a re-navigate.
    private async Task<List<TrainInfo>> SearchTrainsWithProxyFallbackAsync(
        string fromCode, string toCode, string date, IProgress<string> progress)
    {
        await _webViewGate.WaitAsync();
        try
        {
            try
            {
                var session = new IrctcWebViewSession(_webView, _proxy, "WebView2-Search");
                return await session.SearchTrainsAsync(fromCode, toCode, date, progress);
            }
            catch (IrctcBlockedException) when (_proxy.IsConfigured)
            {
                progress.Report("IRCTC blocked direct access — retrying through proxy...");

                var old = _webView;
                Controls.Remove(old);
                old.Dispose();

                _webView = new WebView2 { Size = new Size(1280, 900), Location = new Point(-3000, -3000) };
                Controls.Add(_webView);
                _webView.BringToFront();

                var session = new IrctcWebViewSession(_webView, _proxy, "WebView2-Search");
                return await session.SearchTrainsAsync(fromCode, toCode, date, progress, useProxy: true);
            }
        }
        finally { _webViewGate.Release(); }
    }

    // ── Save Train button ─────────────────────────────────────────────────────
    private void btnSaveTrain_Click(object? sender, EventArgs e)
    {
        if (dgvTrains.SelectedRows.Count == 0)
        {
            MessageBox.Show("Select a train row first.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var (fromName, fromCode) = ResolveStation(txtFrom, _fromStn);
        var (toName,   toCode)   = ResolveStation(txtTo,   _toStn);
        var date  = dtpDate.Value.ToString("dd-MMM-yyyy");
        var cls   = cmbClass.SelectedItem?.ToString() ?? "SL";
        var quota = cmbQuota.SelectedItem?.ToString() ?? "General Quota";

        // Normalise class code: "SL - Sleeper" → "SL"
        var clsCode = cls.Contains('-') ? cls.Split('-')[0].Trim() : cls;
        // Normalise quota: "General Quota" → "GN"
        var quotaCode = quota switch
        {
            var q when q.Contains("General")  => "GN",
            var q when q.Contains("Tatkal")   => "TQ",
            var q when q.Contains("Pre")      => "PT",
            _                                  => "GN",
        };

        var saved = new List<SavedBooking>();
        foreach (DataGridViewRow row in dgvTrains.SelectedRows)
        {
            if (dgvTrains.DataSource is not List<TrainInfo> list) break;
            var train = list[row.Index];

            var booking = new SavedBooking
            {
                TrainNo     = train.TrainNo,
                TrainName   = train.TrainName,
                FromCode    = fromCode,
                FromName    = fromName,
                ToCode      = toCode,
                ToName      = toName,
                DepTime     = train.DepTime,
                ArrTime     = train.ArrTime,
                Duration    = train.Duration,
                JourneyDate = date,
                TravelClass = clsCode,
                Quota       = quotaCode,
            };
            saved.Add(booking);
        }

        if (saved.Count == 0) return;

        // Ask for passengers once (applies to all selected rows)
        using var dlg = new PassengersDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var existing = SavedBooking.LoadAll();
        foreach (var b in saved)
        {
            b.Passengers = dlg.Passengers;
            existing.Add(b);
        }
        SavedBooking.SaveAll(existing);

        lblStatus.Text = $"{saved.Count} train(s) saved for booking.";
        MessageBox.Show($"{saved.Count} train(s) saved.\nOpen Booking Manager to start IRCTC automation.",
            "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Booking Manager button ────────────────────────────────────────────────
    private void btnBookingMgr_Click(object? sender, EventArgs e)
    {
        new BookingManagerForm().Show(this);
    }

    // ── Swap button ───────────────────────────────────────────────────────────
    private void btnSwap_Click(object? sender, EventArgs e)
    {
        (txtFrom.Text, txtTo.Text) = (txtTo.Text, txtFrom.Text);
        (_fromStn,     _toStn)     = (_toStn,     _fromStn);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _webView.Dispose();
        base.OnFormClosed(e);
    }
}
