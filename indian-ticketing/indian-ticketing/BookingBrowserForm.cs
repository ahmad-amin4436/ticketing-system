using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace indian_ticketing;

/// <summary>
/// Reveals one booking's otherwise-hidden WebView2 on demand — each booking
/// runs its own browser off-screen by default (parallel bookings each need
/// an independent session, and the app shouldn't clutter the UI with N
/// browser panes), but the automation's existing "click this manually, then
/// OK" fallbacks still need somewhere for a human to actually see and click
/// the page when the automation can't find something itself. Closing this
/// window hands the WebView2 back to its owning BookingCard rather than
/// disposing it — the booking keeps running either way.
/// </summary>
public sealed class BookingBrowserForm : Form
{
    public BookingBrowserForm(string trainLabel, WebView2 webView)
    {
        Text          = $"Browser — {trainLabel}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize    = new Size(1000, 760);
        MinimumSize   = new Size(600, 400);
        BackColor     = UiTheme.Surface;

        webView.Dock     = DockStyle.Fill;
        webView.Location = Point.Empty; // undo the off-screen position while it's shown here
        Controls.Add(webView);
    }
}
