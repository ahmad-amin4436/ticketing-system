using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.WebsiteAdapters;

/// <summary>
/// Isolates unavoidable website-specific knowledge (extra selectors a particular site's
/// framework needs) away from the generic adaptive engine, per the "no hardcoded workflow"
/// rule — an adapter may widen what the observer looks at, but it never encodes a sequence
/// of steps or page-specific navigation logic.
/// </summary>
public interface IWebsiteAdapter
{
    bool CanHandle(PageState state);

    /// <summary>Optional post-processing of an observed state; return it unchanged if none is needed.</summary>
    PageState Normalize(PageState state);

    /// <summary>Extra CSS selectors this site's framework needs beyond the generic element set.</summary>
    IReadOnlyList<string> ExtraElementSelectors { get; }
}
