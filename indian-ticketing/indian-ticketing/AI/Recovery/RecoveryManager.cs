namespace indian_ticketing.AI.Recovery;

public enum RecoveryDecision
{
    /// <summary>Let the AI reassess normally from the freshly observed state.</summary>
    Reassess,

    /// <summary>Force the next decision through the reasoning (escalation) model specifically.</summary>
    RetryEscalated,

    HumanIntervention,
}

/// <summary>
/// Sequences what happens after a verification failure: re-observe-and-reassess (if the
/// page actually changed, something happened — let the AI look again rather than blindly
/// retrying the same action) → retry once → escalate to the reasoning model → give up and
/// ask for human help. Never retries blindly past the configured threshold.
/// </summary>
public sealed class RecoveryManager
{
    private readonly int _maxActionRetries;
    private int _consecutiveFailures;

    public RecoveryManager(int maxActionRetries) => _maxActionRetries = Math.Max(0, maxActionRetries);

    public RecoveryDecision OnVerificationFailed(bool stateChangedSincePriorAction)
    {
        if (stateChangedSincePriorAction)
        {
            _consecutiveFailures = 0;
            return RecoveryDecision.Reassess;
        }

        _consecutiveFailures++;
        if (_consecutiveFailures <= _maxActionRetries)
            return RecoveryDecision.Reassess;

        if (_consecutiveFailures <= _maxActionRetries + 1)
            return RecoveryDecision.RetryEscalated;

        return RecoveryDecision.HumanIntervention;
    }

    public void OnVerificationSucceeded() => _consecutiveFailures = 0;
}
