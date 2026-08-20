using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Verification;

public sealed class VerificationResult
{
    public bool Success { get; init; }
    public string Details { get; init; } = "";
}

/// <summary>
/// The AI is never trusted to declare its own success. Every executed action is checked
/// against a fresh observation of the resulting page — "the AI says it clicked Search"
/// means nothing until the page actually shows search results.
/// </summary>
public interface IActionVerifier
{
    VerificationResult Verify(PageState before, BrowserAction action, ActionExecutionResult executionResult, PageState after);
}
