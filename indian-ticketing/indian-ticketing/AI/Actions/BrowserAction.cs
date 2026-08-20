namespace indian_ticketing.AI.Actions;

public sealed class BrowserAction
{
    public BrowserActionType Type { get; init; }

    public string? TargetId { get; init; }

    /// <summary>Literal value to type/select. Null when ValueSource supplies it instead.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// "SECURE_USERNAME" / "SECURE_PASSWORD" — the AI never receives real credentials, so
    /// it can only ever reference them indirectly. ActionExecutor resolves the real value
    /// from application state at execution time.
    /// </summary>
    public string? ValueSource { get; init; }

    public string? Key { get; init; }

    public int? Amount { get; init; }

    public double Confidence { get; init; }

    public string Reason { get; init; } = "";

    public string? ExpectedOutcome { get; init; }
}
