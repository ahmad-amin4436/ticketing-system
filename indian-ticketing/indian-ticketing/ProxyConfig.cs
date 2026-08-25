using System.Net;
using System.Text;
using System.Text.Json;

namespace indian_ticketing;

public class ProxyConfig
{
    public bool   Enabled  { get; set; } = false;
    public string Host     { get; set; } = "";
    public int    Port     { get; set; } = 0;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Host) && Port > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Username);

    [System.Text.Json.Serialization.JsonIgnore]
    public string ServerArg => IsConfigured ? $"http://{Host}:{Port}" : "";

    [System.Text.Json.Serialization.JsonIgnore]
    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "IndianTicketing", "proxy_config.json");

    // Default proxy for a fresh install (no saved config yet) — the one this
    // app has been configured against throughout development. Still shown
    // via the Proxy field / ON-OFF toggle, so it's just a starting point,
    // not fixed in place.
    private static ProxyConfig DefaultConfig() => new()
    {
        Enabled  = true,
        Host     = "103.148.67.253",
        Port     = 3128,
        Username = "demo",
        Password = "demo",
    };

    // ── Persistence ──────────────────────────────────────────────────────
    public static ProxyConfig Load()
    {
        if (!File.Exists(StorePath)) return DefaultConfig();
        try
        {
            return JsonSerializer.Deserialize<ProxyConfig>(File.ReadAllText(StorePath))
                   ?? DefaultConfig();
        }
        catch { return DefaultConfig(); }
    }

    public static void Save(ProxyConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Parsing ──────────────────────────────────────────────────────────
    // Supported formats:
    //   HOST:PORT:USERNAME:PASSWORD    (the user's format)
    //   USERNAME:PASSWORD@HOST:PORT
    //   HOST:PORT                      (no auth)
    public static ProxyConfig Parse(string text, out string? error)
    {
        error = null;
        text = text.Trim();

        if (string.IsNullOrEmpty(text) || text == "user:pass@host:port")
            return new ProxyConfig { Enabled = false };

        string host, username = "", password = "";
        int port;

        // Format A: USERNAME:PASSWORD@HOST:PORT
        var atIdx = text.LastIndexOf('@');
        if (atIdx > 0)
        {
            var creds = text[..atIdx];
            var addr  = text[(atIdx + 1)..];
            var colonIdx = creds.IndexOf(':');
            if (colonIdx >= 0)
            {
                username = creds[..colonIdx];
                password = creds[(colonIdx + 1)..];
            }
            else
            {
                username = creds;
            }

            // Parse HOST:PORT from the address portion
            var pIdx = addr.LastIndexOf(':');
            if (pIdx < 0)
            {
                error = "Missing port after host. Format: user:pass@host:port";
                return new ProxyConfig();
            }
            host = addr[..pIdx];
            if (!int.TryParse(addr[(pIdx + 1)..], out port) || port <= 0 || port > 65535)
            {
                error = $"Invalid port number: '{addr[(pIdx + 1)..]}'";
                return new ProxyConfig();
            }
        }
        // Format B: HOST:PORT:USERNAME:PASSWORD  or  HOST:PORT
        else
        {
            var parts = text.Split(':');
            if (parts.Length < 2)
            {
                error = "Missing port. Format: host:port or host:port:user:pass";
                return new ProxyConfig();
            }

            host = parts[0];
            if (!int.TryParse(parts[1], out port) || port <= 0 || port > 65535)
            {
                error = $"Invalid port number: '{parts[1]}'";
                return new ProxyConfig();
            }

            if (parts.Length >= 4)
            {
                username = parts[2];
                password = parts[3];
            }
            else if (parts.Length == 3)
            {
                error = "Partial credentials: provide both username and password (host:port:user:pass)";
                return new ProxyConfig();
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host is empty.";
            return new ProxyConfig();
        }

        return new ProxyConfig
        {
            Enabled  = true,
            Host     = host,
            Port     = port,
            Username = username,
            Password = password,
        };
    }

    // ── For HttpClient ───────────────────────────────────────────────────
    public WebProxy? ToWebProxy()
    {
        if (!IsConfigured) return null;
        var proxy = new WebProxy(Host, Port);
        if (HasCredentials)
            proxy.Credentials = new NetworkCredential(Username, Password);
        return proxy;
    }

    public HttpClientHandler ApplyToHandler(HttpClientHandler handler)
    {
        var proxy = ToWebProxy();
        if (proxy != null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        return handler;
    }

    // ── For ChromeDriver / WebView2 ──────────────────────────────────────
    public string GetProxyServerArg() => IsConfigured ? $"--proxy-server={ServerArg}" : "";

    public void ApplyToChromeOptions(OpenQA.Selenium.Chrome.ChromeOptions opts)
    {
        if (!IsConfigured) return;
        opts.AddArgument(GetProxyServerArg());
        // NOTE: --proxy-server does NOT carry credentials.
        // The caller MUST also load the proxy auth extension if HasCredentials.
    }

    // ── Proxy Auth Extension ─────────────────────────────────────────────
    // Generates a temporary Chrome extension (MV2) that handles proxy auth
    // via webRequest.onAuthRequired. This is the only reliable way to supply
    // HTTP proxy credentials to Chrome/Chromium/WebView2.

    private static string? _extensionPath;

    public static string? EnsureAuthExtension(ProxyConfig proxy)
    {
        if (!proxy.HasCredentials || !proxy.IsConfigured)
            return null;

        if (_extensionPath != null && Directory.Exists(_extensionPath))
            return _extensionPath;

        var extDir = Path.Combine(
            Path.GetTempPath(), "IndianTicketing", "proxy_auth_ext");
        Directory.CreateDirectory(extDir);

        // manifest.json — MV2 (supported by Chrome 124 and Edge WebView2)
        var manifest = $$"""
        {
          "manifest_version": 2,
          "name": "Proxy Auth Helper",
          "version": "1.0",
          "description": "Provides HTTP proxy authentication credentials",
          "permissions": [
            "webRequest",
            "webRequestBlocking",
            "<all_urls>"
          ],
          "background": {
            "scripts": ["background.js"]
          },
          "minimum_chrome_version": "88"
        }
        """;

        // background.js — intercepts 407 challenges and supplies credentials
        var backgroundJs = $$"""
        chrome.webRequest.onAuthRequired.addListener(
          function(details) {
            return {
              authCredentials: {
                username: "{{EscapeJs(proxy.Username)}}",
                password: "{{EscapeJs(proxy.Password)}}"
              }
            };
          },
          {urls: ["<all_urls>"]},
          ["blocking"]
        );
        """;

        File.WriteAllText(Path.Combine(extDir, "manifest.json"), manifest,
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(extDir, "background.js"), backgroundJs,
            new UTF8Encoding(false));

        _extensionPath = extDir;
        return _extensionPath;
    }

    private static string EscapeJs(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "\\'");

    // ── Diagnostics ──────────────────────────────────────────────────────
    public string DiagnosticSummary()
    {
        if (!IsConfigured)
            return "Proxy configured: NO (all requests go direct)";

        return $"Proxy configured: YES\n" +
               $"Proxy host: {Host}\n" +
               $"Proxy port: {Port}\n" +
               $"Proxy auth configured: {HasCredentials}\n" +
               $"Proxy server arg: {ServerArg}";
    }

    public override string ToString()
    {
        if (!IsConfigured) return "(no proxy)";
        var auth = HasCredentials ? $"{Username}:***@{Host}:{Port}" : $"{Host}:{Port}";
        return auth;
    }
}
