using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Agent;
using indian_ticketing.AI.Configuration;
using indian_ticketing.AI.Goals;
using indian_ticketing.AI.Logging;
using indian_ticketing.AI.Observation;
using indian_ticketing.AI.Planning;
using indian_ticketing.AI.Verification;
using indian_ticketing.AI.WebsiteAdapters;

namespace indian_ticketing;

public class BookingManagerForm : Form
{
    // ── Controls ──────────────────────────────────────────────────────────
    private readonly SplitContainer _split       = new();
    private readonly Panel          _topBar      = new();
    private readonly Label          _lblTitle    = new();
    private readonly Label          _lblUser     = new();
    private readonly TextBox        _txtUser     = new();
    private readonly Label          _lblPass     = new();
    private readonly TextBox        _txtPass     = new();
    private readonly Label          _lblProxy    = new();
    private readonly TextBox        _txtProxy    = new();
    private readonly Button         _btnStartAll = new();
    private readonly Button         _btnRefresh  = new();
    private readonly Button         _btnSaveProxy = new();
    private readonly FlowLayoutPanel _cardPanel  = new();
    private readonly WebView2       _webView     = new();

    // ── State ─────────────────────────────────────────────────────────────
    private List<SavedBooking>        _bookings = SavedBooking.LoadAll();
    private readonly List<BookingCard> _cards   = new();
    private IrctcWebViewSession?       _session;
    private readonly ProxyConfig       _proxy   = ProxyConfig.Load();

    // One reusable QR popup window per booking — a refreshed QR replaces the
    // image in the existing window instead of opening a new one.
    private readonly Dictionary<SavedBooking, QrPopupForm> _qrPopups = new();

