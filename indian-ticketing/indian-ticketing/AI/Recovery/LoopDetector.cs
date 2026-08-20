using indian_ticketing.AI.Actions;
using indian_ticketing.AI.Observation;

namespace indian_ticketing.AI.Recovery;

/// <summary>Flags a (state, action) pair repeating past a configurable threshold — the classic "click A, back, click A again" stall.</summary>
public sealed class LoopDetector
{
    private readonly int _maxRepeats;
    private readonly Dictionary<string, int> _counts = new();

    public LoopDetector(int maxRepeats) => _maxRepeats = Math.Max(1, maxRepeats);

    public bool RegisterAndCheckLoop(PageState state, BrowserAction action)
    {
        var key = StateFingerprint.Compute(state) + "|" + action.Type + "|" + (action.TargetId ?? "") + "|" + (action.Value ?? "");
        _counts.TryGetValue(key, out var count);
        count++;
        _counts[key] = count;
        return count > _maxRepeats;
    }
}
