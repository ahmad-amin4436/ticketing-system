namespace indian_ticketing.AI.Logging;

/// <summary>
/// Every agent cycle logs a concise, human-readable line (never raw model chain-of-thought)
/// via <see cref="OnEntry"/>, which the UI wires straight into the existing BookingCard
/// status label. When DebugMode is on, the same lines are additionally appended to a file
/// so a developer can inspect exactly why the AI chose each action. Any string that would
/// echo a real credential is masked before it is emitted anywhere, as defense in depth —
/// credentials are not expected to reach this layer at all (see ActionExecutor).
/// </summary>
public sealed class AgentLogger
{
    private readonly bool _debugMode;
    private readonly string? _debugFilePath;
    private readonly IReadOnlyList<string> _secrets;

    public event Action<string>? OnEntry;

    public AgentLogger(bool debugMode, IEnumerable<string> secretsToMask, string? debugFilePath = null)
    {
        _debugMode = debugMode;
        _debugFilePath = debugFilePath;
        _secrets = secretsToMask.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
    }

    public void Log(string message)
    {
        var masked = Mask(message);
        OnEntry?.Invoke(masked);

        if (_debugMode && !string.IsNullOrEmpty(_debugFilePath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_debugFilePath)!);
                File.AppendAllText(_debugFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {masked}{Environment.NewLine}");
            }
            catch { /* debug logging must never break the booking flow */ }
        }
    }

    private string Mask(string s)
    {
        foreach (var secret in _secrets)
            s = s.Replace(secret, "***", StringComparison.Ordinal);
        return s;
    }
}
