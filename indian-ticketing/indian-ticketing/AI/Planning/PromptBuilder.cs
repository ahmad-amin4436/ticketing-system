using System.Text;
using System.Text.Json;
using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Goals;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Planning;

/// <summary>
/// Single source of truth for the prompt sent to Ollama every cycle. Deliberately never
/// includes the full DOM, prior history beyond the immediately previous step, or real
/// credentials — see spec §13/§26/§42. Built once here rather than duplicated per caller.
/// </summary>
public static class PromptBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private const string SystemPreamble = """
        You are an adaptive browser navigation agent.

        Your task is to achieve the supplied USER GOAL by interacting with the CURRENT PAGE
        state below. You do not know this website's fixed workflow. Never assume a particular
        page comes before or after another. Use only the currently observed page state and the
        goal - never invent elements, and only target an element "id" that is present in
        CURRENT PAGE.elements.

        Choose exactly ONE next action toward the goal, then stop - the page will be re-observed
        after your action executes, and you will be asked again.

        Respond with exactly one JSON object, no other text, matching this shape:
        {
          "status": "ACTION_REQUIRED" | "WAITING" | "COMPLETED" | "FAILED" | "HUMAN_INTERVENTION_REQUIRED" | "UNKNOWN",
          "action": {
            "type": "CLICK" | "TYPE" | "CLEAR" | "SELECT" | "CHECK" | "UNCHECK" | "SCROLL" | "PRESS_KEY" | "WAIT" | "GO_BACK",
            "targetId": "<element id from CURRENT PAGE.elements, e.g. e3>",
            "value": "<text to type or option to select, if applicable>",
            "valueSource": "SECURE_USERNAME" | "SECURE_PASSWORD" | null,
            "key": "<key name for PRESS_KEY, e.g. Enter>",
            "amount": <integer, for SCROLL pixels or WAIT milliseconds>
          },
          "reason": "<one short sentence>",
          "confidence": <number 0.0-1.0>,
          "expectedOutcome": "<one short sentence>"
        }
        "action" is required only when status is "ACTION_REQUIRED"; omit or null it otherwise.

        For any field the user's real IRCTC username or password would go into (a login username
        or password input), NEVER output the real value - set "valueSource" to "SECURE_USERNAME"
        or "SECURE_PASSWORD" instead and leave "value" null. You will never be given the real
        credentials, so do not attempt to guess or fabricate them.

        Use status "COMPLETED" only when the page clearly shows the goal has been achieved (a
        confirmation, booking reference, or success message) - not just because a final-looking
        button was clicked.

        Never attempt to bypass, guess, or work around a CAPTCHA, MFA/OTP challenge, or any other
        security or anti-bot control. If the page shows one of these, or anything else you cannot
        safely resolve alone, respond with status "HUMAN_INTERVENTION_REQUIRED" and a short reason
        instead of guessing.

        Do not return C#, JavaScript, Selenium, or Playwright code - only the JSON object above.
        """;

    public static string Build(BookingGoal goal, PageState page, BrowserAction? previousAction, ActionExecutionResult? previousResult)
    {
        var goalPayload = new
        {
            goalId = goal.GoalId,
            origin = goal.Origin,
            destination = goal.Destination,
            journeyDate = goal.JourneyDate,
            trainNumber = goal.TrainNumber,
            travelClass = goal.TravelClass,
            quota = goal.Quota,
            passengers = goal.Passengers.Select(p => new { p.Name, p.Age, p.Gender }),
        };

        var pagePayload = new
        {
            url = page.Url,
            title = page.Title,
            visibleText = Truncate(page.VisibleText, 400),
            elements = page.Elements.Select(e => new
            {
                id = e.Id,
                type = e.Type,
                role = e.Role,
                label = e.Label,
                placeholder = e.Placeholder,
                value = e.Value,
                enabled = e.Enabled,
                selected = e.Selected,
            }),
        };

        object? prevActionPayload = previousAction is null ? null : new
        {
            type = previousAction.Type.ToString(),
            targetId = previousAction.TargetId,
            value = previousAction.Value,
        };

        object? prevResultPayload = previousResult is null ? null : new
        {
            success = previousResult.Success,
            error = previousResult.Error,
        };

        var sb = new StringBuilder();
        sb.AppendLine(SystemPreamble);
        sb.AppendLine();
        sb.AppendLine("USER GOAL");
        sb.AppendLine(JsonSerializer.Serialize(goalPayload, JsonOpts));
        sb.AppendLine();
        sb.AppendLine("CURRENT PAGE");
        sb.AppendLine(JsonSerializer.Serialize(pagePayload, JsonOpts));
        sb.AppendLine();
        sb.AppendLine("PREVIOUS ACTION");
        sb.AppendLine(JsonSerializer.Serialize(prevActionPayload, JsonOpts));
        sb.AppendLine();
        sb.AppendLine("PREVIOUS ACTION RESULT");
        sb.AppendLine(JsonSerializer.Serialize(prevResultPayload, JsonOpts));

        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
