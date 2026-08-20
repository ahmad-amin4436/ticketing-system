namespace indian_ticketing.AI.Planning;

/// <summary>
/// Two-level model strategy: llama3.2:1b answers first for routine decisions; llama3.2:latest
/// (or whatever ReasoningModel is configured) is only called when the fast model's confidence
/// is below threshold, it fails to produce parseable output, or the caller explicitly forces
/// escalation (loop detected / repeated failure / validator rejection). The stronger model is
/// never used for every action - only when the fast one is uncertain or stuck.
/// </summary>
public sealed class AiModelRouter : IAiModelRouter
{
    private readonly IOllamaClient _client;
    private readonly string _fastModel;
    private readonly string _reasoningModel;
    private readonly double _confidenceThreshold;
    private readonly Action<string>? _log;

    public AiModelRouter(IOllamaClient client, string fastModel, string reasoningModel, double confidenceThreshold, Action<string>? log = null)
    {
        _client = client;
        _fastModel = fastModel;
        _reasoningModel = reasoningModel;
        _confidenceThreshold = confidenceThreshold;
        _log = log;
    }

    public async Task<AiDecision> DecideAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        var prompt = PromptBuilder.Build(context.Goal, context.Page, context.PreviousAction, context.PreviousResult);

        if (!context.ForceReasoningModel)
        {
            var fastDecision = await TryDecideAsync(_fastModel, prompt, cancellationToken);
            if (fastDecision is not null && fastDecision.Confidence >= _confidenceThreshold)
                return fastDecision;

            _log?.Invoke(fastDecision is null
                ? $"[{_fastModel}] produced no usable decision — escalating to {_reasoningModel}."
                : $"[{_fastModel}] confidence {fastDecision.Confidence:0.00} below threshold {_confidenceThreshold:0.00} — escalating to {_reasoningModel}.");
        }

        var reasoningDecision = await TryDecideAsync(_reasoningModel, prompt, cancellationToken);
        if (reasoningDecision is not null) return reasoningDecision;

        return new AiDecision
        {
            Status = AgentStatus.HumanInterventionRequired,
            Reason = "Neither model produced a valid, parseable decision.",
            Confidence = 0,
            ModelUsed = _reasoningModel,
        };
    }

    private async Task<AiDecision?> TryDecideAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _client.GenerateAsync(model, prompt, cancellationToken);
            var outcome = DecisionParser.Parse(raw, model);
            if (outcome.Success) return outcome.Decision;
            _log?.Invoke($"[{model}] decision parse failed: {outcome.Error}");
            return null;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[{model}] Ollama call failed: {ex.Message}");
            return null;
        }
    }
}
