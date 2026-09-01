# Authorized browser automation

The application uses a visible, supported WebView2 browser with a persistent
per-user profile at `%LocalAppData%\Indian Ticketing\WebView2`. Browser cookies
and normal authenticated sessions therefore survive restarts when the site permits them.

## Configuration

- Enter IRCTC credentials in the Booking Manager at run time. They are not supplied by source defaults.
- Proxy configuration is optional: `host:port`, `host:port:user:pass`, or `user:pass@host:port`.
- A configured proxy is the selected infrastructure path for the whole browser session. The application does not switch or rotate proxies after a block, CAPTCHA, or rate limit.

## Failure policy

The browser stops the affected workflow when it detects access denial, HTTP 403/429, a CAPTCHA/challenge, authentication failure, session expiry, or a navigation timeout. It does not solve, refresh, or bypass a CAPTCHA or challenge.

For each browser failure, a redacted JSON record, screenshot, and rendered HTML are written under `%AppData%\IndianTicketing\automation_diagnostics`. URLs are stored without query strings and proxy credentials are never logged.

When the site's edge protection (e.g. Akamai) rejects a request, the JSON
record additionally captures the anti-bot signals that explain the block —
without changing identity or trying to defeat it:

- Akamai response headers (`X-Akamai-*`, `Server`, `Via`) and the HTTP status
  of the blocked response, captured from `WebResourceResponseReceived`.
- Akamai/anti-bot cookies present in the `WebView2` cookie jar (`_abck`,
  `ak_bmsc`, `bm_*`, …), including their domain, expiry and `HttpOnly`/`Secure`
  flags. Cookie token values are stored only as a short redacted preview, never
  the full value.

## Run and validation

```powershell
dotnet build indian-ticketing.sln
dotnet run --project indian-ticketing\indian-ticketing.csproj
```

There is no automated test project. Live-site validation requires site-owner authorization and adherence to its permitted access/rate rules.
