using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Actions;

public sealed class ActionValidator : IActionValidator
{
    public ValidationResult Validate(BrowserAction action, PageState page)
    {
        switch (action.Type)
        {
            case BrowserActionType.Complete:
            case BrowserActionType.HumanIntervention:
            case BrowserActionType.Scroll:
            case BrowserActionType.GoBack:
            case BrowserActionType.Wait:
                return ValidationResult.Ok();

            case BrowserActionType.PressKey:
                return string.IsNullOrWhiteSpace(action.Key)
                    ? ValidationResult.Fail("PressKey requires a Key value.")
                    : ValidationResult.Ok();
        }

        // Everything else must target an element that exists, right now, in this observation.
        if (string.IsNullOrWhiteSpace(action.TargetId))
            return ValidationResult.Fail($"{action.Type} requires a TargetId.");

        var el = page.Elements.FirstOrDefault(e => e.Id == action.TargetId);
        if (el is null)
            return ValidationResult.Fail($"Target '{action.TargetId}' does not exist in the current page observation.");
        if (!el.Visible)
            return ValidationResult.Fail($"Target '{action.TargetId}' is not visible.");
        if (!el.Enabled)
            return ValidationResult.Fail($"Target '{action.TargetId}' is not enabled.");

        switch (action.Type)
        {
            case BrowserActionType.Type:
                if (string.IsNullOrEmpty(action.Value) && string.IsNullOrEmpty(action.ValueSource))
                    return ValidationResult.Fail("Type requires a Value or ValueSource.");
                break;

            case BrowserActionType.Select:
                if (string.IsNullOrEmpty(action.Value))
                    return ValidationResult.Fail("Select requires a Value.");
                var type = (el.Type ?? "").ToLowerInvariant();
                var role = (el.Role ?? "").ToLowerInvariant();
                if (type != "select" && role != "combobox")
                    return ValidationResult.Fail($"Target '{action.TargetId}' is not a select/dropdown element.");
                break;

            case BrowserActionType.Check:
            case BrowserActionType.Uncheck:
                var checkRole = (el.Role ?? "").ToLowerInvariant();
                if (checkRole != "checkbox" && checkRole != "radio")
                    return ValidationResult.Fail($"Target '{action.TargetId}' is not a checkbox/radio element.");
                break;
        }

        return ValidationResult.Ok();
    }
}
