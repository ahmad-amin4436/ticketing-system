using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace indian_ticketing;

public enum AutomationFailureKind { AccessDenied, ChallengeOrCaptcha, RateLimited, AuthenticationFailed, SessionExpired, UnexpectedRedirect, NetworkFailure, Timeout, Unknown }

/// <summary>A single Akamai-relevant cookie as captured for diagnostics.</summary>
public class AkamaiCookieDiag
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public string? ExpiresUtc { get; set; }
    /// <summary>Short redacted preview (never the full token value), so session
    /// secrets aren't written to disk.</summary>
    public string ValuePreview { get; set; } = "";
}

/// <summary>Captured anti-bot signals from the edge layer. Filled in during a
/// navigation by <see cref="AkamaiResponseWatcher"/> (headers + status) and/or
/// lazily from the cookie jar (Akamai cookies).</summary>
public class AkamaiDiagInfo
{
    /// <summary>Response status of the first Akamai-tagged response observed.</summary>
    public int? ResponseStatus { get; set; }

    /// <summary>URL of that response (path only, no query string).</summary>
    public string? ResponseUrl { get; set; }

    /// <summary>Relevant edge/anti-bot response headers (X-Akamai-*, Server, …).</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Akamai / anti-bot cookies observed in the cookie jar.</summary>
    public List<AkamaiCookieDiag> Cookies { get; set; } = new();
}

/// <summary>Captures redacted browser diagnostics. It never retries, changes identity/network, or solves a challenge.</summary>
public static class AccessDeniedDiagnostics
{
    private static string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IndianTicketing", "automation_diagnostics");

    // ── Reusable response-header capture ──────────────────────────────────
    // WebView2 exposes response headers only asynchronously through the
    // WebResourceResponseReceived event — there is no "get the last response's
    // headers" call — so a caller that wants them must subscribe around a
    // navigation and read the event stream. This tiny watcher collects just the
    // anti-bot-relevant signals and detaches itself on Dispose.
    public static AkamaiResponseWatcher WatchAkamaiResponses(CoreWebView2 core, AkamaiDiagInfo sink)
        => new(core, sink);

    public sealed class AkamaiResponseWatcher : IDisposable
    {
        private readonly CoreWebView2 _core;
        private readonly AkamaiDiagInfo _sink;

        public AkamaiResponseWatcher(CoreWebView2 core, AkamaiDiagInfo sink)
        {
            _core = core;
            _sink = sink;
            core.WebResourceResponseReceived += OnResponse;
        }

        private void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                var resp = e.Response;
                if (resp == null) return;
                if (!HasAkamaiHeaders(resp.Headers)) return;

                // Prefer the first Akamai-tagged response; keep its status/url.
                _sink.ResponseStatus ??= resp.StatusCode;
                _sink.ResponseUrl ??= RedactUrl(e.Request?.Uri);

