using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// Shown before Form1. Two modes, chosen automatically:
///   - First run (no rows in dbo.Users yet): forces creating the first
///     Admin account instead of a normal login — there's nothing to log
///     into otherwise.
///   - Normal run: username/password against AuthRepository (SQL Server).
///     Success calls Session.SignIn and closes with DialogResult.OK;
///     Program.cs only opens Form1 if that happened.
/// Program.cs calls AuthDatabase.EnsureReady() before this form is shown,
/// so the schema/seed data already exist by the time HasAnyUsers() runs.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly bool _firstRun;

    private readonly Label   _lblTitle    = new();
    private readonly Label   _lblSubtitle = new();
    private readonly Label   _lblUser     = new();
    private readonly TextBox _txtUser     = new();
    private readonly Label   _lblPass     = new();
    private readonly TextBox _txtPass     = new();
    private readonly Label   _lblConfirm  = new();
    private readonly TextBox _txtConfirm  = new();
    private readonly Label   _lblError    = new();
    private readonly Button  _btnGo       = new();

    public LoginForm()
    {
        _firstRun = !AuthRepository.HasAnyUsers();
        BuildUi();
    }

    private void BuildUi()
    {
        Text            = "IRCTC Booking Manager — Sign In";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(360, _firstRun ? 340 : 300);
        BackColor       = UiTheme.Surface;
        AcceptButton    = _btnGo;

        var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = UiTheme.Primary };
        _lblTitle.AutoSize  = true;
        _lblTitle.Font      = UiTheme.FontHeader;
        _lblTitle.ForeColor = UiTheme.TextOnPrimary;
        _lblTitle.Location  = new Point(20, 12);
        _lblTitle.Text      = "IRCTC Booking Manager";

        _lblSubtitle.AutoSize  = true;
        _lblSubtitle.Font      = UiTheme.FontSmall;
        _lblSubtitle.ForeColor = UiTheme.TextOnPrimary;
        _lblSubtitle.Location  = new Point(20, 42);
        _lblSubtitle.Text      = _firstRun
            ? "Create the first Admin account to get started"
            : "Sign in to continue";
        header.Controls.Add(_lblTitle);
        header.Controls.Add(_lblSubtitle);

        void Lbl(Label l, string t, int y)
        {
            l.AutoSize = true; l.Font = UiTheme.FontBody;
            l.ForeColor = UiTheme.TextSecondary;
            l.Location = new Point(20, y); l.Text = t;
        }
        void Txt(TextBox t, int y, bool pwd = false)
        {
            UiTheme.StyleTextBox(t);
            t.Font = new Font("Segoe UI", 10.5F);
            t.Location = new Point(20, y);
            t.Size = new Size(320, 28);
            if (pwd) t.PasswordChar = '*';
        }

        int y = 90;
        Lbl(_lblUser, "Username", y); y += 20;
        Txt(_txtUser, y); y += 40;

        Lbl(_lblPass, "Password", y); y += 20;
        Txt(_txtPass, y, pwd: true); y += 40;

        if (_firstRun)
        {
            Lbl(_lblConfirm, "Confirm Password", y); y += 20;
            Txt(_txtConfirm, y, pwd: true); y += 40;
        }

        _lblError.AutoSize    = true;
        _lblError.ForeColor   = UiTheme.Danger;
        _lblError.Font        = UiTheme.FontSmall;
        _lblError.Location    = new Point(20, y);
        _lblError.MaximumSize = new Size(320, 0);
        y += 26;

        _btnGo.Location = new Point(20, y);
        _btnGo.Size     = new Size(320, 34);
        _btnGo.Text     = _firstRun ? "Create Admin Account" : "Sign In";
        UiTheme.StylePrimary(_btnGo);
        _btnGo.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _btnGo.Click += (_, _) => { if (_firstRun) TryCreateAdmin(); else TryLogin(); };

        Controls.Add(_lblUser); Controls.Add(_txtUser);
        Controls.Add(_lblPass); Controls.Add(_txtPass);
        if (_firstRun) { Controls.Add(_lblConfirm); Controls.Add(_txtConfirm); }
        Controls.Add(_lblError);
        Controls.Add(_btnGo);
        Controls.Add(header);
    }

    private void TryLogin()
    {
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text;

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            _lblError.Text = "Enter both username and password.";
            return;
        }

        UserAccount? account;
        try { account = AuthRepository.Authenticate(user, pass); }
        catch (Exception ex)
        {
            _lblError.Text = $"Couldn't reach the database: {ex.Message}";
            return;
        }

        if (account == null)
        {
            _lblError.Text = "Incorrect username or password.";
            _txtPass.Clear();
            _txtPass.Focus();
            return;
        }

        Session.SignIn(account);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void TryCreateAdmin()
    {
        var user    = _txtUser.Text.Trim();
        var pass    = _txtPass.Text;
        var confirm = _txtConfirm.Text;

        if (user.Length < 3) { _lblError.Text = "Username must be at least 3 characters."; return; }
        if (pass.Length < 6) { _lblError.Text = "Password must be at least 6 characters."; return; }
        if (pass != confirm) { _lblError.Text = "Passwords don't match."; return; }

        try
        {
            var adminRole = AuthRepository.GetAllRoles().FirstOrDefault(r => r.RoleName == "Admin");
            if (adminRole == null)
            {
                _lblError.Text = "Admin role not found — database wasn't seeded correctly.";
                return;
            }
            if (AuthRepository.UsernameExists(user))
            {
                _lblError.Text = "That username already exists.";
                return;
            }

            AuthRepository.CreateUser(user, pass, adminRole.RoleId);
            var account = AuthRepository.Authenticate(user, pass);
            if (account == null) { _lblError.Text = "Account created, but sign-in failed — try again."; return; }

            Session.SignIn(account);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Couldn't reach the database: {ex.Message}";
        }
    }
}
