namespace indian_ticketing.AI.Actions;

/// <summary>
/// The complete, closed set of actions the AI may request. There is no "run JavaScript",
/// "run shell command", or "run arbitrary C#" member here on purpose — the enum itself is
/// the enforcement mechanism, not a runtime check the AI could talk its way around.
/// </summary>
public enum BrowserActionType
{
    Click,
    Type,
    Clear,
    Select,
    Check,
    Uncheck,
    Scroll,
    PressKey,
    Wait,
    GoBack,
    Complete,
    HumanIntervention,
}