                foreach (var (name, value) in resp.Headers)
                {
                    if (IsRelevantHeader(name))
                        _sink.Headers.TryAdd(name, value);
                }
            }
            catch { /* diagnostics must never break navigation */ }
        }

        public void Dispose()
        {
            _core.WebResourceResponseReceived -= OnResponse;
        }
    }

    private static bool HasAkamaiHeaders(CoreWebView2HttpResponseHeaders headers)
    {
        if (headers == null) return false;
        foreach (var (name, _) in headers)
            if (IsRelevantHeader(name)) return true;
        return false;
    }

    // Anything that can tell us "this traffic is proxied through an Akamai edge
    // and what that edge decided". Server + via too, since some edges only leak
    // an identification through those.
    private static bool IsRelevantHeader(string name) =>
        name.StartsWith("X-Akamai", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Via", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-CDN", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Edge", StringComparison.OrdinalIgnoreCase);

    // ── Cookie capture ────────────────────────────────────────────────────
    // Akamai's anti-bot layer sets _abck (long-lived behavioural tracking) and
    // ak_bmsc (short-lived session cookie) among others. Reading those names —
    // and their flags, which Akamai's challenge logic varies on — is the signal
    // that answers "was the edge rejecting on reputation/behaviour".
    public static async Task CaptureAkamaiCookiesAsync(CoreWebView2 core, AkamaiDiagInfo sink, string origin = "https://www.irctc.co.in/")
    {
        try
        {
            var list = await core.CookieManager.GetCookiesAsync(origin);
            if (list == null) return;
            foreach (var cookie in list)
            {
                if (!IsAkamaiCookie(cookie.Name)) continue;
                sink.Cookies.Add(new AkamaiCookieDiag
                {
                    Name = cookie.Name,
                    Domain = cookie.Domain,
                    HttpOnly = cookie.IsHttpOnly,
                    Secure = cookie.IsSecure,
                    ExpiresUtc = cookie.Expires > DateTime.MinValue ? cookie.Expires.ToUniversalTime().ToString("O") : "(session)",
                    ValuePreview = Preview(cookie.Value),
                });
            }
        }
        catch { /* diagnostics must never break the workflow */ }
    }

    // _abck, ak_bmsc plus the wider family (bm_sz, akamai-*, _bm_*, etc.).
    private static bool IsAkamaiCookie(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.ToLowerInvariant();
        if (n == "_abck" || n == "ak_bmsc" || n == "bm_sz" || n == "bm_mi") return true;
        return n.StartsWith("akamai", StringComparison.Ordinal) ||
               n.StartsWith("_bm_", StringComparison.Ordinal) ||
               n.StartsWith("bm_", StringComparison.Ordinal);
    }

    // Redact cookie tokens: show enough to tell two sessions apart (and prove
    // the cookie actually exists/rotated) without persisting the full secret.
    private static string Preview(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= 16 ? value
             : value[..8] + "…(" + value.Length + " chars)";
    }

    // ── Existing capture, extended with Akamai signals ─────────────────────
    public static async Task<AutomationFailureKind?> DetectAndCaptureAsync(CoreWebView2? core, int? httpStatusCode = null, bool useProxy = false, ProxyConfig? proxy = null, AkamaiDiagInfo? akamai = null)
    {
        if (core == null) return null;
        var bodyText = await ReadBodyTextAsync(core);
        var kind = Classify(bodyText, httpStatusCode);
        if (kind != null) await CaptureAsync(core, kind.Value, httpStatusCode, null, useProxy, proxy, bodyText, akamai);
        return kind;
    }

    public static async Task CaptureAsync(CoreWebView2? core, AutomationFailureKind kind, int? httpStatusCode = null, string? detail = null, bool useProxy = false, ProxyConfig? proxy = null, string? bodyText = null, AkamaiDiagInfo? akamai = null)
    {
        if (core == null) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var now = DateTimeOffset.UtcNow;
            var stamp = now.ToString("yyyyMMdd_HHmmss_fff");
            var url = RedactUrl((await core.ExecuteScriptAsync("location.href")).Trim('"'));
            bodyText ??= await ReadBodyTextAsync(core);

            // Pull Akamai cookies lazily if the caller didn't already supply them.
            akamai ??= new AkamaiDiagInfo();
            if (akamai.Cookies.Count == 0)
                await CaptureAkamaiCookiesAsync(core, akamai);

            var screenshotPath = Path.Combine(LogDir, $"{stamp}_{kind}.png");
            var htmlPath = Path.Combine(LogDir, $"{stamp}_{kind}.html");
            try { using var stream = new FileStream(screenshotPath, FileMode.Create); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }
            catch { screenshotPath = null; }
            try { await File.WriteAllTextAsync(htmlPath, await core.ExecuteScriptAsync("document.documentElement.outerHTML")); }
            catch { htmlPath = null; }

            var record = new
            {
                timestampUtc = now,
                kind = kind.ToString(),
                httpStatusCode,
                detail,
                url,
                akamaiReference = ExtractReference(bodyText),
                networkMode = useProxy && proxy != null ? $"Proxy: {proxy.Host}:{proxy.Port}" : "Direct",
                akamai = new
                {
                    responseStatus = akamai.ResponseStatus,
                    responseUrl = akamai.ResponseUrl,
                    headers = akamai.Headers.Count > 0 ? akamai.Headers : null,
                    cookies = akamai.Cookies.Count > 0 ? akamai.Cookies : null,
                },
                screenshotPath,
                htmlPath
            };
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
    private static string RedactUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : (value ?? "");
}
