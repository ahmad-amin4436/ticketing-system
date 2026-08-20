# Build, run, and package

## Prerequisites

- Windows 10 (build 19041+) or Windows 11 — the project targets `net8.0-windows10.0.19041.0` and uses WinRT OCR APIs (`Windows.Media.Ocr`) that only exist on Windows.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- WebView2 Runtime — normally pre-installed on Windows 10/11; the [Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) installer if it's missing.
- Google Chrome installed (only needed if you're touching/running the unused `IrctcBookingSession` Selenium path — `Selenium.WebDriver` drives the system Chrome, it doesn't bundle one).

## Build & run

From the solution root ([indian-ticketing.sln](../indian-ticketing.sln)):

```powershell
dotnet restore
dotnet build
dotnet run --project indian-ticketing
```

Or open `indian-ticketing.sln` in Visual Studio and F5.

**Trial gate:** [Program.cs](../indian-ticketing/Program.cs) refuses to start once the local system clock is on/after the hardcoded `TrialExpiry` date. If the app appears to do nothing / immediately shows "Trial Expired," check that date first (see [known-issues.md](known-issues.md) for the exact value and a text/date mismatch to be aware of).

## Publish (self-contained, matches the installer script)

```powershell
dotnet publish indian-ticketing -c Release -r win-x64 --self-contained true
```

Output lands in `indian-ticketing/bin/Release/net8.0-windows10.0.19041.0/publish/`.

## Installer

[installer/indian-ticketing.iss](../indian-ticketing/installer/indian-ticketing.iss) is an [Inno Setup](https://jrsoftware.org/isinfo.php) script that packages the self-contained publish output above into a Windows installer (`ISCC.exe indian-ticketing.iss`, output in `installer/Output/`). It expects the publish step to have already been run — the `PublishDir` it references is the exact path produced by the command above. The installer requires admin privileges (`PrivilegesRequired=admin`) and is 64-bit only (`ArchitecturesAllowed=x64compatible`).

## First run — WebView2 profile & permissions

`BookingManagerForm` creates its own WebView2 user-data folder at `%LocalAppData%\Indian Ticketing\WebView2\` on first load (separate from any other WebView2 app's profile) so login/session cookies persist across app restarts. If that folder can't be created/written to (e.g. locked-down environment), the form shows a "WebView2 Initialization Failed" message box naming the path — that's the first thing to check when the embedded browser pane stays blank.

## Testing the automation

There's no automated test suite in this repo — the booking flow is validated by actually running it against the live irctc.co.in site through a real (or scheduled) booking window, since IRCTC's session/date/quota rules and anti-automation behavior can't be meaningfully mocked. Keep that in mind before assuming a red/green CI signal exists for changes to `IrctcWebViewSession`.
