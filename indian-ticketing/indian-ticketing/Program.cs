using System;
using System.Windows.Forms;

namespace indian_ticketing;

static class Program
{
    // Trial version expires at the end of 3 September 2026 (local time).
    // The app refuses to start on or after 4 September 2026.
    private static readonly DateTime TrialExpiry = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Local);

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!CheckTrial())
        {
            return;
        }

        try
        {
            AuthDatabase.EnsureReady(DbConfig.Load());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't reach the login database.\n\n{ex.Message}\n\n" +
                "Check the connection settings in %AppData%\\IndianTicketing\\db_config.json " +
                "and that SQL Server is running.",
                "Database Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (var login = new LoginForm())
        {
            if (login.ShowDialog() != DialogResult.OK)
            {
                return; // cancelled, or closed without signing in
            }
        }

        Application.Run(new Form1());
    }

    private static bool CheckTrial()
    {
        DateTime now = DateTime.Now;

        if (now >= TrialExpiry)
        {
            MessageBox.Show(
                $"This trial version expired today and can no longer be used.\n\n" +
                "Please contact the vendor to obtain a licensed version.",
                "Trial Expired",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        int daysLeft = (int)Math.Ceiling((TrialExpiry - now).TotalDays);
        MessageBox.Show(
            $"This is a trial version. It will stop working after 17 June 2026.\n\n" +
            $"Days remaining: {daysLeft}",
            "Trial Version",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        return true;
    }
}
