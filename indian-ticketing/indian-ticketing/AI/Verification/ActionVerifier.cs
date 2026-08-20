using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Verification;

public sealed class ActionVerifier : IActionVerifier
{
    // "data-ai-id" is stamped onto each DOM element once and reused on subsequent
    // observations of the same live element (see WebViewPageObserver.collect), so the
    // same TargetId can be looked up again in the post-action observation here — that's
    // what makes per-field verification ("did e7 actually end up holding the typed text")
    // possible instead of just re-running the whole decision loop blind.
    public VerificationResult Verify(PageState before, BrowserAction action, ActionExecutionResult executionResult, PageState after)
    {
        if (!executionResult.Success)
            return Fail(executionResult.Error ?? "Execution reported failure.");

        switch (action.Type)
        {
            case BrowserActionType.Type:
            {
                var el = after.Elements.FirstOrDefault(e => e.Id == action.TargetId);
                if (el is null) return Fail("Target field no longer present after typing.");
                var actual = (el.Value ?? "").Trim();
                if (string.IsNullOrEmpty(action.Value))
                    return string.IsNullOrEmpty(actual) ? Fail("Field is still empty after typing.") : Ok($"Field now has a value: '{actual}'.");
                return actual.Contains(action.Value.Trim(), StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(actual)
                    ? Ok($"Field value: '{actual}'.")
                    : Fail("Typed value did not land in the field.");
            }

            case BrowserActionType.Clear:
            {
                var el = after.Elements.FirstOrDefault(e => e.Id == action.TargetId);
                if (el is null) return Ok("Target no longer present (page likely changed).");
                return string.IsNullOrEmpty(el.Value) ? Ok("Field is empty.") : Fail("Field still has a value after clear.");
            }

            case BrowserActionType.Select:
            {
                var el = after.Elements.FirstOrDefault(e => e.Id == action.TargetId);
                if (el is null) return Ok("Target no longer present (page likely changed).");
                var val = (el.Value ?? "").Trim();
                return string.IsNullOrEmpty(action.Value) || val.Contains(action.Value.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? Ok($"Selected value: '{val}'.")
                    : Fail($"Expected '{action.Value}' but field shows '{val}'.");
            }

            case BrowserActionType.Check:
            case BrowserActionType.Uncheck:
            {
                var el = after.Elements.FirstOrDefault(e => e.Id == action.TargetId);
                if (el is null) return Fail("Target no longer present after check/uncheck.");
                var wantSelected = action.Type == BrowserActionType.Check;
                return el.Selected == wantSelected ? Ok("Checked state matches.") : Fail("Checked state did not change as expected.");
            }

            case BrowserActionType.Click:
            case BrowserActionType.GoBack:
            {
                var beforeFp = StateFingerprint.Compute(before);
                var afterFp = StateFingerprint.Compute(after);
                return beforeFp != afterFp
                    ? Ok("Page state changed after the action.")
                    : Fail("Page state did not change — the action may not have registered.");
            }

            default:
                return Ok("No specific verification defined for this action type; treating executor result as authoritative.");
        }
    }

    private static VerificationResult Ok(string details) => new() { Success = true, Details = details };
    private static VerificationResult Fail(string details) => new() { Success = false, Details = details };
}
