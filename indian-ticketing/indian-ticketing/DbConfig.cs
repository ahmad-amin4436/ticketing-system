using System.Text.Json;

namespace indian_ticketing;

// Connection settings for the SQL Server-backed login/roles system, stored
// the same way as ProxyConfig/SavedBooking (a local JSON file under
// %AppData%\IndianTicketing) instead of hardcoding them in source. Seeded
// with the instance this app was set up against on first run; editable
// afterward without touching code.
public class DbConfig
{
    public string Server   { get; set; } = "157.180.51.251,1435";
    public string Database { get; set; } = "IndianTicketingAuth";
    public string UserId   { get; set; } = "indian";
    public string Password { get; set; } = "abcd@1234";

    public string ConnectionString(bool includeDatabase = true) =>
        includeDatabase
            ? $"Server={Server};Database={Database};User Id={UserId};Password={Password};TrustServerCertificate=True;"
            : $"Server={Server};User Id={UserId};Password={Password};TrustServerCertificate=True;";

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IndianTicketing", "db_config.json");

    public static DbConfig Load()
    {
        if (!File.Exists(StorePath)) return new DbConfig();
        try
        {
            return JsonSerializer.Deserialize<DbConfig>(File.ReadAllText(StorePath)) ?? new DbConfig();
        }
        catch { return new DbConfig(); }
    }

    public static void Save(DbConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
