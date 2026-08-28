using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace indian_ticketing;

public enum AutomationFailureKind { AccessDenied, ChallengeOrCaptcha, RateLimited, AuthenticationFailed, SessionExpired, UnexpectedRedirect, NetworkFailure, Timeout, Unknown }

/// <summary>Captures redacted browser diagnostics. It never retries, changes identity/network, or solves a challenge.</summary>
public static class AccessDeniedDiagnostics
{
    private static string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndianTicketing", "automation_diagnostics");

    public static async Task<AutomationFailureKind?> DetectAndCaptureAsync(CoreWebView2? core, int? httpStatusCode = null, bool useProxy = false, ProxyConfig? proxy = null)
    {
        if (core == null) return null;
        var bodyText = await ReadBodyTextAsync(core);
        var kind = Classify(bodyText, httpStatusCode);
        if (kind != null) await CaptureAsync(core, kind.Value, httpStatusCode, null, useProxy, proxy, bodyText);
        return kind;
    }

    public static async Task CaptureAsync(CoreWebView2? core, AutomationFailureKind kind, int? httpStatusCode = null, string? detail = null, bool useProxy = false, ProxyConfig? proxy = null, string? bodyText = null)
    {
        if (core == null) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var now = DateTimeOffset.UtcNow;
            var stamp = now.ToString("yyyyMMdd_HHmmss_fff");
            var url = RedactUrl((await core.ExecuteScriptAsync("location.href")).Trim('"'));
            bodyText ??= await ReadBodyTextAsync(core);
            var screenshotPath = Path.Combine(LogDir, $"{stamp}_{kind}.png");
            var htmlPath = Path.Combine(LogDir, $"{stamp}_{kind}.html");
            try { using var stream = new FileStream(screenshotPath, FileMode.Create); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }
            catch { screenshotPath = null; }
            try { await File.WriteAllTextAsync(htmlPath, await core.ExecuteScriptAsync("document.documentElement.outerHTML")); }
            catch { htmlPath = null; }

            var record = new { timestampUtc = now, kind = kind.ToString(), httpStatusCode, detail, url,
                akamaiReference = ExtractReference(bodyText), networkMode = useProxy && proxy != null ? $"Proxy: {proxy.Host}:{proxy.Port}" : "Direct", screenshotPath, htmlPath };
            await File.WriteAllTextAsync(Path.Combine(LogDir, $"{stamp}_{kind}.json"), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Diagnostics must never interrupt the workflow. */ }
    }

    public static string UserMessage(AutomationFailureKind kind) => kind switch
    {
        AutomationFailureKind.AccessDenied => "Access was denied by the site. The workflow stopped without retrying or changing network identity.",
        AutomationFailureKind.ChallengeOrCaptcha => "A CAPTCHA or browser challenge was presented. Complete it manually in the visible browser, then continue.",
        AutomationFailureKind.RateLimited => "The site rate-limited this session. The workflow stopped; wait for the site's permitted retry window before trying again.",
        AutomationFailureKind.AuthenticationFailed => "Authentication was rejected. Check the account and use the site's normal login flow.",
        AutomationFailureKind.SessionExpired => "The session expired. Sign in again through the visible browser before resuming.",
        _ => "The browser encountered a failure. Review the saved diagnostics before retrying."
    };

    private static AutomationFailureKind? Classify(string text, int? status)
    {
        var value = text.ToLowerInvariant();
        if (status == 403 || value.Contains("access denied") || value.Contains("you don't have permission")) return AutomationFailureKind.AccessDenied;
        if (status == 429 || value.Contains("too many requests") || value.Contains("rate limit")) return AutomationFailureKind.RateLimited;
        if (value.Contains("captcha") || value.Contains("verify you are human") || value.Contains("security challenge")) return AutomationFailureKind.ChallengeOrCaptcha;
        if (value.Contains("session expired") || value.Contains("session has expired")) return AutomationFailureKind.SessionExpired;
        if (value.Contains("invalid username") || value.Contains("invalid password") || value.Contains("authentication failed")) return AutomationFailureKind.AuthenticationFailed;
        return null;
    }

    private static async Task<string> ReadBodyTextAsync(CoreWebView2 core) { try { return (await core.ExecuteScriptAsync("document.body ? document.body.innerText : ''")).Trim('"'); } catch { return ""; } }
    private static string? ExtractReference(string text) { var m = System.Text.RegularExpressions.Regex.Match(text, @"Reference\s*#\s*([\w.\-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value : null; }
    private static string RedactUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : value;
}
