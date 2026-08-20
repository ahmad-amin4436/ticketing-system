using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Actions;

public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public string? Reason { get; init; }

    public static ValidationResult Ok() => new() { IsValid = true };
    public static ValidationResult Fail(string reason) => new() { IsValid = false, Reason = reason };
}

/// <summary>
/// The AI is never trusted blindly. Every proposed action is checked against the CURRENT
/// observation before it is allowed to touch the browser — a stale or hallucinated target
/// is rejected here, not discovered as a runtime exception during execution.
/// </summary>
public interface IActionValidator
{
    ValidationResult Validate(BrowserAction action, PageState page);
}
