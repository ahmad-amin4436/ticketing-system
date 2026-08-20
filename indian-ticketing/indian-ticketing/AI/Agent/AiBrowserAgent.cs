using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Configuration;
using indian_ticketing.AI.Goals;
using indian_ticketing.AI.Logging;
using indian_ticketing.AI.Observation;
using indian_ticketing.AI.Planning;
using indian_ticketing.AI.Recovery;
using indian_ticketing.AI.Verification;

namespace indian_ticketing.AI.Agent;

/// <summary>
/// The full observe -> decide -> validate -> execute -> verify loop. This is the only place
/// that sequences the other components; it never follows a fixed page order itself - every
/// iteration re-observes the live page and lets the model router decide the next action from
/// scratch, informed only by the goal and the immediately previous step.
/// </summary>
public sealed class AiBrowserAgent
{
    private readonly IPageObserver _observer;
    private readonly IAiModelRouter _router;
    private readonly IActionValidator _validator;
    private readonly IActionExecutor _executor;
    private readonly IActionVerifier _verifier;
    private readonly LoopDetector _loopDetector;
    private readonly RecoveryManager _recovery;
    private readonly AiAgentSettings _options;
    private readonly AgentLogger _logger;

    public event Action<string>? OnStatus;

    public AiBrowserAgent(
        IPageObserver observer,
        IAiModelRouter router,
        IActionValidator validator,
        IActionExecutor executor,
        IActionVerifier verifier,
        AiAgentSettings options,
        AgentLogger logger)
    {
        _observer = observer;
        _router = router;
        _validator = validator;
        _executor = executor;
        _verifier = verifier;
        _options = options;
        _logger = logger;
        _loopDetector = new LoopDetector(options.MaxRepeatedState);
        _recovery = new RecoveryManager(options.MaxActionRetries);
        _logger.OnEntry += msg => OnStatus?.Invoke(msg);
    }

    public async Task<AgentResult> RunAsync(BookingGoal goal, CancellationToken cancellationToken = default)
    {
        var state = new AgentExecutionState { Goal = goal };
        Report("Adaptive agent started.");

        var forceReasoning = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (state.StepCount >= _options.MaxSteps)
            {
                Report($"Step limit ({_options.MaxSteps}) reached without completing the goal.");
                return AgentResult.StepLimit();
            }
            state.StepCount++;

            PageState page;
            try { page = await _observer.ObserveAsync(cancellationToken); }
            catch (Exception ex)
            {
                Report($"Failed to observe the page: {ex.Message}");
                return AgentResult.Failed($"Observation failed: {ex.Message}");
            }
            state.CurrentPage = page;

            AiDecision decision;
            try
            {
                var context = new AgentContext
                {
                    Goal = state.Goal,
                    Page = page,
                    PreviousAction = state.PreviousAction,
                    PreviousResult = state.PreviousResult,
                    ForceReasoningModel = forceReasoning,
                };
                decision = await _router.DecideAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                Report($"AI planning failed: {ex.Message}");
                return AgentResult.Failed($"Planning failed: {ex.Message}");
            }
            forceReasoning = false;

            Report(Summarize(decision));

            if (decision.Status == AgentStatus.Completed)
            {
                if (LooksLikeCompletion(page))
                {
                    Report("Booking objective appears complete.");
                    return AgentResult.Completed();
                }
                Report("AI reported completion, but no confirmation signal was found on the page — continuing.");
                continue;
            }

            if (decision.Status == AgentStatus.HumanInterventionRequired)
            {
                Report($"Human intervention required: {decision.Reason}");
                return AgentResult.RequiresHumanIntervention(decision.Reason);
            }

            if (decision.Status == AgentStatus.Failed)
            {
                Report($"AI reported failure: {decision.Reason}");
                return AgentResult.Failed(decision.Reason);
            }

            if (decision.Status == AgentStatus.Waiting)
            {
                Report($"Waiting: {decision.Reason}");
                await Task.Delay(1500, cancellationToken);
                continue;
            }

            if (decision.Status != AgentStatus.ActionRequired || decision.Action is null)
            {
                Report("AI returned an unusable decision — treating as a stall.");
                if (_loopDetector.RegisterAndCheckLoop(page, new BrowserAction { Type = BrowserActionType.Wait }))
                    return AgentResult.RequiresHumanIntervention("The agent could not produce a usable decision after repeated attempts.");
                continue;
            }

            var action = decision.Action;

            var validation = _validator.Validate(action, page);
            if (!validation.IsValid)
            {
                Report($"Rejected AI action ({action.Type}): {validation.Reason}");
                state.PreviousAction = action;
                state.PreviousResult = new ActionExecutionResult { Success = false, Error = $"Validator rejected action: {validation.Reason}" };
                if (_loopDetector.RegisterAndCheckLoop(page, action))
                    return AgentResult.RequiresHumanIntervention($"Repeated invalid actions ({action.Type} on {action.TargetId}).");
                forceReasoning = true;
                continue;
            }

            if (_loopDetector.RegisterAndCheckLoop(page, action))
            {
                Report($"Detected a repeated state/action loop on {action.Type} → {action.TargetId}. Escalating.");
                forceReasoning = true;
                continue;
            }

            var executionResult = await _executor.ExecuteAsync(action, cancellationToken);

            PageState afterPage;
            try { afterPage = await _observer.ObserveAsync(cancellationToken); }
            catch (Exception ex)
            {
                Report($"Failed to re-observe after action: {ex.Message}");
                return AgentResult.Failed($"Post-action observation failed: {ex.Message}");
            }

            var verification = _verifier.Verify(page, action, executionResult, afterPage);
            Report(verification.Success ? $"Verified: {verification.Details}" : $"Verification failed: {verification.Details}");

            state.PreviousAction = action;
            state.PreviousResult = new ActionExecutionResult { Success = verification.Success, Error = verification.Success ? null : verification.Details };

            if (verification.Success)
            {
                _recovery.OnVerificationSucceeded();
                continue;
            }

            var beforeFingerprint = StateFingerprint.Compute(page);
            var afterFingerprint = StateFingerprint.Compute(afterPage);
            var recoveryDecision = _recovery.OnVerificationFailed(beforeFingerprint != afterFingerprint);

            switch (recoveryDecision)
            {
                case RecoveryDecision.Reassess:
                    continue;
                case RecoveryDecision.RetryEscalated:
                    Report("Repeated failure — escalating to the reasoning model.");
                    forceReasoning = true;
                    continue;
                case RecoveryDecision.HumanIntervention:
                    Report("Unable to make progress after retries and escalation.");
                    return AgentResult.RequiresHumanIntervention("Repeated action failures could not be resolved automatically.");
            }
        }

        return AgentResult.Cancelled();
    }

    private static bool LooksLikeCompletion(PageState page)
    {
        var text = (page.Title + " " + page.VisibleText).ToLowerInvariant();
        return text.Contains("booking confirmed") || text.Contains("ticket booked")
            || text.Contains("pnr") || text.Contains("booking reference")
            || text.Contains("transaction successful") || text.Contains("payment successful");
    }

    private static string Summarize(AiDecision d)
    {
        if (d.Status == AgentStatus.ActionRequired && d.Action is not null)
            return $"{d.Action.Type} → {d.Action.TargetId ?? "-"} ({d.Confidence:0.00}, {d.ModelUsed}): {Trim(d.Reason)}";
        return $"{d.Status} ({d.Confidence:0.00}, {d.ModelUsed}): {Trim(d.Reason)}";
    }

    private static string Trim(string s) => s.Length > 140 ? s[..140] + "…" : s;

    private void Report(string message) => _logger.Log(message);
}
