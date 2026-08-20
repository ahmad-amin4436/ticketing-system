namespace indian_ticketing.AI.Observation;

/// <summary>
/// One interactive (or otherwise relevant) DOM element as seen by the adaptive agent.
/// "Id" is a temporary observation identifier (a "data-ai-id" attribute stamped onto the
/// live DOM element) — it is only meaningful for the observe/decide/execute round trip it
/// was produced in, not a durable selector.
/// </summary>
public sealed class PageElement
{
    public string Id { get; init; } = "";

    public string Type { get; init; } = "";

    public string? Role { get; init; }

    public string? Text { get; init; }

    public string? Label { get; init; }

    public string? Placeholder { get; init; }

    public string? Value { get; init; }

    public bool Visible { get; init; }

    public bool Enabled { get; init; }

    public bool Selected { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
