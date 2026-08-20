namespace indian_ticketing.AI.Actions;

public interface IActionExecutor
{
    Task<ActionExecutionResult> ExecuteAsync(BrowserAction action, CancellationToken cancellationToken = default);
}