    public BookingManagerForm()
    {
        BuildUi();
        _txtUser.Text = "SEJAL124";
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
        RebuildCards();
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

    private async Task InitializeWebViewAsync()
    {
        var dataFolder = GetWebView2UserDataFolder();
        try
        {
            Directory.CreateDirectory(dataFolder);
            _webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = dataFolder
            };

            var envOptions = new CoreWebView2EnvironmentOptions();
            var proxyArg = _proxy.GetProxyServerArg();
            if (!string.IsNullOrEmpty(proxyArg))
            {
                envOptions.AdditionalBrowserArguments = proxyArg;
            }

            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, envOptions);
            await _webView.EnsureCoreWebView2Async(env);

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

            _webView.CoreWebView2?.Navigate("https://www.irctc.co.in/nget/train-search");

            // Auto-fill login form if it appears on the initial page
            _webView.CoreWebView2.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess) return;
                try
                {
                    await Task.Delay(3000); // wait for Angular to render
                    await _webView.CoreWebView2.ExecuteScriptAsync(IrctcWebViewSession.HelperJs);

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
                            _session = new IrctcWebViewSession(_webView, _proxy);
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

    // ── UI construction ───────────────────────────────────────────────────
    private void BuildUi()
    {
        // Top bar
        _topBar.Dock      = DockStyle.Top;
        _topBar.Height    = 52;
        _topBar.BackColor = Color.FromArgb(30, 144, 255);
        _topBar.Padding   = new Padding(6, 6, 6, 0);

        _lblTitle.AutoSize  = true;
        _lblTitle.Font      = new Font("Segoe UI", 12F, FontStyle.Bold);
        _lblTitle.ForeColor = Color.White;
        _lblTitle.Location  = new Point(8, 12);
        _lblTitle.Text      = "IRCTC Booking Manager";

        void Lbl(Label l, string t, int x)
        {
            l.AutoSize = true; l.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            l.ForeColor = Color.White; l.Location = new Point(x, 16); l.Text = t;
        }
        Lbl(_lblUser, "User:", 230);
        Lbl(_lblPass, "Pass:", 380);
        Lbl(_lblProxy, "Proxy:", 548);

        void Txt(TextBox t, int x, bool pwd = false)
        {
            t.Font = new Font("Segoe UI", 9F); t.Location = new Point(x, 12);
            t.Size = new Size(120, 24); if (pwd) t.PasswordChar = '*';
        }
        Txt(_txtUser, 270); Txt(_txtPass, 418, true);
        _txtProxy.Font = new Font("Segoe UI", 8F);
        _txtProxy.Location = new Point(590, 12);
        _txtProxy.Size = new Size(230, 24);
        _txtProxy.Text = "user:pass@host:port";

        _btnSaveProxy.Font = new Font("Segoe UI", 8F);
        _btnSaveProxy.Location = new Point(826, 10);
        _btnSaveProxy.Size = new Size(50, 28);
        _btnSaveProxy.Text = "Set";
        _btnSaveProxy.BackColor = Color.FromArgb(80, 80, 80);
        _btnSaveProxy.ForeColor = Color.White;
        _btnSaveProxy.FlatStyle = FlatStyle.Flat;
        _btnSaveProxy.Click += (_, _) => SaveProxyFromTextBox();

        _btnStartAll.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
        _btnStartAll.Location  = new Point(882, 10);
        _btnStartAll.Size      = new Size(130, 28);
        _btnStartAll.Text      = "Start All Bookings";
        _btnStartAll.BackColor = Color.FromArgb(0, 110, 200);
        _btnStartAll.ForeColor = Color.White;
        _btnStartAll.FlatStyle = FlatStyle.Flat;
        _btnStartAll.Click    += (_, _) => StartAllBookings();

        _btnRefresh.Font      = new Font("Segoe UI", 9F);
        _btnRefresh.Location  = new Point(1018, 10);
        _btnRefresh.Size      = new Size(70, 28);
        _btnRefresh.Text      = "Refresh";
        _btnRefresh.UseVisualStyleBackColor = true;
        _btnRefresh.Click += (_, _) => { _bookings = SavedBooking.LoadAll(); RebuildCards(); };

        _topBar.Controls.AddRange(new Control[] {
            _lblTitle, _lblUser, _txtUser, _lblPass, _txtPass,
            _lblProxy, _txtProxy, _btnSaveProxy, _btnStartAll, _btnRefresh
        });

        // Split container — SplitterDistance set in Shown (layout is complete by then)
        _split.Dock          = DockStyle.Fill;
        _split.Panel1MinSize = 280;
        _split.Panel2MinSize = 100;
        _split.BorderStyle   = BorderStyle.None;

        // Left: scrollable card panel
        _cardPanel.Dock          = DockStyle.Fill;
        _cardPanel.FlowDirection = FlowDirection.TopDown;
        _cardPanel.AutoScroll    = true;
        _cardPanel.WrapContents  = false;
        _cardPanel.BackColor     = Color.FromArgb(240, 243, 250);
        _cardPanel.Padding       = new Padding(4, 4, 4, 4);
        _split.Panel1.Controls.Add(_cardPanel);

        // Right: WebView2 (embedded IRCTC browser)
        _webView.Dock = DockStyle.Fill;
        _split.Panel2.Controls.Add(_webView);

        // Form
        Controls.Add(_split);
        Controls.Add(_topBar);
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
            card.OnAiBookClicked += () => StartAiBooking(b, card);
            card.OnAckClicked    += () => _session?.AcknowledgeUserAction();
            card.OnDeleteClicked += () => { _bookings.Remove(b); SavedBooking.SaveAll(_bookings); RebuildCards(); };
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

        // Show diagnostic summary
        var diag = _proxy.DiagnosticSummary();
        if (_proxy.HasCredentials)
        {
            diag += $"\n\nProxy auth extension will be loaded at browser startup.";
        }

        MessageBox.Show($"{diag}\n\nNote: Proxy changes take effect for NEW browser sessions.\n" +
            $"If a WebView2 session is already running, close and reopen the Booking Manager.",
            "Proxy Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        _session = new IrctcWebViewSession(_webView, _proxy);
        _session.OnStatus  += msg => this.Invoke(() => card.SetStatus(msg));
        _session.OnQrReady += bmp => this.Invoke(() => { card.ShowQr(bmp); ShowQrPopup(booking, bmp); });

        await _session.RunAsync(booking, u, p);
        card.SetBooking(false);
    }

    // ── Adaptive AI Agent booking (opt-in, alongside the deterministic engine above) ───────
    private async void StartAiBooking(SavedBooking booking, BookingCard card)
    {
        var u = _txtUser.Text.Trim();
        var p = _txtPass.Text.Trim();
        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
        {
            MessageBox.Show("Enter IRCTC credentials in the top bar.", "Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var config = AiAgentConfigFile.Load();
        if (!config.AiAgent.Enabled)
        {
            MessageBox.Show("The AI agent is disabled in ai_agent_config.json.", "AI Agent Disabled",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        card.SetAiBooking(true);

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IndianTicketing", "ai_agent_debug");
        var logFile = Path.Combine(logDir, $"{booking.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var logger = new AgentLogger(config.AiAgent.DebugMode, new[] { u, p }, logFile);

        using var ollama = new OllamaClient(config.Ollama.BaseUrl, config.Ollama.TimeoutSeconds);
        var router = new AiModelRouter(
            ollama, config.Ollama.FastModel, config.Ollama.ReasoningModel,
            config.AiAgent.ConfidenceThreshold, msg => logger.Log(msg));

        var observer = new WebViewPageObserver(_webView, new IWebsiteAdapter[] { new IrctcWebsiteAdapter() });
        var validator = new ActionValidator();
        var executor = new ActionExecutor(observer, key => key switch
        {
            "SECURE_USERNAME" => u,
            "SECURE_PASSWORD" => p,
            _ => null,
        });
        var verifier = new ActionVerifier();

        var agent = new AiBrowserAgent(observer, router, validator, executor, verifier, config.AiAgent, logger);
        agent.OnStatus += msg => this.Invoke(() => card.SetStatus(msg));

        try
        {
            var result = await agent.RunAsync(BookingGoal.FromSavedBooking(booking));
            card.SetStatus(result.Outcome switch
            {
                AgentOutcome.Completed => "AI Agent: booking objective completed.",
                AgentOutcome.HumanInterventionRequired => $"AI Agent: needs help — {result.Reason}",
                AgentOutcome.Failed => $"AI Agent failed: {result.Reason}",
                AgentOutcome.StepLimitReached => "AI Agent: step limit reached.",
                _ => "AI Agent: stopped.",
            });
        }
        catch (Exception ex)
        {
            card.SetStatus($"AI Agent error: {ex.Message}");
        }
        finally
        {
            card.SetAiBooking(false);
        }
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
        _session = new IrctcWebViewSession(_webView, _proxy);
        _session.OnStatus  += msg => this.Invoke(() => c.SetStatus(msg));
        _session.OnQrReady += bmp => this.Invoke(() => { c.ShowQr(bmp); ShowQrPopup(b, bmp); });

        await _session.RunAsync(b, u, p);
        c.SetBooking(false);

        // Move to next booking
        StartChain(index + 1);
    }
}

// ── BookingCard ───────────────────────────────────────────────────────────────
public class BookingCard : Panel
{
    private readonly Label      _lblTrain;
    private readonly Label      _lblPax;
    private readonly Label      _lblStatus;
    private readonly Button     _btnBook;
    private readonly Button     _btnAiBook;
    private readonly Button     _btnAck;
    private readonly Button     _btnDel;
    private readonly PictureBox _picQr;

    public event Action? OnBookClicked;
    public event Action? OnAiBookClicked;
    public event Action? OnAckClicked;
    public event Action? OnDeleteClicked;

    public BookingCard(SavedBooking b)
    {
        Height      = 140;
        Dock        = DockStyle.None;
        BackColor   = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Margin      = new Padding(2, 2, 2, 4);
        Padding     = new Padding(6);

        _lblTrain = new Label
        {
            AutoSize  = false, Location = new Point(6, 6),
            Size      = new Size(196, 42),
            Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Text      = $"[{b.TrainNo}] {b.TrainName}\n{b.FromCode} → {b.ToCode}  {b.JourneyDate}\n{b.TravelClass} / {b.Quota}",
        };

        _lblPax = new Label
        {
            AutoSize  = false, Location = new Point(6, 52),
            Size      = new Size(196, 30),
            Font      = new Font("Segoe UI", 7.5F),
            ForeColor = Color.DimGray,
            Text      = b.Passengers.Count > 0
                ? string.Join(", ", b.Passengers.Select(p => $"{p.Name}({p.Age}{p.Gender})"))
                : "(no passengers)",
        };

        _lblStatus = new Label
        {
            AutoSize  = false, Location = new Point(6, 86),
            Size      = new Size(196, 40),
            Font      = new Font("Segoe UI", 7.5F),
            ForeColor = Color.Navy,
            Text      = "Ready",
        };

        _btnBook = new Button
        {
            Location = new Point(206, 6), Size = new Size(88, 26),
            Text     = "Book on IRCTC",
            Font     = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            UseVisualStyleBackColor = true,
        };
        _btnBook.Click += (_, _) => OnBookClicked?.Invoke();

        _btnAck = new Button
        {
            Location = new Point(206, 36), Size = new Size(88, 24),
            Text     = "OK (Continue)", Font = new Font("Segoe UI", 7.5F),
            UseVisualStyleBackColor = true, Enabled = false,
        };
        _btnAck.Click += (_, _) => OnAckClicked?.Invoke();

        _btnDel = new Button
        {
            Location  = new Point(206, 64), Size = new Size(88, 24),
            Text      = "Delete", Font = new Font("Segoe UI", 7.5F),
            ForeColor = Color.Firebrick,
            UseVisualStyleBackColor = true,
        };
        _btnDel.Click += (_, _) => OnDeleteClicked?.Invoke();

        _btnAiBook = new Button
        {
            Location  = new Point(206, 92), Size = new Size(88, 24),
            Text      = "Book (AI Agent)", Font = new Font("Segoe UI", 7F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 100, 0),
            UseVisualStyleBackColor = true,
        };
        _btnAiBook.Click += (_, _) => OnAiBookClicked?.Invoke();

        _picQr = new PictureBox
        {
            Location    = new Point(300, 4), Size = new Size(130, 130),
            SizeMode    = PictureBoxSizeMode.Zoom,
            BackColor   = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Visible     = false,
        };

        Controls.AddRange(new Control[] { _lblTrain, _lblPax, _lblStatus, _btnBook, _btnAiBook, _btnAck, _btnDel, _picQr });
    }

    public void SetStatus(string msg)
    {
        _lblStatus.Text = msg;
        bool waiting = msg.Contains("OK (Continue)", StringComparison.OrdinalIgnoreCase);
        _btnAck.Enabled  = waiting;
        _btnAck.BackColor = waiting ? Color.LightYellow : SystemColors.Control;
    }

    public void ShowQr(System.Drawing.Bitmap bmp)
    {
        _picQr.Image   = bmp;
        _picQr.Visible = true;
        Height         = Math.Max(Height, 140);
    }

    public void SetBooking(bool running)
    {
        _btnBook.Enabled = !running;
        _btnBook.Text    = running ? "Running..." : "Book on IRCTC";
    }

    public void SetAiBooking(bool running)
    {
        _btnAiBook.Enabled = !running;
        _btnAiBook.Text    = running ? "AI Running..." : "Book (AI Agent)";
    }
}
