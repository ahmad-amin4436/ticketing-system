using System.Text.Json;
using indian_ticketing.AI.Actions;

namespace indian_ticketing.AI.Planning;

/// <summary>
/// Never trusts the model's raw text output. Extracts a JSON object (models sometimes wrap
/// it in prose or a markdown code fence despite format:"json"), schema-validates it, and
/// only then produces an AiDecision. Malformed output becomes a parse error the caller can
/// retry or escalate on - it is never partially executed.
/// </summary>
public static class DecisionParser
{
    public sealed class ParseOutcome
    {
        public AiDecision? Decision { get; init; }
        public string? Error { get; init; }
        public bool Success => Decision is not null;
    }

    // Deliberately not a strict typed Deserialize<T>: local models are inconsistent about
    // JSON types even when told to use a schema (observed in practice: a valid decision
    // with "amount" sent as a JSON string instead of a number, which a strict deserializer
    // rejects outright). Every field below is coerced defensively instead of assumed.
    public static ParseOutcome Parse(string rawResponse, string modelUsed)
    {
        var json = ExtractJsonObject(rawResponse);
        if (json is null)
            return new ParseOutcome { Error = "No JSON object found in model response." };

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { return new ParseOutcome { Error = $"JSON parse error: {ex.Message}" }; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new ParseOutcome { Error = "Top-level JSON value is not an object." };

            var statusRaw = GetString(root, "status");
            if (!TryParseStatus(statusRaw, out var status))
                return new ParseOutcome { Error = $"Unrecognized status '{statusRaw}'." };

            var reason = GetString(root, "reason") ?? "";
            var confidence = GetDouble(root, "confidence");
            var expectedOutcome = GetString(root, "expectedOutcome");

            BrowserAction? action = null;
            if (status == AgentStatus.ActionRequired)
            {
                if (!TryGetObject(root, "action", out var actionEl))
                    return new ParseOutcome { Error = "status ACTION_REQUIRED but no action provided." };

                var typeRaw = GetString(actionEl, "type");
                if (!TryParseActionType(typeRaw, out var type))
                    return new ParseOutcome { Error = $"Unrecognized action type '{typeRaw}'." };

                action = new BrowserAction
                {
                    Type = type,
                    TargetId = GetString(actionEl, "targetId"),
                    Value = GetString(actionEl, "value"),
                    ValueSource = GetString(actionEl, "valueSource"),
                    Key = GetString(actionEl, "key"),
                    Amount = GetInt(actionEl, "amount"),
                    Confidence = confidence,
                    Reason = reason,
                    ExpectedOutcome = expectedOutcome,
                };
            }

            var decision = new AiDecision
            {
                Status = status,
                Action = action,
                Reason = reason,
                Confidence = confidence,
                ExpectedOutcome = expectedOutcome,
                ModelUsed = modelUsed,
            };
            return new ParseOutcome { Decision = decision };
        }
    }

    private static bool TryGetObject(JsonElement obj, string prop, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(prop, out value)
            && value.ValueKind == JsonValueKind.Object)
            return true;
        value = default;
        return false;
    }

    // Accepts the property as a JSON string, number, or bool and coerces to string; returns
    // null for a JSON null, a missing property, or an empty string.
    private static string? GetString(JsonElement obj, string prop)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el))
            return null;
        var s = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
        return string.IsNullOrEmpty(s) ? null : s;
    }

    // Accepts the property as a JSON number or a numeric-looking string; anything else (or
    // missing/null) becomes null rather than a thrown exception.
    private static int? GetInt(JsonElement obj, string prop)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.Number when el.TryGetDouble(out var d) => (int)d,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i2) => i2,
            _ => null,
        };
    }

    private static double GetDouble(JsonElement obj, string prop)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el))
            return 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(el.GetString(), out var d2) => d2,
            _ => 0,
        };
    }

    private static string? ExtractJsonObject(string text)
    {
        text = text.Trim();

        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = text[(fenceStart + 3)..].TrimStart();
            if (afterFence.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                afterFence = afterFence[4..];
            var fenceEnd = afterFence.IndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) text = afterFence[..fenceEnd].Trim();
        }

        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[start..(i + 1)];
            }
        }
        return null;
    }

    private static bool TryParseStatus(string? s, out AgentStatus status)
    {
        status = AgentStatus.Unknown;
        if (string.IsNullOrWhiteSpace(s)) return false;
        switch (s.Trim().ToUpperInvariant())
        {
            case "ACTION_REQUIRED": status = AgentStatus.ActionRequired; return true;
            case "WAITING": status = AgentStatus.Waiting; return true;
            case "COMPLETED": status = AgentStatus.Completed; return true;
            case "FAILED": status = AgentStatus.Failed; return true;
            case "HUMAN_INTERVENTION_REQUIRED": status = AgentStatus.HumanInterventionRequired; return true;
            case "UNKNOWN": status = AgentStatus.Unknown; return true;
            default: return false;
        }
    }

    private static bool TryParseActionType(string? s, out BrowserActionType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        switch (s.Trim().ToUpperInvariant())
        {
            case "CLICK": type = BrowserActionType.Click; return true;
            case "TYPE": type = BrowserActionType.Type; return true;
            case "CLEAR": type = BrowserActionType.Clear; return true;
            case "SELECT": type = BrowserActionType.Select; return true;
            case "CHECK": type = BrowserActionType.Check; return true;
            case "UNCHECK": type = BrowserActionType.Uncheck; return true;
            case "SCROLL": type = BrowserActionType.Scroll; return true;
            case "PRESS_KEY": case "PRESSKEY": type = BrowserActionType.PressKey; return true;
            case "WAIT": type = BrowserActionType.Wait; return true;
            case "GO_BACK": case "GOBACK": type = BrowserActionType.GoBack; return true;
            case "COMPLETE": type = BrowserActionType.Complete; return true;
            case "HUMAN_INTERVENTION": type = BrowserActionType.HumanIntervention; return true;
            default: return false;
        }
    }
}
