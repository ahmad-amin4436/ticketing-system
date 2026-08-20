using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Goals;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Agent;

/// <summary>Short-term state for one run. Only the immediately previous step is kept — the full history is never sent to Ollama.</summary>
public sealed class AgentExecutionState
{
    public required BookingGoal Goal { get; init; }
    public PageState? CurrentPage { get; set; }
    public BrowserAction? PreviousAction { get; set; }
    public ActionExecutionResult? PreviousResult { get; set; }
    public int StepCount { get; set; }
    public int FailedActions { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}
