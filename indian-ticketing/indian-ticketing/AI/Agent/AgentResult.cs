namespace indian_ticketing.AI.Agent;

public enum AgentOutcome
{
    Completed,
    HumanInterventionRequired,
    Failed,
    Cancelled,
    StepLimitReached,
}

public sealed class AgentResult
{
    public AgentOutcome Outcome { get; init; }
    public string? Reason { get; init; }

    public static AgentResult Completed() => new() { Outcome = AgentOutcome.Completed };
    public static AgentResult RequiresHumanIntervention(string reason) => new() { Outcome = AgentOutcome.HumanInterventionRequired, Reason = reason };
    public static AgentResult Failed(string reason) => new() { Outcome = AgentOutcome.Failed, Reason = reason };
    public static AgentResult Cancelled() => new() { Outcome = AgentOutcome.Cancelled };
    public static AgentResult StepLimit() => new() { Outcome = AgentOutcome.StepLimitReached };
}
