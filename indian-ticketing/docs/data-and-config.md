# Data models, persistence, and the train-search feed

## SavedBooking / Passenger — [BookingData.cs](../indian-ticketing/BookingData.cs)

```csharp
class SavedBooking {
    string Id;            // random 8-char hex, generated on construction
    DateTime SavedAt;
    string TrainNo, TrainName;
    string FromCode, FromName, ToCode, ToName;
    string DepTime, ArrTime, Duration;
    string JourneyDate;   // "dd-MMM-yyyy", e.g. "05-Sep-2026"
    string TravelClass;   // "SL", "3A", "2A", "1A", "CC", "2S", "3E"
    string Quota;         // "GN", "TQ", "PT"
    List<Passenger> Passengers;
}

class Passenger {
    string Name;
    int    Age;
    string Gender;  // "M" / "F" / "T"
    string Berth;   // "NP" / "LB" / "MB" / "UB" / "SL" / "SU"
}
```

Persisted as a single JSON array at `%AppData%\IndianTicketing\saved_bookings.json` via `SavedBooking.LoadAll()` / `SavedBooking.SaveAll(list)`. There's no per-record file and no migration/versioning — a malformed file is silently treated as empty (`catch { return new(); }` in `LoadAll`), so a corrupt file doesn't crash the app but does silently drop all saved bookings.

`Form1.btnSaveTrain_Click` is the only writer for new records; `BookingManagerForm`'s card "Delete" button and `Refresh` button are the other read/write touchpoints.

## ProxyConfig — [ProxyConfig.cs](../indian-ticketing/ProxyConfig.cs)

Persisted at `%AppData%\IndianTicketing\proxy_config.json`. Fields: `Enabled`, `Host`, `Port`, `Username`, `Password` — **stored in plaintext JSON**, no encryption (see [known-issues.md](known-issues.md)).

`ProxyConfig.Parse(text, out error)` accepts three input formats typed into `BookingManagerForm`'s proxy textbox:

- `host:port:user:pass`
- `user:pass@host:port`
- `host:port` (no auth)

It's consumed three different ways depending on the HTTP client:

| Consumer | How the proxy is applied |
|---|---|
| `TrainScraper` (plain `HttpClient`) | `ApplyToHandler(HttpClientHandler)` sets `.Proxy` = `ToWebProxy()`, which supports `NetworkCredential` directly. |
| `IrctcBookingSession` (unused Selenium path) | `ApplyToChromeOptions(ChromeOptions)` adds `--proxy-server=...`. |
| `BookingManagerForm` / `IrctcWebViewSession` (WebView2) | `GetProxyServerArg()` is passed as `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`. |

Chromium's `--proxy-server` flag carries the host/port but **not** credentials, so authenticated proxies additionally need `EnsureAuthExtension(proxy)`. That method generates a temporary Manifest V2 Chrome extension on disk (under `%TEMP%\IndianTicketing\proxy_auth_ext`) whose `background.js` registers a `chrome.webRequest.onAuthRequired` listener that answers 407 challenges with the configured username/password. It's loaded via `Profile.AddBrowserExtensionAsync` (WebView2) or `--load-extension`/`--disable-extensions-except` (Selenium). This is the standard workaround for the fact that neither Chrome's command line nor WebView2's API accepts proxy credentials directly.

## StationData — [StationData.cs](../indian-ticketing/StationData.cs)

A static, hand-maintained array of ~hundreds of `(Name, Code)` tuples covering Indian railway stations, organized by state in comments. `Search(query)` (further down in the file, not shown in the excerpt above) powers the autocomplete dropdowns in `Form1`. There's no external API call — adding a new station means editing this array directly and shipping a new build.

## TrainScraper — [TrainScraper.cs](../indian-ticketing/TrainScraper.cs)

Does **not** use Selenium/HtmlAgilityPack/browser automation for search — it's a direct `HttpClient` call to erail.in's own backend feed:

```
GET https://erail.in/rail/getTrains.aspx?Station_From={code}&Station_To={code}&DataSource=0&Language=0&Cache=true
```

Response format: one string, trains separated by `^`, fields within a train separated by `~`. Field indices were reverse-engineered from the live feed (see the constants and comments in `ParseTrains`):

- `f[0]` train number, `f[1]` train name, `f[7]`/`f[9]` from/to station codes, `f[10]`/`f[11]` dep/arr time, `f[12]` duration, `f[13]` a 7-char Mon–Sun running-days bitmap, `f[15]` a train-type label (used for row coloring), `f[21]` a 15-char class-availability bitmap.
- Class bitmap bit positions are named constants (`Bit1A = 0, Bit2A = 1, Bit3A = 2, BitCC = 3, Bit3E = 4, BitSL = 5, Bit2S = 7`) — comment notes they're "verified against Rajdhani/Garib Rath/Shatabdi/Vande Bharat," implying other train types weren't exhaustively checked.
- Rows with fewer than 22 fields, or a train number containing no digit, are skipped as malformed/header rows.

**Important limitation** (called out in the file's own comment): this feed does **not** carry live per-class seat counts (e.g. "AVAILABLE-12" / "WL 45"). `AvlXX` columns in the grid only indicate whether a train *offers* that class at all (shows the class code, or `"x"` if not offered) — real-time availability is only checked once you're inside the actual IRCTC booking flow. Don't mistake the search grid's colors for live seat data.

`DeriveDates` infers whether departure/arrival fall on the "same day" purely by comparing `HH.mm` times (arrival time earlier than departure time ⇒ next-day arrival) — it does not compute real calendar dates from the feed, since the feed doesn't provide them per search date.

## Where files live on disk

| What | Path |
|---|---|
| Saved bookings | `%AppData%\IndianTicketing\saved_bookings.json` |
| Proxy config | `%AppData%\IndianTicketing\proxy_config.json` |
| WebView2 browser profile | `%LocalAppData%\Indian Ticketing\WebView2\` |
| Generated proxy-auth Chrome extension | `%TEMP%\IndianTicketing\proxy_auth_ext\` |
