using System.Text.Json;

namespace indian_ticketing.AI.Configuration;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string FastModel { get; set; } = "llama3.2:1b";
    public string ReasoningModel { get; set; } = "llama3.2:latest";

    // A cold Ollama model load alone can take ~10s on this class of hardware (measured:
    // llama3.2:1b's first call after being idle took 10.4s just to load, before any
    // prompt evaluation) — 30s leaves too little margin once a full page-sized prompt is
    // added on top, especially for the first decision of a fresh run or right after the
    // model has been evicted from Ollama's keep-alive window.
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class AiAgentSettings
{
    public bool Enabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.85;
    public int MaxSteps { get; set; } = 100;
    public int MaxRepeatedState { get; set; } = 3;
    public int MaxActionRetries { get; set; } = 1;
    public bool EnableScreenshots { get; set; } = true;
    public bool DebugMode { get; set; } = true;
}

/// <summary>
/// Persisted exactly like ProxyConfig/SavedBooking (manual System.Text.Json load/save
/// against a file under %AppData%\IndianTicketing\) — this app has no DI container or
/// appsettings.json convention, so the AI subsystem follows its existing pattern rather
/// than introducing one.
/// </summary>
public sealed class AiAgentConfigFile
{
    public OllamaOptions Ollama { get; set; } = new();
    public AiAgentSettings AiAgent { get; set; } = new();

    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "IndianTicketing", "ai_agent_config.json");

    public static AiAgentConfigFile Load()
    {
        if (!File.Exists(StorePath)) return new AiAgentConfigFile();
        try
        {
            return JsonSerializer.Deserialize<AiAgentConfigFile>(File.ReadAllText(StorePath))
                   ?? new AiAgentConfigFile();
        }
        catch { return new AiAgentConfigFile(); }
    }

    public static void Save(AiAgentConfigFile config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
