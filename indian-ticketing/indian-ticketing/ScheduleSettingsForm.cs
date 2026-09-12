using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// Admin-side (MANAGE_SCHEDULE) control for the daily auto-booking time.
/// Saves straight to dbo.ScheduleSettings — Form1's scheduler timer reads
/// it fresh every tick, so a change here takes effect without restarting
/// the app.
/// </summary>
public sealed class ScheduleSettingsForm : Form
{
    private readonly CheckBox _chkEnabled = new();
    private readonly DateTimePicker _dtpTime = new();
    private readonly Label _lblInfo = new();

    public ScheduleSettingsForm()
    {
        BuildUi();
        LoadCurrent();
    }

    private void BuildUi()
    {
        Text            = "Daily Booking Schedule";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        ClientSize      = new Size(340, 220);
        BackColor       = UiTheme.Surface;

        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = UiTheme.Primary };
        var lblTitle = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            Font = UiTheme.FontHeader, ForeColor = UiTheme.TextOnPrimary,
            Text = "Daily Booking Schedule",
        };
        header.Controls.Add(lblTitle);

        _chkEnabled.Text = "Automatically start all bookings every day";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Font = UiTheme.FontBody;
        _chkEnabled.Location = new Point(20, 72);
        _chkEnabled.CheckedChanged += (_, _) => _dtpTime.Enabled = _chkEnabled.Checked;

        var lblAt = new Label { Text = "At", AutoSize = true, Font = UiTheme.FontBody, Location = new Point(20, 108) };
        _dtpTime.Format = DateTimePickerFormat.Time;
        _dtpTime.ShowUpDown = true;
        _dtpTime.Location = new Point(50, 104);
        _dtpTime.Size = new Size(120, 26);

        _lblInfo.AutoSize = true;
        _lblInfo.Font = UiTheme.FontSmall;
        _lblInfo.ForeColor = UiTheme.TextSecondary;
        _lblInfo.MaximumSize = new Size(300, 0);
        _lblInfo.Location = new Point(20, 140);
        _lblInfo.Text = "Requires the app to already be open with a signed-in user " +
                        "who has permission to start bookings — it does not run if no one is signed in.";

        var btnSave = new Button { Text = "Save", Location = new Point(150, 178), Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(238, 178), Size = new Size(80, 30) };
        UiTheme.StylePrimary(btnSave);
        UiTheme.StyleSecondary(btnCancel);
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _chkEnabled, lblAt, _dtpTime, _lblInfo, btnSave, btnCancel, header });
        AcceptButton = btnSave;
    }

    private void LoadCurrent()
    {
        ScheduleSettings s;
        try { s = ScheduleRepository.Get(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load the current schedule: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            s = new ScheduleSettings();
        }

        _chkEnabled.Checked = s.Enabled;
        _dtpTime.Value = DateTime.Today.Add(s.TriggerTime);
        _dtpTime.Enabled = s.Enabled;
    }

    private void Save()
    {
        try
        {
            ScheduleRepository.Save(_chkEnabled.Checked, _dtpTime.Value.TimeOfDay);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save the schedule: {ex.Message}", "Database Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
