using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.WebsiteAdapters;

/// <summary>
/// IRCTC renders most of its interactive controls as PrimeNG components (p-dropdown,
/// p-autocomplete, p-calendar, p-radiobutton) whose real clickable surface is a wrapper
/// <c>div</c>/<c>span</c>, not a native form element. This adapter only widens the
/// observer's element-discovery selector set to include those known wrapper classes —
/// it does not encode any page order or workflow steps, so the core agent loop stays
/// generic to any site.
/// </summary>
public sealed class IrctcWebsiteAdapter : IWebsiteAdapter
{
    public bool CanHandle(PageState state)
        => !string.IsNullOrEmpty(state.Url) && state.Url.Contains("irctc.co.in", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> ExtraElementSelectors { get; } = new[]
    {
        ".ui-dropdown", ".p-dropdown", ".ui-dropdown-trigger", ".p-dropdown-trigger",
        ".ui-radiobutton-box", ".p-radiobutton-box",
        ".ui-autocomplete-list-item", ".p-autocomplete-item", "li.ui-corner-all",
        "p-calendar input", "p-autocomplete input", "p-dropdown",
    };

    public PageState Normalize(PageState state) => state;
}
