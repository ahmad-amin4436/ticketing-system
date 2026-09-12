using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace indian_ticketing;

public class BookingManagerForm : Form
{
    // ── Controls ──────────────────────────────────────────────────────────
    private readonly Panel          _topBar      = new();
    private readonly Label          _lblTitle    = new();
    private readonly TextBox        _txtUser     = new();
    private readonly TextBox        _txtPass     = new();
    private readonly TextBox        _txtProxy    = new();
    private readonly Button         _btnStartAll = new();
    private readonly Button         _btnRefresh  = new();
    private readonly Button         _btnSaveProxy = new();
    private readonly Button         _btnToggleProxy = new();
    private readonly Label          _lblSession  = new();
    private readonly ToolTip        _sessionTip  = new();
    private readonly Button         _btnManageUsers = new();
    private readonly FlowLayoutPanel _cardPanel  = new();

    // ── State ─────────────────────────────────────────────────────────────
    // No shared browser or session anymore — every booking gets its own
    // isolated WebView2 (see StartBookingAsync), so bookings run in
    // parallel instead of one at a time, and the IRCTC page isn't shown in
    // this window at all (BookingCard.ToggleBrowserPopup reveals a specific
    // one on demand instead).
    // _allBookings is EVERY user's saved bookings — the true on-disk list,
    // and the only one ever passed to SavedBooking.SaveAll (deleting must
    // remove from and re-save the FULL list, never the filtered one below,
    // or a non-admin's delete would silently wipe out every other user's
    // bookings too). _bookings is the filtered subset actually shown —
    // just this signed-in user's own, unless they hold VIEW_ALL_BOOKINGS.
    private List<SavedBooking>        _allBookings = SavedBooking.LoadAll();
    private List<SavedBooking>        _bookings    = new();
    private readonly List<BookingCard> _cards   = new();
    private readonly ProxyConfig       _proxy   = ProxyConfig.Load();
    // The currently-running session for each card that has one — looked up
    // by "OK (Continue)" to acknowledge the right booking's own manual-step
    // prompt, now that each card can have an independent run in flight.
    private readonly Dictionary<BookingCard, IrctcWebViewSession> _sessions = new();

    // One reusable QR popup window per booking — a refreshed QR replaces the
    // image in the existing window instead of opening a new one.
    private readonly Dictionary<SavedBooking, QrPopupForm> _qrPopups = new();

    public BookingManagerForm()
    {
        BuildUi();
        _txtUser.Text = "SEJAL115";
        _txtPass.Text = "Radharani@89";
        // Load saved proxy config — display as HOST:PORT:USERNAME:PASSWORD
        if (_proxy.IsConfigured)
        {
            _txtProxy.Text = _proxy.HasCredentials
                ? $"{_proxy.Host}:{_proxy.Port}:{_proxy.Username}:{_proxy.Password}"
                : $"{_proxy.Host}:{_proxy.Port}";
        }
        ApplyRolePermissions();
        RebuildCards();
    }

    // Users without MANAGE_CREDENTIALS can view the booking list but not
    // touch IRCTC credentials/proxy settings or start/delete bookings
    // (needs MANAGE_BOOKINGS) or manage users (needs MANAGE_USERS) — which
    // permissions those actually are is entirely data-driven from the
    // Users/Roles/Permissions tables via Session.Has(...), not a hardcoded
    // role check, so a custom role can be granted any subset of these.
    private void ApplyRolePermissions()
    {
        var user = Session.CurrentUser;
        // Single-letter role badge (A/O/…) instead of spelling the role
        // name out — keeps the header bar compact regardless of how long a
        // custom role's name is. Full name still available on hover so
        // nothing's actually lost, just not spelled out inline.
        if (user != null)
        {
            _lblSession.Text = $"Signed in as {user.Username} ({user.RoleName[..1].ToUpperInvariant()})";
            _sessionTip.SetToolTip(_lblSession, $"Role: {user.RoleName}");
        }
        else
        {
            _lblSession.Text = "Not signed in";
        }

        _btnManageUsers.Visible = Session.Has("MANAGE_USERS");

        if (!Session.Has("MANAGE_CREDENTIALS"))
        {
            // Mask the displayed credentials rather than just disabling the
            // boxes — a restricted user shouldn't be able to read the IRCTC
            // account password or the proxy's plain-text host/port/creds.
            _txtUser.Text = "••••••••"; _txtUser.Enabled = false;
            _txtPass.Text = "••••••••"; _txtPass.Enabled = false;
            _txtProxy.Text = "•••• (restricted)"; _txtProxy.Enabled = false;
            _btnSaveProxy.Enabled   = false;
            _btnToggleProxy.Enabled = false;
        }

        if (!Session.Has("MANAGE_BOOKINGS"))
            _btnStartAll.Enabled = false;
    }

