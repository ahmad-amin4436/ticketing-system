using System.Drawing;
using System.Windows.Forms;

namespace indian_ticketing;

/// <summary>
/// Shared visual language for every window in the app — one palette/type
/// scale/button style instead of each form inventing its own colors ad hoc
/// (the pre-existing forms each used slightly different blues/greys). Apply
/// via the Style* helpers so buttons/panels look consistent everywhere
/// without duplicating the same five lines in every form.
/// </summary>
public static class UiTheme
{
    // ── Palette ──────────────────────────────────────────────────────────
    public static readonly Color Primary       = Color.FromArgb(0x1E, 0x3A, 0x5F); // deep navy — headers
    public static readonly Color PrimaryLight  = Color.FromArgb(0x2C, 0x55, 0x82); // hover/secondary navy
    public static readonly Color Accent        = Color.FromArgb(0x0E, 0x9F, 0x6E); // green — primary CTA
    public static readonly Color AccentDark    = Color.FromArgb(0x0A, 0x7D, 0x56);
    public static readonly Color Warning       = Color.FromArgb(0xE6, 0x7E, 0x22);
    public static readonly Color Danger        = Color.FromArgb(0xD1, 0x43, 0x43);
    public static readonly Color DangerDark    = Color.FromArgb(0xA8, 0x2E, 0x2E);
    public static readonly Color Surface       = Color.White;
    public static readonly Color Background    = Color.FromArgb(0xF2, 0xF4, 0xF8);
    public static readonly Color CardAlt       = Color.FromArgb(0xF7, 0xF9, 0xFC);
    public static readonly Color Border        = Color.FromArgb(0xDD, 0xE3, 0xEA);
    public static readonly Color TextPrimary   = Color.FromArgb(0x1F, 0x29, 0x37);
    public static readonly Color TextSecondary = Color.FromArgb(0x64, 0x71, 0x82);
    public static readonly Color TextOnPrimary = Color.White;
    public static readonly Color Disabled      = Color.FromArgb(0xB6, 0xBD, 0xC6);

    // ── Type scale ───────────────────────────────────────────────────────
    public static Font FontTitle  => new("Segoe UI", 13F, FontStyle.Bold);
    public static Font FontHeader => new("Segoe UI", 10.5F, FontStyle.Bold);
    public static Font FontLabel  => new("Segoe UI", 8.5F, FontStyle.Bold);
    public static Font FontBody   => new("Segoe UI", 9.5F);
    public static Font FontSmall  => new("Segoe UI", 8F);
    public static Font FontButton => new("Segoe UI", 9.5F, FontStyle.Bold);
    public static Font FontButtonSmall => new("Segoe UI", 8.5F, FontStyle.Bold);

    // ── Button styles ────────────────────────────────────────────────────
    // Solid, high-contrast — the one primary action on a screen (Get Trains,
    // Sign In, Start All).
    public static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentDark;
        b.BackColor = Accent;
        b.ForeColor = TextOnPrimary;
        b.Font = FontButton;
        b.Cursor = Cursors.Hand;
    }

    // Filled navy — for actions that live inside a navy header bar and need
    // to read clearly against it (Start All Bookings, Manage Users trigger).
    public static void StyleOnHeader(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PrimaryLight;
        b.BackColor = PrimaryLight;
        b.ForeColor = TextOnPrimary;
        b.Font = FontButtonSmall;
        b.Cursor = Cursors.Hand;
    }

    // Quiet outline — secondary actions (Save Train, Refresh, Cancel).
    public static void StyleSecondary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = CardAlt;
        b.BackColor = Surface;
        b.ForeColor = TextPrimary;
        b.Font = FontButtonSmall;
        b.Cursor = Cursors.Hand;
    }

    // Destructive actions (Delete).
    public static void StyleDanger(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Danger;
        b.FlatAppearance.MouseOverBackColor = Danger;
        b.BackColor = Surface;
        b.ForeColor = DangerDark;
        b.Font = FontButtonSmall;
        b.Cursor = Cursors.Hand;
    }

    // Toggle chip that reflects an on/off state (Proxy: ON/OFF).
    public static void StyleToggle(Button b, bool on)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = on ? Accent : Color.FromArgb(0x5B, 0x66, 0x74);
        b.ForeColor = TextOnPrimary;
        b.Font = FontButtonSmall;
        b.Cursor = Cursors.Hand;
    }

    public static void StyleTextBox(TextBox t)
    {
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = FontBody;
    }

    public static void StyleCombo(ComboBox c)
    {
        c.FlatStyle = FlatStyle.Flat;
        c.Font = FontSmall;
    }
}
