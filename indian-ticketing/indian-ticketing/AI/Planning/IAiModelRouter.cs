using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Goals;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Planning;

public sealed class AgentContext
{
    public required BookingGoal Goal { get; init; }
    public required PageState Page { get; init; }
    public BrowserAction? PreviousAction { get; init; }
    public ActionExecutionResult? PreviousResult { get; init; }

    /// <summary>Set by the recovery manager to skip straight to the reasoning model — after a validator rejection, a loop, or a repeated verification failure.</summary>
    public bool ForceReasoningModel { get; init; }
}

public interface IAiModelRouter
{
    Task<AiDecision> DecideAsync(AgentContext context, CancellationToken cancellationToken = default);
}
