using indian_ticketing.AI.Actions;

namespace indian_ticketing.AI.Planning;

public sealed class AiDecision
{
    public AgentStatus Status { get; init; } = AgentStatus.Unknown;
    public BrowserAction? Action { get; init; }
    public string Reason { get; init; } = "";
    public double Confidence { get; init; }
    public string? ExpectedOutcome { get; init; }
    public string ModelUsed { get; init; } = "";
}
