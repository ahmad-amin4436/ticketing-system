using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// Requires MANAGE_USERS: add/remove users, change roles, reset passwords.
/// Roles themselves (and which permissions each grants) are managed
/// separately via RoleManagementForm — this screen only assigns users to
/// whatever roles already exist. Never shows password hashes. Guards
/// against removing/demoting the last remaining Admin, which would lock
/// everyone out of user management.
/// </summary>
public sealed class UserManagementForm : Form
{
    private readonly DataGridView _grid = new();
    private List<UserAccount> _users = new();
    private List<RoleInfo> _roles = new();

    public UserManagementForm()
    {
        BuildUi();
        RefreshData();
    }

    private void BuildUi()
    {
        Text          = "Manage Users";
        ClientSize    = new Size(600, 440);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize   = new Size(480, 320);
        BackColor     = UiTheme.Surface;

        _grid.Dock                  = DockStyle.Fill;
        _grid.AutoGenerateColumns   = false;
        _grid.AllowUserToAddRows    = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly              = true;
        _grid.SelectionMode         = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect           = false;
        _grid.RowHeadersVisible     = false;
        _grid.BorderStyle           = BorderStyle.None;
        _grid.BackgroundColor       = UiTheme.Background;
        _grid.Font                  = UiTheme.FontBody;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Primary;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextOnPrimary;
        _grid.EnableHeadersVisualStyles = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = "Username", HeaderText = "Username", Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = "RoleName", HeaderText = "Role", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = "IsActive", HeaderText = "Active", Width = 80 });

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8),
        };

        Button Btn(string text)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(4, 4, 4, 4), Padding = new Padding(8, 4, 8, 4) };
            UiTheme.StyleSecondary(b);
            return b;
        }

        var btnAdd    = Btn("Add User");
        var btnRole   = Btn("Change Role");
        var btnReset  = Btn("Reset Password");
        var btnDelete = Btn("Delete User");
        var btnRoles  = Btn("Manage Roles");
        var btnClose  = Btn("Close");
        UiTheme.StyleDanger(btnDelete);

        btnAdd.Click    += (_, _) => AddUser();
        btnRole.Click   += (_, _) => ChangeRole();
        btnReset.Click  += (_, _) => ResetPassword();
        btnDelete.Click += (_, _) => DeleteUser();
        btnRoles.Click  += (_, _) => { new RoleManagementForm().ShowDialog(this); RefreshData(); };
        btnClose.Click  += (_, _) => Close();

        bar.Controls.AddRange(new Control[] { btnAdd, btnRole, btnReset, btnDelete, btnRoles, btnClose });

        Controls.Add(_grid);
        Controls.Add(bar);
    }

    private void RefreshData()
    {
        try
        {
            _users = AuthRepository.GetAllUsers();
            _roles = AuthRepository.GetAllRoles();
            _grid.DataSource = _users;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load users: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private UserAccount? SelectedUser()
    {
        if (_grid.SelectedRows.Count == 0) return null;
        var idx = _grid.SelectedRows[0].Index;
        return idx >= 0 && idx < _users.Count ? _users[idx] : null;
    }

    private void AddUser()
    {
        if (_roles.Count == 0)
        {
            MessageBox.Show("No roles exist yet — create one first via Manage Roles.", "Add User",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new AddUserDialog(_roles);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            if (AuthRepository.UsernameExists(dlg.Username))
            {
                MessageBox.Show("That username already exists.", "Add User",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AuthRepository.CreateUser(dlg.Username, dlg.Password, dlg.SelectedRoleId);
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't add user: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangeRole()
    {
        var user = SelectedUser();
        if (user == null) return;

        using var dlg = new PickRoleDialog(_roles, user.RoleId);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.SelectedRoleId == user.RoleId) return;

        // Don't move the last user out of Admin — would lock everyone out
        // of user management (Admin is the only role guaranteed to carry
        // MANAGE_USERS, though even that can be edited via Manage Roles —
        // this guard specifically protects against the common mistake).
        var currentRole = _roles.FirstOrDefault(r => r.RoleId == user.RoleId);
        if (currentRole?.RoleName == "Admin" && AuthRepository.CountUsersInRole(user.RoleId) <= 1)
        {
            MessageBox.Show("Can't move the last Admin out of the Admin role.", "Change Role",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try { AuthRepository.ChangeUserRole(user.UserId, dlg.SelectedRoleId); RefreshData(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't change role: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetPassword()
    {
        var user = SelectedUser();
        if (user == null) return;

        using var dlg = new ResetPasswordDialog(user.Username);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try { AuthRepository.ResetPassword(user.UserId, dlg.NewPassword); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't reset password: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        MessageBox.Show("Password updated.", "Reset Password", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DeleteUser()
    {
        var user = SelectedUser();
        if (user == null) return;

        var role = _roles.FirstOrDefault(r => r.RoleId == user.RoleId);
        if (role?.RoleName == "Admin" && AuthRepository.CountUsersInRole(user.RoleId) <= 1)
        {
            MessageBox.Show("Can't delete the last remaining Admin.", "Delete User",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Session.CurrentUser != null && user.UserId == Session.CurrentUser.UserId)
        {
            MessageBox.Show("You can't delete the account you're currently logged in as.", "Delete User",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"Delete user \"{user.Username}\"? This can't be undone.", "Delete User",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try { AuthRepository.DeleteUser(user.UserId); RefreshData(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't delete user: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ── Add User dialog ─────────────────────────────────────────────────────
internal sealed class AddUserDialog : Form
{
    private readonly TextBox  _txtUser  = new();
    private readonly TextBox  _txtPass  = new();
    private readonly ComboBox _cmbRole  = new();
    private readonly Label    _lblError = new();
    private readonly List<RoleInfo> _roles;

    public string Username      => _txtUser.Text.Trim();
    public string Password      => _txtPass.Text;
    public int    SelectedRoleId => _roles[_cmbRole.SelectedIndex].RoleId;

    public AddUserDialog(List<RoleInfo> roles)
    {
        _roles = roles;
        Text = "Add User";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 230);
        BackColor = UiTheme.Surface;

        var lblU = new Label { Text = "Username", AutoSize = true, Location = new Point(16, 14) };
        _txtUser.Location = new Point(16, 34); _txtUser.Size = new Size(268, 24);
        UiTheme.StyleTextBox(_txtUser);

        var lblP = new Label { Text = "Password (min 6 chars)", AutoSize = true, Location = new Point(16, 66) };
        _txtPass.Location = new Point(16, 86); _txtPass.Size = new Size(268, 24); _txtPass.PasswordChar = '*';
        UiTheme.StyleTextBox(_txtPass);

        var lblR = new Label { Text = "Role", AutoSize = true, Location = new Point(16, 118) };
        _cmbRole.Location = new Point(16, 138); _cmbRole.Size = new Size(268, 24);
        _cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var r in _roles) _cmbRole.Items.Add(r.RoleName);
        var operatorIdx = _roles.FindIndex(r => r.RoleName == "Operator");
        _cmbRole.SelectedIndex = operatorIdx >= 0 ? operatorIdx : 0;

        _lblError.ForeColor = UiTheme.Danger; _lblError.AutoSize = true;
        _lblError.Location = new Point(16, 168); _lblError.MaximumSize = new Size(268, 0);

        var btnOk = new Button { Text = "Add", Location = new Point(16, 192), Size = new Size(120, 28) };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(164, 192), Size = new Size(120, 28) };
        UiTheme.StylePrimary(btnOk); UiTheme.StyleSecondary(btnCancel);
        btnOk.Click += (_, _) =>
        {
            if (Username.Length < 3) { _lblError.Text = "Username must be at least 3 characters."; return; }
            if (Password.Length < 6) { _lblError.Text = "Password must be at least 6 characters."; return; }
            DialogResult = DialogResult.OK; Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lblU, _txtUser, lblP, _txtPass, lblR, _cmbRole, _lblError, btnOk, btnCancel });
        AcceptButton = btnOk;
    }
}

// ── Pick Role dialog (used by "Change Role") ────────────────────────────
internal sealed class PickRoleDialog : Form
{
    private readonly ComboBox _cmbRole = new();
    private readonly List<RoleInfo> _roles;

    public int SelectedRoleId => _roles[_cmbRole.SelectedIndex].RoleId;

    public PickRoleDialog(List<RoleInfo> roles, int currentRoleId)
    {
        _roles = roles;
        Text = "Change Role";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(280, 130);
        BackColor = UiTheme.Surface;

        var lbl = new Label { Text = "New role", AutoSize = true, Location = new Point(16, 14) };
        _cmbRole.Location = new Point(16, 34); _cmbRole.Size = new Size(248, 24);
        _cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var r in _roles) _cmbRole.Items.Add(r.RoleName);
        var idx = _roles.FindIndex(r => r.RoleId == currentRoleId);
        _cmbRole.SelectedIndex = idx >= 0 ? idx : 0;

        var btnOk = new Button { Text = "OK", Location = new Point(16, 72), Size = new Size(116, 28) };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(148, 72), Size = new Size(116, 28) };
        UiTheme.StylePrimary(btnOk); UiTheme.StyleSecondary(btnCancel);
        btnOk.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl, _cmbRole, btnOk, btnCancel });
        AcceptButton = btnOk;
    }
}

// ── Reset Password dialog ───────────────────────────────────────────────
internal sealed class ResetPasswordDialog : Form
{
    private readonly TextBox _txtPass = new();
    private readonly Label _lblError  = new();

    public string NewPassword => _txtPass.Text;

    public ResetPasswordDialog(string username)
    {
        Text = $"Reset Password — {username}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 140);
        BackColor = UiTheme.Surface;

        var lbl = new Label { Text = "New password (min 6 chars)", AutoSize = true, Location = new Point(16, 14) };
        _txtPass.Location = new Point(16, 34); _txtPass.Size = new Size(268, 24); _txtPass.PasswordChar = '*';
        UiTheme.StyleTextBox(_txtPass);

        _lblError.ForeColor = UiTheme.Danger; _lblError.AutoSize = true;
        _lblError.Location = new Point(16, 64); _lblError.MaximumSize = new Size(268, 0);

        var btnOk = new Button { Text = "Reset", Location = new Point(16, 96), Size = new Size(120, 28) };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(164, 96), Size = new Size(120, 28) };
        UiTheme.StylePrimary(btnOk); UiTheme.StyleSecondary(btnCancel);
        btnOk.Click += (_, _) =>
        {
            if (NewPassword.Length < 6) { _lblError.Text = "Password must be at least 6 characters."; return; }
            DialogResult = DialogResult.OK; Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl, _txtPass, _lblError, btnOk, btnCancel });
        AcceptButton = btnOk;
    }
}
