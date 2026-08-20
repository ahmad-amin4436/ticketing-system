# Known issues and things to be careful about

Things a new contributor should know before touching this code.

## Hardcoded IRCTC credentials in source

[BookingManagerForm.cs](../indian-ticketing/BookingManagerForm.cs) pre-fills the username/password textboxes with a literal account and password in the constructor:

```csharp
_txtUser.Text = "SEJAL108";
_txtPass.Text = "Radharani@1719";
```

This means a real IRCTC account's password is committed to source control in plaintext and ships inside the built binary. If this repository (or the compiled app) is ever shared beyond its current owner, that credential is exposed. Recommended fix: remove the hardcoded default, leave the fields blank (or read from `ProxyConfig`-style local JSON/user secrets instead), and rotate the password on the IRCTC account since it's already in git history.

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

## CAPTCHA OCR is best-effort, not guaranteed

`AutoSolveCaptchaAsync` reads captchas with on-device Windows OCR and has no fallback to a human once its 8 attempts are exhausted — it proceeds with its last (possibly wrong) guess rather than pausing for manual entry. A wrong captcha guess here surfaces later as an unexplained "stuck" step rather than an explicit "captcha failed" status, so when the automation stalls right after a captcha-bearing page, check the status log for "Captcha ... rejected" messages first.

## No automated tests

There is no test project in the solution. Changes to `IrctcWebViewSession` in particular can only really be validated by running the app against the live IRCTC site (see [setup.md](setup.md#testing-the-automation)).

## Legal/ToS note

This app automates interaction with a live, real-money government ticketing portal (irctc.co.in), including working around IRCTC's login/captcha/anti-double-click behavior. Automated purchasing tools for IRCTC are commonly restricted under IRCTC's terms of service. This is documentation of the existing code's behavior for engineering purposes, not an endorsement of a particular usage — anyone deploying or distributing this app should independently confirm it's being used in a way that's consistent with IRCTC's terms and applicable law.
