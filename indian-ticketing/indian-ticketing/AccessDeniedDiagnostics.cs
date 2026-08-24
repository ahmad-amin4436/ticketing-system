using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace indian_ticketing;

// Captures a lightweight diagnostic record whenever IRCTC's edge WAF
// (Akamai) returns its static "Access Denied" page instead of the real
// site: timestamp, URL, the Akamai reference number (if present), and a
// screenshot — appended to a small rolling log, so a block is diagnosable
// after the fact instead of just a bare error message. This does NOT retry
// or attempt to work around the block in any way — it only records what
// happened.
public static class AccessDeniedDiagnostics
{
    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IndianTicketing", "access_denied");

    // networkMode/proxy identify WHICH network path was active when the
    // block happened (e.g. "Direct" vs "Proxy: host:port") — never the
    // credentials, only host/port, so this log is safe to share for
    // troubleshooting without leaking the proxy password.
    // Returns the extracted Akamai reference number, if any, for use in
    // user-facing messages.
    public static async Task<string?> CaptureAsync(CoreWebView2? core, bool useProxy = false, ProxyConfig? proxy = null)
    {
        if (core == null) return null;
        try
        {
            Directory.CreateDirectory(LogDir);

            var url = (await core.ExecuteScriptAsync("location.href")).Trim('"');
            var refRaw = await core.ExecuteScriptAsync(@"(function(){
  var m = (document.body.innerText||'').match(/Reference\s*#\s*([\w.\-]+)/i);
  return m ? m[1] : '';
})()");
            var reference = refRaw.Trim('"');

            var now = DateTime.Now;
            var stamp = now.ToString("yyyyMMdd_HHmmss");
            string? screenshotPath = Path.Combine(LogDir, $"access_denied_{stamp}.png");
            try
            {
                using var stream = new FileStream(screenshotPath, FileMode.Create);
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }
            catch { screenshotPath = null; }

            var networkMode = useProxy && proxy != null ? $"Proxy: {proxy.Host}:{proxy.Port}" : "Direct";
            AppendLogEntry(now, url, reference, screenshotPath, networkMode);
            return string.IsNullOrEmpty(reference) ? null : reference;
        }
        catch { return null; }
    }

    private static void AppendLogEntry(
        DateTime timestamp, string url, string reference, string? screenshotPath, string networkMode)
    {
        var logPath = Path.Combine(LogDir, "access_denied_log.json");
        var entries = new List<Dictionary<string, object?>>();
        if (File.Exists(logPath))
        {
            try
            {
                entries = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(File.ReadAllText(logPath))
                          ?? new();
            }
            catch { entries = new(); }
        }

        entries.Add(new Dictionary<string, object?>
        {
            ["timestamp"] = timestamp.ToString("o"),
            ["networkMode"] = networkMode,
            ["url"] = url,
            ["akamaiReference"] = reference,
            ["screenshotPath"] = screenshotPath,
        });

        // Keep only the most recent 20 entries — this is a troubleshooting
        // aid, not an audit trail.
        if (entries.Count > 20) entries = entries.GetRange(entries.Count - 20, 20);

        File.WriteAllText(logPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
