namespace indian_ticketing.AI.Observation;

/// <summary>A normalized snapshot of the currently loaded page, built for AI consumption.</summary>
public sealed class PageState
{
    public string Url { get; init; } = "";

    public string Title { get; init; } = "";

    public string VisibleText { get; init; } = "";

    public IReadOnlyList<PageElement> Elements { get; init; } = Array.Empty<PageElement>();

    public string? ScreenshotPath { get; init; }

    public DateTime ObservedAt { get; init; } = DateTime.UtcNow;
}
