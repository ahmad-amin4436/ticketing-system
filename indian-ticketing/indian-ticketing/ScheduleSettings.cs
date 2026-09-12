using Microsoft.Data.SqlClient;

namespace indian_ticketing;

public class ScheduleSettings
{
    public bool     Enabled           { get; set; }
    public TimeSpan TriggerTime       { get; set; } = new(10, 0, 0);
    public DateOnly? LastTriggeredDate { get; set; }
}

// SQL-backed, single-row settings for the daily automatic "Start All
// Bookings" trigger — admin-configurable (MANAGE_SCHEDULE), read by Form1's
// background scheduler check every tick.
public static class ScheduleRepository
{
    private static SqlConnection Open()
    {
        var conn = new SqlConnection(DbConfig.Load().ConnectionString());
        conn.Open();
        return conn;
    }

    public static ScheduleSettings Get()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Enabled, TriggerTime, LastTriggeredDate FROM dbo.ScheduleSettings WHERE Id = 1;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return new ScheduleSettings();

        return new ScheduleSettings
        {
            Enabled           = reader.GetBoolean(0),
            TriggerTime       = reader.GetTimeSpan(1),
            LastTriggeredDate = reader.IsDBNull(2)
                ? null
                : DateOnly.FromDateTime(reader.GetDateTime(2)),
        };
    }

    public static void Save(bool enabled, TimeSpan triggerTime)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.ScheduleSettings SET Enabled = @e, TriggerTime = @t WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@e", enabled);
        cmd.Parameters.AddWithValue("@t", triggerTime);
        cmd.ExecuteNonQuery();
    }

    // Marks today (the caller's LOCAL date — same clock the scheduler
    // compares TriggerTime against) as done, so a same-day app restart or a
    // tick shortly after firing doesn't trigger a second run.
    public static void MarkTriggeredToday()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ScheduleSettings SET LastTriggeredDate = @d WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@d", DateTime.Today);
        cmd.ExecuteNonQuery();
    }
}
