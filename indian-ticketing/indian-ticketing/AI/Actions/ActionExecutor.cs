using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Actions;

/// <summary>
/// Translates a validated BrowserAction into a call on the observer's low-level
/// interaction primitives. Resolves "SECURE_USERNAME"/"SECURE_PASSWORD" placeholders via
/// a caller-supplied resolver so the real credential value only ever exists here, briefly,
/// on its way into the browser — it is never part of an AI prompt or response, and never
/// logged (see AgentLogger's masking).
/// </summary>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly WebViewPageObserver _observer;
    private readonly Func<string, string?> _secureValueResolver;

    public ActionExecutor(WebViewPageObserver observer, Func<string, string?> secureValueResolver)
    {
        _observer = observer;
        _secureValueResolver = secureValueResolver;
    }

    public async Task<ActionExecutionResult> ExecuteAsync(BrowserAction action, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (action.Type)
            {
                case BrowserActionType.Click:
                {
                    var ok = await _observer.ClickElementAsync(action.TargetId!, cancellationToken);
                    return Result(ok, ok ? null : "Click did not resolve a clickable target.");
                }
                case BrowserActionType.Type:
                {
                    var value = ResolveValue(action);
                    if (value is null) return Result(false, "Could not resolve a value to type.");
                    var ok = await _observer.TypeIntoElementAsync(action.TargetId!, value, cancellationToken);
                    return Result(ok, ok ? null : "Type did not land in the target field.");
                }
                case BrowserActionType.Clear:
                {
                    var ok = await _observer.ClearElementAsync(action.TargetId!, cancellationToken);
                    return Result(ok, ok ? null : "Clear failed.");
                }
                case BrowserActionType.Select:
                {
                    var ok = await _observer.SelectOptionAsync(action.TargetId!, action.Value ?? "", cancellationToken);
                    return Result(ok, ok ? null : "Select failed.");
                }
                case BrowserActionType.Check:
                {
                    var ok = await _observer.SetCheckedAsync(action.TargetId!, true, cancellationToken);
                    return Result(ok, ok ? null : "Check failed.");
                }
                case BrowserActionType.Uncheck:
                {
                    var ok = await _observer.SetCheckedAsync(action.TargetId!, false, cancellationToken);
                    return Result(ok, ok ? null : "Uncheck failed.");
                }
                case BrowserActionType.Scroll:
                    await _observer.ScrollAsync(action.Amount ?? 400, cancellationToken);
                    return Result(true, null);

                case BrowserActionType.PressKey:
                    await _observer.PressKeyAsync(action.Key ?? "Enter", cancellationToken);
                    return Result(true, null);

                case BrowserActionType.Wait:
                    await Task.Delay(Math.Clamp(action.Amount ?? 1000, 100, 15000), cancellationToken);
                    return Result(true, null);

                case BrowserActionType.GoBack:
                    await _observer.GoBackAsync(cancellationToken);
                    return Result(true, null);

                case BrowserActionType.Complete:
                case BrowserActionType.HumanIntervention:
                    return Result(true, null);

                default:
                    return Result(false, $"Unsupported action type: {action.Type}");
            }
        }
        catch (Exception ex)
        {
            return Result(false, ex.Message);
        }
    }

    private string? ResolveValue(BrowserAction action)
        => !string.IsNullOrEmpty(action.ValueSource) ? _secureValueResolver(action.ValueSource) : action.Value;

    private static ActionExecutionResult Result(bool success, string? error) =>
        new() { Success = success, Error = error };
}
