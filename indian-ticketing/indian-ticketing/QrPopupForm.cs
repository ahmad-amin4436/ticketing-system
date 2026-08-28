using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// A standalone, always-on-top window that shows the captured UPI payment QR at a
/// large, scannable size. Raised from the Booking Manager when Step 10 extracts the
/// QR off the IRCTC payment gateway. Re-uses one instance per booking card so a
/// refreshed QR replaces the previous image instead of stacking windows.
/// </summary>
public sealed class QrPopupForm : Form
{
    private readonly Panel      _header;
    private readonly Label      _headerLabel;
    private readonly PictureBox _pic;
    private readonly Label      _caption;

    public QrPopupForm(string trainLabel)
    {
        Text            = $"Scan to Pay — {trainLabel}";
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = true;
        TopMost         = true;
        ClientSize      = new Size(360, 456);
        BackColor       = UiTheme.Surface;

        _header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = UiTheme.Primary };
        _headerLabel = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = UiTheme.FontHeader, ForeColor = UiTheme.TextOnPrimary,
            AutoEllipsis = true,
            Text = $"Scan to Pay — {trainLabel}",
        };
        _header.Controls.Add(_headerLabel);

        _pic = new PictureBox
        {
            Location    = new Point(30, 56),
            Size        = new Size(300, 300),
            SizeMode    = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = UiTheme.Surface,
        };

        _caption = new Label
        {
            Location  = new Point(20, 366),
            Size      = new Size(320, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = UiTheme.Primary,
            Text      = "Scan this QR with any UPI app\n(PhonePe / GPay / Paytm) to complete payment.",
        };

        Controls.Add(_pic);
        Controls.Add(_caption);
        Controls.Add(_header);
    }

    /// <summary>Set/replace the QR image and bring the window to the foreground.</summary>
    public void SetQr(Bitmap bmp)
    {
        _pic.Image?.Dispose();
        _pic.Image = (Bitmap)bmp.Clone();

        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pic.Image?.Dispose();
        base.Dispose(disposing);
    }
}
