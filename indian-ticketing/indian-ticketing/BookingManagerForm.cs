using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace indian_ticketing;

public class BookingManagerForm : Form
{
    // ── Controls ──────────────────────────────────────────────────────────
    private readonly SplitContainer _split       = new();
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
    private WebView2                _webView     = new();

    // ── State ─────────────────────────────────────────────────────────────
    private List<SavedBooking>        _bookings = SavedBooking.LoadAll();
    private readonly List<BookingCard> _cards   = new();
    private IrctcWebViewSession?       _session;
    private readonly ProxyConfig       _proxy   = ProxyConfig.Load();
    // Tracks which network mode _webView is actually running on right now —
    // set by SetupWebViewAsync — so a session created for booking can label
    // its own Access-Denied diagnostics correctly (direct vs proxy).
    private bool                       _usingProxy;

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
        Shown += (_, _) => _split.SplitterDistance = 320;  // layout complete here
        Load  += async (_, _) => await InitializeWebViewAsync();
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

    private static string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Indian Ticketing",
            "WebView2");
    }

    private async Task<bool> EvalBool(string js)
    {
        try
        {
            var r = await _webView.CoreWebView2.ExecuteScriptAsync(js);
            return r.Trim('"') is "true" or "1";
        }
        catch { return false; }
    }

    // Use the explicitly configured network mode. A proxy is infrastructure
    // configuration, never a fallback used to respond to an access block.
    private async Task InitializeWebViewAsync() => await SetupWebViewAsync(useProxy: _proxy.IsConfigured);

    private static bool IsProfileLockError(Exception ex)
        => ex is System.Runtime.InteropServices.COMException com && (uint)com.HResult == 0x8007139F;

    private async Task InitCoreWebView2Async(string dataFolder, bool useProxy)
    {
        Directory.CreateDirectory(dataFolder);
        // No CreationProperties assignment here: it's only consulted by the
        // control's OWN implicit init path, and only if set BEFORE the
        // control gets a window handle (i.e. before it's added to a visible
        // parent) — by the time this runs, _webView is already parented, so
        // WebView2 has already begun its own init and setting this now
        // throws "CreationProperties cannot be modified after the
        // initialization of CoreWebView2 has begun." Passing an explicit
        // environment to EnsureCoreWebView2Async(env) below (which already
        // has dataFolder baked in) makes this assignment unnecessary anyway.

        var envOptions = new CoreWebView2EnvironmentOptions();
        if (useProxy)
        {
            var proxyArg = _proxy.GetProxyServerArg();
            if (!string.IsNullOrEmpty(proxyArg))
            {
                envOptions.AdditionalBrowserArguments = proxyArg;
            }
        }

        var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, envOptions);
        await _webView.EnsureCoreWebView2Async(env);

        // Auto-answer the native proxy-auth dialog ("Sign in to access this
        // site") with the configured credentials, so it never blocks the UI
        // waiting on a manual Username/Password/Sign in. IrctcWebViewSession
        // already does this for the sessions IT initializes, but _webView is
        // initialized directly by THIS form (booking sessions just reuse the
        // already-created CoreWebView2), so it needs its own subscription too.
        if (useProxy && _proxy.HasCredentials)
        {
            _webView.CoreWebView2.BasicAuthenticationRequested += (s, e) =>
            {
                e.Response.UserName = _proxy.Username;
                e.Response.Password = _proxy.Password;
            };
        }

        if (useProxy)
        {
            // Load proxy auth extension AFTER profile is available
            var extPath = ProxyConfig.EnsureAuthExtension(_proxy);
            if (extPath != null && _webView.CoreWebView2?.Profile != null)
            {
                try
                {
                    await _webView.CoreWebView2.Profile.AddBrowserExtensionAsync(extPath);
                }
                catch { /* Extension may already be loaded from a previous session */ }
            }
        }
    }

    private async Task SetupWebViewAsync(bool useProxy)
    {
        _usingProxy = useProxy;
        var dataFolder = GetWebView2UserDataFolder();
        try
        {
            try
            {
                await InitCoreWebView2Async(dataFolder, useProxy);
            }
            catch (Exception ex) when (IsProfileLockError(ex))
            {
                // HRESULT 0x8007139F: another process still holds this profile
                // folder's lock (a WebView2 child process left running after an
                // abrupt stop — common while iterating via a debugger). Fall
                // back to a fresh, uniquely named profile for this run instead
                // of failing hard.
                dataFolder = $"{dataFolder}-{DateTime.Now:yyyyMMddHHmmss}";
                await InitCoreWebView2Async(dataFolder, useProxy);
            }

            var core = _webView.CoreWebView2;
            if (core == null) throw new InvalidOperationException("WebView2 initialization did not produce a browser core.");
            core.Navigate("https://www.irctc.co.in/nget/train-search");

            // Auto-fill login form if it appears on the initial page
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess) return;
                try
                {
                    await Task.Delay(3000); // wait for Angular to render
                    await core.ExecuteScriptAsync(IrctcWebViewSession.HelperJs);

                    bool blocked = await EvalBool(
                        "__h.pageHas('Access Denied') && __h.pageHas('have permission')");
                    if (blocked)
                    {
                        await AccessDeniedDiagnostics.CaptureAsync(_webView.CoreWebView2,
                            AutomationFailureKind.AccessDenied, detail: "Initial navigation", useProxy: useProxy, proxy: _proxy);
                        MessageBox.Show(AccessDeniedDiagnostics.UserMessage(AutomationFailureKind.AccessDenied),
                            "IRCTC Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    bool hasLogin = await EvalBool(
                        @"__h.exists('input[placeholder=""User Name""]') ||
                          __h.exists('input[formcontrolname=""userid""]') ||
                          __h.pageHas('Please login') || __h.pageHas('Login to proceed')");

                    if (hasLogin)
                    {
                        var u = _txtUser.Text.Trim();
                        var p = _txtPass.Text.Trim();
                        if (!string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p))
                        {
                            _session = new IrctcWebViewSession(_webView, _proxy, usingProxy: _usingProxy);
                            await _session.LoginAsync(u, p);
                        }
                    }
                }
                catch { }
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize embedded browser.\n\n" +
                $"Please ensure the application can write to this folder:\n{dataFolder}\n\n" +
                $"Error: {ex.Message}",
                "WebView2 Initialization Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Same constraint as above, generalized: switch the live browser between
    // direct and proxy right here, without requiring the user to close and
    // reopen the Booking Manager for a network-mode change to take effect.
    private async Task SwitchWebViewNetworkModeAsync(bool useProxy)
    {
        var old = _webView;
        _split.Panel2.Controls.Remove(old);
        old.Dispose();

        _webView = new WebView2 { Dock = DockStyle.Fill };
        _split.Panel2.Controls.Add(_webView);

        await SetupWebViewAsync(useProxy);
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
        _btnRefresh.Click += (_, _) => { _bookings = SavedBooking.LoadAll(); RebuildCards(); };

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
        _btnSaveProxy.Click += async (_, _) => await SaveProxyFromTextBoxAsync();

        // Enable/Disable Proxy — switches the live browser between direct
        // and proxy right here (no closing/reopening this window needed).
        // Label reflects current state; UpdateProxyToggleButton keeps it in
        // sync after every change (Set, toggle, or load).
        _btnToggleProxy.Size   = new Size(90, 28);
        _btnToggleProxy.Margin = new Padding(0, 3, 0, 0);
        _btnToggleProxy.Click += async (_, _) => await ToggleProxyAsync();
        UpdateProxyToggleButton();

        row2.Controls.AddRange(new Control[]
        {
            Lbl("USER"), _txtUser, Lbl("PASS"), _txtPass,
            Lbl("PROXY"), _txtProxy, _btnSaveProxy, _btnToggleProxy,
        });

        _topBar.Controls.Add(row2);
        _topBar.Controls.Add(row1);

        // Split container — SplitterDistance set in Shown (layout is complete by then)
        _split.Dock          = DockStyle.Fill;
        _split.Panel1MinSize = 280;
        _split.Panel2MinSize = 100;
        _split.BorderStyle   = BorderStyle.None;
        _split.BackColor     = UiTheme.Border; // shows through as a thin splitter line

        // Left: scrollable card panel
        _cardPanel.Dock          = DockStyle.Fill;
        _cardPanel.FlowDirection = FlowDirection.TopDown;
        _cardPanel.AutoScroll    = true;
        _cardPanel.WrapContents  = false;
        _cardPanel.BackColor     = UiTheme.Background;
        _cardPanel.Padding       = new Padding(8, 8, 8, 8);
        _split.Panel1.Controls.Add(_cardPanel);

        // Right: WebView2 (embedded IRCTC browser)
        _webView.Dock = DockStyle.Fill;
        _split.Panel2.Controls.Add(_webView);

        // Form
        Controls.Add(_split);
        Controls.Add(_topBar);
        BackColor    = UiTheme.Background;
        ClientSize   = new Size(1200, 680);
        MinimumSize  = new Size(920, 480);
        Text         = "IRCTC Booking Manager";
        StartPosition = FormStartPosition.CenterScreen;
    }

    // ── Card builder ──────────────────────────────────────────────────────
    private void RebuildCards()
    {
        foreach (var c in _cards) c.Dispose();
        _cards.Clear();
        _cardPanel.Controls.Clear();

        foreach (var b in _bookings)
        {
            var card = new BookingCard(b);
            card.Width = _split.Panel1.ClientSize.Width - 12;
            card.OnBookClicked   += () => StartBooking(b, card);
            card.OnAckClicked    += () => _session?.AcknowledgeUserAction();
            card.OnDeleteClicked += () => { _bookings.Remove(b); SavedBooking.SaveAll(_bookings); RebuildCards(); };
            // Users without MANAGE_BOOKINGS can view cards but not start or
            // delete bookings.
            card.SetActionsEnabled(Session.Has("MANAGE_BOOKINGS"));
            _cardPanel.Controls.Add(card);
            _cards.Add(card);
        }

        _split.Panel1.Resize += (_, _) =>
        {
            foreach (BookingCard c in _cardPanel.Controls)
                c.Width = _split.Panel1.ClientSize.Width - 12;
        };
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
    private async Task SaveProxyFromTextBoxAsync()
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
            await SwitchWebViewNetworkModeAsync(useProxy: false);
            MessageBox.Show("Proxy cleared. All requests will go direct.",
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
        await SwitchWebViewNetworkModeAsync(useProxy: true);

        // Show diagnostic summary
        var diag = _proxy.DiagnosticSummary();
        if (_proxy.HasCredentials)
        {
            diag += $"\n\nProxy auth extension will be loaded at browser startup.";
        }

        MessageBox.Show(diag, "Proxy Saved — now active", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Flips Enabled without touching the saved host/port/credentials, so
    // switching off and back on later doesn't require retyping the address.
    private async Task ToggleProxyAsync()
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
        await SwitchWebViewNetworkModeAsync(useProxy: _proxy.IsConfigured);
    }

    private void UpdateProxyToggleButton()
    {
        bool on = _proxy.IsConfigured;
        _btnToggleProxy.Text = on ? "Proxy: ON" : "Proxy: OFF";
        UiTheme.StyleToggle(_btnToggleProxy, on);
    }

    // ── Booking logic ─────────────────────────────────────────────────────
    private async void StartBooking(SavedBooking booking, BookingCard card)
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
        _session = new IrctcWebViewSession(_webView, _proxy, usingProxy: _usingProxy);
        _session.OnStatus  += msg => this.Invoke(() => card.SetStatus(msg));
        _session.OnQrReady += bmp => this.Invoke(() => { card.ShowQr(bmp); ShowQrPopup(booking, bmp); });
        _session.OnQrGone  += ()  => this.Invoke(() => CloseQrPopup(booking));

        await _session.RunAsync(booking, u, p);
        card.SetBooking(false);
    }

    private void StartAllBookings()
    {
        if (_cards.Count == 0) return;
        // Chain: start first, then each subsequent one when the previous finishes
        StartChain(0);
    }

    private async void StartChain(int index)
    {
        if (index >= _bookings.Count) return;
        var u = _txtUser.Text.Trim();
        var p = _txtPass.Text.Trim();
        var b = _bookings[index];
        var c = _cards[index];

        c.SetBooking(true);
        _session = new IrctcWebViewSession(_webView, _proxy, usingProxy: _usingProxy);
        _session.OnStatus  += msg => this.Invoke(() => c.SetStatus(msg));
        _session.OnQrReady += bmp => this.Invoke(() => { c.ShowQr(bmp); ShowQrPopup(b, bmp); });
        _session.OnQrGone  += ()  => this.Invoke(() => CloseQrPopup(b));

        await _session.RunAsync(b, u, p);
        c.SetBooking(false);

        // Move to next booking
        StartChain(index + 1);
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
    private readonly PictureBox _picQr;

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

        Controls.AddRange(new Control[]
            { _lblTrain, _lblPax, _lblStatus, _btnBook, _btnAck, _btnDel, _picQr, _statusStrip });
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
}
