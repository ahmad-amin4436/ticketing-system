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
        ClientSize      = new Size(360, 420);
        BackColor       = Color.White;

        _pic = new PictureBox
        {
            Location    = new Point(30, 20),
            Size        = new Size(300, 300),
            SizeMode    = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = Color.White,
        };

        _caption = new Label
        {
            Location  = new Point(20, 330),
            Size      = new Size(320, 70),
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x1B, 0x3A, 0x6B),
            Text      = "Scan this QR with any UPI app\n(PhonePe / GPay / Paytm) to complete payment.",
        };

        Controls.Add(_pic);
        Controls.Add(_caption);
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