    // ── UI construction ───────────────────────────────────────────────────
    private void BuildUi()
    {
        // Top bar — two rows, each laid out with FlowLayoutPanels instead of
        // hand-computed X coordinates. Absolute positioning here repeatedly
        // produced overlaps (a label's actual rendered width never quite
        // matched what was guessed for the next control's X) — a flow panel
        // measures each child's real size and places the next one after it,
        // so this class of bug can't recur regardless of font metrics.
        _topBar.Dock      = DockStyle.Top;
        _topBar.Height    = 92;
        _topBar.BackColor = UiTheme.Primary;

        // Row 1: title on the left, action buttons on the right.
        var row1 = new Panel { Dock = DockStyle.Top, Height = 44 };

        _lblTitle.AutoSize  = true;
        _lblTitle.Font      = UiTheme.FontTitle;
        _lblTitle.ForeColor = UiTheme.TextOnPrimary;
        _lblTitle.Margin    = new Padding(10, 12, 0, 0);
        _lblTitle.Dock      = DockStyle.Left;

        var actionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 8, 10, 0),
        };

        _lblSession.AutoSize  = true;
        _lblSession.Font      = UiTheme.FontSmall;
        _lblSession.ForeColor = Color.FromArgb(0xC8, 0xD6, 0xE8);
        _lblSession.Margin    = new Padding(0, 15, 14, 0);

        _btnManageUsers.Size   = new Size(104, 28);
        _btnManageUsers.Text   = "Manage Users";
        _btnManageUsers.Margin = new Padding(0, 0, 8, 0);
        UiTheme.StyleOnHeader(_btnManageUsers);
        _btnManageUsers.Click += (_, _) => new UserManagementForm().ShowDialog(this);

        _btnStartAll.Size = new Size(130, 28);
        _btnStartAll.Text = "Start All Bookings";
        _btnStartAll.Margin = new Padding(0, 0, 8, 0);
        UiTheme.StylePrimary(_btnStartAll);
        _btnStartAll.Click += (_, _) => StartAllBookings();

        _btnRefresh.Size = new Size(70, 28);
        _btnRefresh.Text = "Refresh";
        _btnRefresh.Margin = new Padding(0);
        UiTheme.StyleOnHeader(_btnRefresh);
        _btnRefresh.Click += (_, _) => { _allBookings = SavedBooking.LoadAll(); RebuildCards(); };

        actionsFlow.Controls.Add(_lblSession);
        actionsFlow.Controls.Add(_btnManageUsers);
        actionsFlow.Controls.Add(_btnStartAll);
        actionsFlow.Controls.Add(_btnRefresh);
        row1.Controls.Add(actionsFlow);
        row1.Controls.Add(_lblTitle);

        // Row 2: credentials + proxy, all in one flowing sequence.
        var row2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 6, 0, 0),
        };

        Label Lbl(string t) => new()
        {
            AutoSize = true, Font = UiTheme.FontLabel,
            ForeColor = Color.FromArgb(0xC8, 0xD6, 0xE8), Text = t,
            Margin = new Padding(0, 11, 4, 0),
        };

        void Txt(TextBox t, int width, bool pwd = false)
        {
            t.Font = UiTheme.FontBody; t.BorderStyle = BorderStyle.FixedSingle;
            t.Size = new Size(width, 26); t.Margin = new Padding(0, 4, 16, 0);
            if (pwd) t.PasswordChar = '*';
        }
        Txt(_txtUser, 120); Txt(_txtPass, 120, true); Txt(_txtProxy, 200);
        _txtProxy.Font = UiTheme.FontSmall;
        _txtProxy.Text = "user:pass@host:port";

        _btnSaveProxy.Size   = new Size(50, 28);
        _btnSaveProxy.Text   = "Set";
        _btnSaveProxy.Margin = new Padding(0, 3, 8, 0);
        UiTheme.StyleOnHeader(_btnSaveProxy);
        _btnSaveProxy.Click += (_, _) => SaveProxyFromTextBox();

        // Enable/Disable Proxy — just flips the saved setting; each booking
        // reads it fresh when it creates its own browser, so there's no
        // live shared browser here to reconfigure. AutoSize instead of a
        // fixed width: "Proxy: OFF" clipped to just "Proxy:" on some
        // DPI/font-rendering setups at a hand-picked 90px — since this
        // control already lives in a FlowLayoutPanel, letting it size
        // itself to its actual text removes that guess entirely.
        _btnToggleProxy.AutoSize     = true;
        _btnToggleProxy.AutoSizeMode = AutoSizeMode.GrowOnly;
        _btnToggleProxy.MinimumSize  = new Size(0, 28);
        _btnToggleProxy.Padding      = new Padding(10, 0, 10, 0);
        _btnToggleProxy.Margin       = new Padding(0, 3, 0, 0);
        _btnToggleProxy.Click       += (_, _) => ToggleProxy();
        UpdateProxyToggleButton();

        row2.Controls.AddRange(new Control[]
        {
            Lbl("USER"), _txtUser, Lbl("PASS"), _txtPass,
            Lbl("PROXY"), _txtProxy, _btnSaveProxy, _btnToggleProxy,
        });

        _topBar.Controls.Add(row2);
        _topBar.Controls.Add(row1);

        // The card list now fills the whole window — no more split-off
        // browser pane. Each booking's IRCTC page is hidden by default and
        // only shown via that card's own "Show Browser" button.
        _cardPanel.Dock          = DockStyle.Fill;
        _cardPanel.FlowDirection = FlowDirection.TopDown;
        _cardPanel.AutoScroll    = true;
        _cardPanel.WrapContents  = false;
        _cardPanel.BackColor     = UiTheme.Background;
        _cardPanel.Padding       = new Padding(8, 8, 8, 8);

        // Form
        Controls.Add(_cardPanel);
        Controls.Add(_topBar);
        BackColor    = UiTheme.Background;
        ClientSize   = new Size(1200, 680);
        MinimumSize  = new Size(920, 480);
        Text         = "IRCTC Booking Manager";
        StartPosition = FormStartPosition.CenterScreen;

        Resize += (_, _) => ResizeCards();
    }

    private void ResizeCards()
    {
        foreach (BookingCard c in _cardPanel.Controls)
            c.Width = _cardPanel.ClientSize.Width - 12;
    }

    // ── Card builder ──────────────────────────────────────────────────────
    private void RebuildCards()
    {
        // VIEW_ALL_BOOKINGS (Admin by default) sees every user's saved
        // bookings; everyone else only sees their own (CreatedByUsername).
        // A booking saved before that field existed has it blank, so it
        // only shows up for VIEW_ALL_BOOKINGS holders rather than being
        // guessed at or shown to everyone.
        var me = Session.CurrentUser?.Username ?? "";
        _bookings = Session.Has("VIEW_ALL_BOOKINGS")
            ? _allBookings
            : _allBookings.Where(b => b.CreatedByUsername == me).ToList();

        foreach (var c in _cards) c.Dispose();
        _cards.Clear();
        _sessions.Clear();
        _cardPanel.Controls.Clear();

        foreach (var b in _bookings)
        {
            var card = new BookingCard(b);
            card.Width = _cardPanel.ClientSize.Width - 12;
            card.OnBookClicked   += () => _ = StartBookingAsync(b, card);
            card.OnAckClicked    += () => { if (_sessions.TryGetValue(card, out var s)) s.AcknowledgeUserAction(); };
            // Removes from the FULL list (every user's bookings), not the
            // filtered _bookings view — deleting your own booking must not
            // wipe everyone else's out of the saved file.
            card.OnDeleteClicked += () => { _allBookings.Remove(b); SavedBooking.SaveAll(_allBookings); RebuildCards(); };
            // Users without MANAGE_BOOKINGS can view cards but not start or
            // delete bookings.
            card.SetActionsEnabled(Session.Has("MANAGE_BOOKINGS"));
            _cardPanel.Controls.Add(card);
            _cards.Add(card);
        }
    }

    // Show the captured UPI QR in its own always-on-top window (reused per booking).
    private void ShowQrPopup(SavedBooking b, System.Drawing.Bitmap bmp)
    {
        if (!_qrPopups.TryGetValue(b, out var popup) || popup.IsDisposed)
        {
            popup = new QrPopupForm($"[{b.TrainNo}] {b.TrainName}");
            popup.FormClosed += (_, _) => _qrPopups.Remove(b);
            _qrPopups[b] = popup;
        }
        popup.SetQr(bmp);
    }

    // The QR that ShowQrPopup last showed has disappeared from the live
    // page (payment completed, or the gateway moved on) — close the popup
    // instead of leaving a stale "scan to pay" window open.
    private void CloseQrPopup(SavedBooking b)
    {
        if (_qrPopups.TryGetValue(b, out var popup) && !popup.IsDisposed)
            popup.Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var popup in _qrPopups.Values.ToList())
            if (!popup.IsDisposed) popup.Close();
        _qrPopups.Clear();
        base.OnFormClosed(e);
    }

    // ── Proxy config ──────────────────────────────────────────────────────
    private void SaveProxyFromTextBox()
    {
        var text = _txtProxy.Text.Trim();

        var parsed = ProxyConfig.Parse(text, out var error);

        if (error != null)
        {
            MessageBox.Show($"Proxy format error:\n\n{error}\n\n" +
                $"Expected formats:\n" +
                $"  host:port:user:pass\n" +
                $"  user:pass@host:port\n" +
                $"  host:port",
                "Proxy Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!parsed.Enabled)
        {
            // User cleared the proxy
            _proxy.Enabled = false;
            _proxy.Host = ""; _proxy.Port = 0;
            _proxy.Username = ""; _proxy.Password = "";
            ProxyConfig.Save(_proxy);
            UpdateProxyToggleButton();
            MessageBox.Show("Proxy cleared. Bookings started after this will go direct.",
                "Proxy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Apply parsed values to the live config
        _proxy.Enabled  = true;
        _proxy.Host     = parsed.Host;
        _proxy.Port     = parsed.Port;
        _proxy.Username = parsed.Username;
        _proxy.Password = parsed.Password;
        ProxyConfig.Save(_proxy);
        UpdateProxyToggleButton();

        // Show diagnostic summary
        var diag = _proxy.DiagnosticSummary();
        if (_proxy.HasCredentials)
        {
            diag += $"\n\nProxy auth extension will be loaded at browser startup.";
        }

        MessageBox.Show(
            diag + "\n\nEach booking creates its own browser when started, so this " +
            "takes effect for bookings started from now on (not ones already running).",
            "Proxy Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Flips Enabled without touching the saved host/port/credentials, so
    // switching off and back on later doesn't require retyping the address.
    private void ToggleProxy()
    {
        if (!_proxy.Enabled && (string.IsNullOrWhiteSpace(_proxy.Host) || _proxy.Port <= 0))
        {
            MessageBox.Show("No proxy address is configured yet. Enter one in the Proxy field and click Set first.",
                "Proxy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _proxy.Enabled = !_proxy.Enabled;
        ProxyConfig.Save(_proxy);
        UpdateProxyToggleButton();
    }

    private void UpdateProxyToggleButton()
    {
        bool on = _proxy.IsConfigured;
        _btnToggleProxy.Text = on ? "Proxy: ON" : "Proxy: OFF";
        UiTheme.StyleToggle(_btnToggleProxy, on);
    }

    // ── Booking logic ─────────────────────────────────────────────────────
    // Each booking gets its own WebView2 + IrctcWebViewSession, isolated by
    // profile folder — this is what actually makes parallel bookings
    // possible (one shared browser can only run one script at a time; N
    // independent ones can run N bookings genuinely at once). The browser
    // is created off-screen (hidden per the "hide the IRCTC screen"
    // requirement) and handed to the card so its "Show Browser" button can
    // reveal it on demand if a manual step is needed.
    private async Task StartBookingAsync(SavedBooking booking, BookingCard card)
    {
        var u = _txtUser.Text.Trim();
        var p = _txtPass.Text.Trim();
        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
        {
            MessageBox.Show("Enter IRCTC credentials in the top bar.", "Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        card.SetBooking(true);

        var cardWebView = new WebView2 { Size = new Size(1280, 900), Location = new Point(-3000, -3000) };
        Controls.Add(cardWebView);
        card.AttachWebView(cardWebView);

        var session = new IrctcWebViewSession(
            cardWebView, _proxy, profileFolderName: $"WebView2-Booking-{booking.Id}", usingProxy: _proxy.IsConfigured);
        _sessions[card] = session;
        session.OnStatus  += msg => this.Invoke(() => card.SetStatus(msg));
        session.OnQrReady += bmp => this.Invoke(() => { card.ShowQr(bmp); ShowQrPopup(booking, bmp); });
        session.OnQrGone  += ()  => this.Invoke(() => CloseQrPopup(booking));

        try
        {
            await session.RunAsync(booking, u, p);
        }
        finally
        {
            card.SetBooking(false);
            _sessions.Remove(card);
        }
    }

    // Gap between launching each successive booking from "Start All" —
    // enough that N WebView2 instances aren't all spinning up and hitting
    // IRCTC in the exact same instant (that's just a burst of simultaneous
    // process/network start-up, nothing to do with pacing requests against
    // the site once each session is running), while still small enough that
    // "Start All" finishes dispatching everything in a few seconds.
    private const int StartAllStaggerMs = 4000;

    // Starts every booking's card with a short stagger between each launch
    // (not one-after-another waiting for completion, and not all in the
    // same instant either) — each still runs against its own isolated
    // browser once started, so they proceed genuinely in parallel from
    // there.
    //
    // Real-world caveat worth knowing before relying on this: IRCTC's own
    // site enforces a single active login session per account. Running the
    // SAME IRCTC account logged in from several parallel browsers at once
    // may get earlier sessions logged out by IRCTC itself, independent of
    // anything this app does — that's a constraint of the real site, not
    // something fixable here.
    private async void StartAllBookings()
    {
        var pairs = _bookings.Zip(_cards).ToList();
        for (int i = 0; i < pairs.Count; i++)
        {
            var (b, c) = pairs[i];
            _ = StartBookingAsync(b, c);
            if (i < pairs.Count - 1)
                await Task.Delay(StartAllStaggerMs);
        }
    }

    // Entry point for Form1's daily scheduler — same action as clicking
    // "Start All Bookings" by hand, still gated by whatever permission the
    // currently signed-in user actually has (the scheduler doesn't bypass
    // the rights system, it just fires within the already-open session
    // instead of waiting for a click).
    public bool TriggerAutoStartAll()
    {
        if (!Session.Has("MANAGE_BOOKINGS")) return false;
        StartAllBookings();
        return true;
    }
}

// ── BookingCard ───────────────────────────────────────────────────────────────
public class BookingCard : Panel
{
    private readonly Panel      _statusStrip;
    private readonly Label      _lblTrain;
    private readonly Label      _lblPax;
    private readonly Label      _lblStatus;
    private readonly Button     _btnBook;
    private readonly Button     _btnAck;
    private readonly Button     _btnDel;
    private readonly Button     _btnShowBrowser;
    private readonly PictureBox _picQr;

    // Each booking now runs its own isolated WebView2 (parallel bookings
    // can't share one browser), kept off-screen by default per the "hide
    // the IRCTC screen" requirement — "Show Browser" reparents it into a
    // popup on demand for the rare case Step-by-step automation needs a
    // human to click something it couldn't find itself (the "click
    // manually, then OK" messages this app has always shown).
    private WebView2? _webView;
    private BookingBrowserForm? _browserPopup;

    public event Action? OnBookClicked;
    public event Action? OnAckClicked;
    public event Action? OnDeleteClicked;

    public BookingCard(SavedBooking b)
    {
        Height      = 144;
        Dock        = DockStyle.None;
        BackColor   = UiTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        Margin      = new Padding(2, 2, 2, 6);
        Padding     = new Padding(0, 8, 8, 8); // no left padding: the status strip below sits flush against the edge

        // A thin colored strip along the left edge doubles as an at-a-glance
        // status indicator (grey=ready, blue=running, green=done, red=error)
        // without needing to read the status text — updated by SetStatus.
        _statusStrip = new Panel
        {
            Dock = DockStyle.Left, Width = 4, BackColor = UiTheme.Disabled,
        };

        _lblTrain = new Label
        {
            AutoSize  = false, Location = new Point(14, 8),
            Size      = new Size(196, 44),
            Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Text      = $"[{b.TrainNo}] {b.TrainName}\n{b.FromCode} → {b.ToCode}  {b.JourneyDate}\n{b.TravelClass} / {b.Quota}",
        };

        _lblPax = new Label
        {
            AutoSize  = false, Location = new Point(14, 54),
            Size      = new Size(196, 30),
            Font      = UiTheme.FontSmall,
            ForeColor = UiTheme.TextSecondary,
            Text      = b.Passengers.Count > 0
                ? string.Join(", ", b.Passengers.Select(p => $"{p.Name}({p.Age}{p.Gender})"))
                : "(no passengers)",
        };

        _lblStatus = new Label
        {
            AutoSize  = false, Location = new Point(14, 88),
            Size      = new Size(196, 40),
            Font      = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextSecondary,
            Text      = "Ready",
        };

        _btnBook = new Button
        {
            Location = new Point(214, 8), Size = new Size(92, 27),
            Text     = "Book on IRCTC",
        };
        UiTheme.StylePrimary(_btnBook);
        _btnBook.Font = UiTheme.FontButtonSmall;
        _btnBook.Click += (_, _) => OnBookClicked?.Invoke();

        _btnAck = new Button
        {
            Location = new Point(214, 42), Size = new Size(92, 25),
            Text     = "OK (Continue)", Enabled = false,
        };
        UiTheme.StyleSecondary(_btnAck);
        _btnAck.Click += (_, _) => OnAckClicked?.Invoke();

        _btnDel = new Button
        {
            Location  = new Point(214, 76), Size = new Size(92, 25),
            Text      = "Delete",
        };
        UiTheme.StyleDanger(_btnDel);
        _btnDel.Click += (_, _) => OnDeleteClicked?.Invoke();

        _picQr = new PictureBox
        {
            Location    = new Point(310, 6), Size = new Size(130, 130),
            SizeMode    = PictureBoxSizeMode.Zoom,
            BackColor   = UiTheme.CardAlt,
            BorderStyle = BorderStyle.FixedSingle,
            Visible     = false,
        };

        // Off by default (the IRCTC browser is hidden) — only lights up
        // once this card's booking has actually started and has a live
        // WebView2 for it to show (see AttachWebView).
        // VIEW_BROWSER-gated (Admin by default) — an Operator can watch a
        // booking's status/QR but not pop open its live IRCTC session.
        _btnShowBrowser = new Button
        {
            Location = new Point(456, 8), Size = new Size(96, 27),
            Text     = "Show Browser", Enabled = false,
            Visible  = Session.Has("VIEW_BROWSER"),
        };
        UiTheme.StyleSecondary(_btnShowBrowser);
        _btnShowBrowser.Click += (_, _) => ToggleBrowserPopup(b);

        Controls.AddRange(new Control[]
            { _lblTrain, _lblPax, _lblStatus, _btnBook, _btnAck, _btnDel, _btnShowBrowser, _picQr, _statusStrip });
    }

    // Called by BookingManagerForm right after it creates this card's own
    // WebView2 (one per booking, so parallel bookings each get an
    // independent browser instead of contending for one shared session).
    public void AttachWebView(WebView2 wv)
    {
        _webView = wv;
        _btnShowBrowser.Enabled = true;
    }

    private void ToggleBrowserPopup(SavedBooking b)
    {
        if (_webView == null) return;

        if (_browserPopup != null && !_browserPopup.IsDisposed)
        {
            _browserPopup.Close();
            return;
        }

        _browserPopup = new BookingBrowserForm($"[{b.TrainNo}] {b.TrainName}", _webView);
        _browserPopup.FormClosed += (_, _) =>
        {
            // Hand the webview back to this card (off-screen, hidden again)
            // instead of letting it get disposed with the popup.
            if (IsDisposed) return;
            _webView.Dock = DockStyle.None;
            Controls.Add(_webView);
            _webView.Size     = new Size(1280, 900);
            _webView.Location = new Point(-3000, -3000);
        };
        _browserPopup.Show();
    }

    public void SetStatus(string msg)
    {
        _lblStatus.Text = msg;
        bool waiting = msg.Contains("OK (Continue)", StringComparison.OrdinalIgnoreCase);
        _btnAck.Enabled = waiting;
        if (waiting) { _btnAck.BackColor = UiTheme.Warning; _btnAck.ForeColor = UiTheme.TextOnPrimary; }
        else UiTheme.StyleSecondary(_btnAck);

        // Color-code the status strip / text so a booking's state reads at
        // a glance without parsing the message.
        var lower = msg.ToLowerInvariant();
        Color c =
            lower.Contains("error") || lower.Contains("denied") || lower.Contains("failed") || lower.Contains("rejected")
                ? UiTheme.Danger
            : lower.Contains("qr") || lower.Contains("scan to pay") || lower.Contains("done")
                ? UiTheme.Accent
            : lower.Contains("step") || lower.Contains("running") || lower.Contains("checking") || waiting
                ? UiTheme.PrimaryLight
            : UiTheme.Disabled;
        _statusStrip.BackColor = c;
        _lblStatus.ForeColor   = c == UiTheme.Disabled ? UiTheme.TextSecondary : c;
    }

    public void ShowQr(System.Drawing.Bitmap bmp)
    {
        _picQr.Image   = bmp;
        _picQr.Visible = true;
        Height         = Math.Max(Height, 144);
    }

    public void SetBooking(bool running)
    {
        _btnBook.Enabled = !running;
        _btnBook.Text    = running ? "Running..." : "Book on IRCTC";
    }

    // Called once at card creation based on the signed-in user's
    // MANAGE_BOOKINGS permission — a restricted user can still see the
    // card's status/QR, just not trigger or remove a booking.
    public void SetActionsEnabled(bool enabled)
    {
        _btnBook.Enabled = enabled;
        _btnDel.Enabled  = enabled;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_browserPopup != null && !_browserPopup.IsDisposed) _browserPopup.Close();
            _webView?.Dispose();
        }
        base.Dispose(disposing);
    }
}
