using System.Security.Cryptography;
using System.Text;

namespace indian_ticketing.AI.Observation;

/// <summary>
/// Cheap, deterministic fingerprint of a PageState, used by the loop detector and the
/// click verifier to answer "did anything actually change" without re-sending state to
/// an LLM just to find out.
/// </summary>
public static class StateFingerprint
{
    public static string Compute(PageState state)
    {
        var sb = new StringBuilder();
        sb.Append(state.Url).Append('|').Append(state.Title).Append('|').Append(state.Elements.Count);

        foreach (var e in state.Elements.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            sb.Append('|').Append(e.Id).Append(':').Append(e.Type).Append(':').Append(e.Label)
              .Append(':').Append(e.Value).Append(':').Append(e.Visible)
              .Append(':').Append(e.Enabled).Append(':').Append(e.Selected);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..16];
    }
}
