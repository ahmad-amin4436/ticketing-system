namespace indian_ticketing.AI.Actions;

public sealed class ActionExecutionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
}
