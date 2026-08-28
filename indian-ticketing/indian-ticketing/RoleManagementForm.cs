using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// The "generic rights" half of the system: roles aren't a fixed enum —
/// this screen creates/deletes roles and toggles which of the app's fixed
/// permissions (MANAGE_CREDENTIALS / MANAGE_BOOKINGS / MANAGE_USERS) each
/// one grants. New roles show up immediately in Manage Users' role picker.
/// </summary>
public sealed class RoleManagementForm : Form
{
    private readonly ListBox _roleList = new();
    private readonly Panel   _permPanel = new();
    private readonly Label   _lblSelected = new();
    private readonly Button  _btnSave = new();

    private List<RoleInfo> _roles = new();
    private readonly List<(string Code, string Desc, CheckBox Box)> _permBoxes = new();

    public RoleManagementForm()
    {
        BuildUi();
        RefreshRoles();
    }

    private void BuildUi()
    {
        Text          = "Manage Roles";
        ClientSize    = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize   = new Size(460, 320);
        BackColor     = UiTheme.Surface;

        _roleList.Location = new Point(12, 12);
        _roleList.Size     = new Size(180, 300);
        _roleList.Font     = UiTheme.FontBody;
        _roleList.SelectedIndexChanged += (_, _) => LoadPermissionsForSelectedRole();

        var btnNewRole = new Button { Text = "+ New Role", Location = new Point(12, 318), Size = new Size(86, 28) };
        var btnDelRole = new Button { Text = "Delete", Location = new Point(106, 318), Size = new Size(86, 28) };
        UiTheme.StyleSecondary(btnNewRole);
        UiTheme.StyleDanger(btnDelRole);
        btnNewRole.Click += (_, _) => CreateRole();
        btnDelRole.Click += (_, _) => DeleteSelectedRole();

        _lblSelected.AutoSize = true;
        _lblSelected.Font     = UiTheme.FontHeader;
        _lblSelected.ForeColor = UiTheme.TextPrimary;
        _lblSelected.Location = new Point(206, 12);
        _lblSelected.Text     = "Select a role";

        _permPanel.Location = new Point(206, 44);
        _permPanel.Size     = new Size(300, 260);
        _permPanel.AutoScroll = true;

        _btnSave.Location = new Point(206, 318);
        _btnSave.Size     = new Size(140, 30);
        _btnSave.Text     = "Save Permissions";
        UiTheme.StylePrimary(_btnSave);
        _btnSave.Click += (_, _) => SavePermissions();

        var btnClose = new Button { Text = "Close", Location = new Point(430, 356), Size = new Size(80, 28) };
        UiTheme.StyleSecondary(btnClose);
        btnClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
            { _roleList, btnNewRole, btnDelRole, _lblSelected, _permPanel, _btnSave, btnClose });
    }

    private void RefreshRoles()
    {
        try { _roles = AuthRepository.GetAllRoles(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load roles: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var previouslySelected = _roleList.SelectedItem as string;
        _roleList.Items.Clear();
        foreach (var r in _roles) _roleList.Items.Add(r.RoleName);

        var idx = previouslySelected != null ? _roleList.Items.IndexOf(previouslySelected) : -1;
        _roleList.SelectedIndex = idx >= 0 ? idx : (_roleList.Items.Count > 0 ? 0 : -1);
    }

    private RoleInfo? SelectedRole()
    {
        var idx = _roleList.SelectedIndex;
        return idx >= 0 && idx < _roles.Count ? _roles[idx] : null;
    }

    private void LoadPermissionsForSelectedRole()
    {
        _permPanel.Controls.Clear();
        _permBoxes.Clear();

        var role = SelectedRole();
        if (role == null) { _lblSelected.Text = "Select a role"; _btnSave.Enabled = false; return; }

        _lblSelected.Text = role.RoleName + (role.IsSystemRole ? "  (built-in)" : "");
        _btnSave.Enabled  = true;

        HashSet<string> granted;
        try { granted = AuthRepository.GetPermissionsForRole(role.RoleId); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load permissions: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int y = 4;
        foreach (var (code, desc) in AuthRepository.GetAllPermissionDefs())
        {
            var box = new CheckBox
            {
                AutoSize = false, Location = new Point(4, y), Size = new Size(280, 40),
                Text = $"{code}\n{desc}", Font = UiTheme.FontSmall,
                Checked = granted.Contains(code),
            };
            _permPanel.Controls.Add(box);
            _permBoxes.Add((code, desc, box));
            y += 44;
        }
    }

    private void CreateRole()
    {
        using var prompt = new PromptDialog("New Role", "Role name:");
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var name = prompt.Value.Trim();
        if (name.Length == 0) return;
        if (_roles.Any(r => r.RoleName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A role with that name already exists.", "New Role",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try { AuthRepository.CreateRole(name); RefreshRoles(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't create role: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedRole()
    {
        var role = SelectedRole();
        if (role == null) return;
        if (role.IsSystemRole)
        {
            MessageBox.Show("Built-in roles (Admin, Operator) can't be deleted.", "Delete Role",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (AuthRepository.CountUsersInRole(role.RoleId) > 0)
        {
            MessageBox.Show("This role still has users assigned — reassign them first.", "Delete Role",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"Delete role \"{role.RoleName}\"?", "Delete Role",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try { AuthRepository.DeleteRole(role.RoleId); RefreshRoles(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't delete role: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SavePermissions()
    {
        var role = SelectedRole();
        if (role == null) return;

        var toGrant = _permBoxes.Where(p => p.Box.Checked).Select(p => p.Code).ToList();

        // Guard against locking everyone out: don't let the last Admin-role
        // user's role lose MANAGE_USERS if they're the only one who has it.
        if (!toGrant.Contains("MANAGE_USERS"))
        {
            var otherRolesWithManageUsers = _roles
                .Where(r => r.RoleId != role.RoleId)
                .Any(r => AuthRepository.GetPermissionsForRole(r.RoleId).Contains("MANAGE_USERS")
                          && AuthRepository.CountUsersInRole(r.RoleId) > 0);
            var thisRoleHasUsers = AuthRepository.CountUsersInRole(role.RoleId) > 0;
            if (thisRoleHasUsers && !otherRolesWithManageUsers)
            {
                MessageBox.Show(
                    "This would leave no user able to manage users at all — " +
                    "keep MANAGE_USERS on at least one role that has members.",
                    "Save Permissions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        try
        {
            AuthRepository.SetRolePermissions(role.RoleId, toGrant);
            MessageBox.Show("Permissions saved. Users with this role will see the change next time they sign in.",
                "Save Permissions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save permissions: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// Minimal single-text-field prompt — avoids pulling in Microsoft.VisualBasic
// just for an InputBox.
internal sealed class PromptDialog : Form
{
    private readonly TextBox _txt = new();
    public string Value => _txt.Text;

    public PromptDialog(string title, string label)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(280, 120);
        BackColor = UiTheme.Surface;

        var lbl = new Label { Text = label, AutoSize = true, Location = new Point(16, 14) };
        _txt.Location = new Point(16, 34); _txt.Size = new Size(248, 24);
        UiTheme.StyleTextBox(_txt);

        var btnOk = new Button { Text = "OK", Location = new Point(16, 72), Size = new Size(116, 28) };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(148, 72), Size = new Size(116, 28) };
        UiTheme.StylePrimary(btnOk); UiTheme.StyleSecondary(btnCancel);
        btnOk.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl, _txt, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
