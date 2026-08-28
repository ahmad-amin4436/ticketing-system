# Known issues and things to be careful about

Things a new contributor should know before touching this code.

## Historical credential exposure

Runtime defaults for IRCTC credentials and proxy settings have been removed. If prior revisions were committed or distributed with credentials, rotate them and remove them from repository history through the appropriate controlled process.

## Proxy credentials stored in plaintext

`ProxyConfig` ([ProxyConfig.cs](../indian-ticketing/ProxyConfig.cs)) persists `Username`/`Password` unencrypted at `%AppData%\IndianTicketing\proxy_config.json`. Same category of issue as above, lower severity since it's local-only and not committed to source — but worth knowing if the proxy account matters.

## Trial-expiry text/date mismatch

[Program.cs](../indian-ticketing/Program.cs) sets `TrialExpiry = new DateTime(2026, 9, 4, ...)` and computes `daysLeft` against that date, but the dialog text is hardcoded to say *"It will stop working after 17 June 2026"* — a different, earlier date than the one actually enforced. The Inno Setup installer's welcome message ([installer/indian-ticketing.iss](../indian-ticketing/installer/indian-ticketing.iss)) repeats the same "17 June 2026" string. If the intent is 2026-09-04, update both message strings; if the intent is 2026-06-17, update `TrialExpiry` instead — right now the enforced date and the displayed date disagree.

## `IrctcBookingSession.cs` is dead code

[IrctcBookingSession.cs](../indian-ticketing/IrctcBookingSession.cs) implements the same booking flow via Selenium/ChromeDriver, but nothing in the project constructs an `IrctcBookingSession` — `BookingManagerForm` only ever uses `IrctcWebViewSession`. It appears to be an earlier prototype that was superseded by the WebView2 approach but never removed. Consequences:

- The `Selenium.WebDriver` package reference exists solely to support this unused file.
- It's easy to accidentally "fix a bug" in the wrong file — if IRCTC booking misbehaves, confirm you're editing `IrctcWebViewSession.cs`, not this one.
- If it's confirmed nobody needs the Selenium path, deleting the file and the `Selenium.WebDriver` package reference would remove a real maintenance trap.

## `HtmlAgilityPack` is an unused dependency

Referenced in [indian-ticketing.csproj](../indian-ticketing/indian-ticketing.csproj) but no `.cs` file uses `HtmlAgilityPack`/`HtmlDocument`/`HtmlNode`. `TrainScraper` parses erail.in's feed with plain string splitting, not HTML parsing. Safe to remove unless something not yet written depends on it.

## The booking automation is tightly coupled to IRCTC's current frontend

As detailed in [booking-automation.md](booking-automation.md), essentially every selector in `IrctcWebViewSession.cs` targets IRCTC's current Angular/PrimeNG markup (specific `formcontrolname` values, PrimeNG CSS class names, exact button text like `"BOOK NOW"`). IRCTC changing its frontend is the single most likely cause of the automation silently getting stuck at a step — check the live DOM against the relevant selector before assuming there's a logic bug.

## Single-click discipline is load-bearing, not stylistic

Steps 6b/8/9 in `IrctcWebViewSession` deliberately click Continue/Pay & Book **exactly once** and then poll for the next page, because IRCTC's backend rejects the whole transaction on a detected double-click. If you're adding retry logic to any click in that part of the flow, make sure it can't result in two activations of the same button — see [booking-automation.md](booking-automation.md#single-click-discipline) for the exact pattern already in place.

## CAPTCHA/challenge workflows require manual handling

The active WebView2 workflow intentionally stops when a CAPTCHA or browser
challenge appears. It saves diagnostics under `%AppData%\IndianTicketing\automation_diagnostics`; complete the site's normal visible process before a new workflow is started.

## No automated tests

There is no test project in the solution. Changes to `IrctcWebViewSession` in particular can only really be validated by running the app against the live IRCTC site (see [setup.md](setup.md#testing-the-automation)).

## Legal/ToS note

This app automates interaction with a live, real-money government ticketing portal (irctc.co.in), including working around IRCTC's login/captcha/anti-double-click behavior. Automated purchasing tools for IRCTC are commonly restricted under IRCTC's terms of service. This is documentation of the existing code's behavior for engineering purposes, not an endorsement of a particular usage — anyone deploying or distributing this app should independently confirm it's being used in a way that's consistent with IRCTC's terms and applicable law.
