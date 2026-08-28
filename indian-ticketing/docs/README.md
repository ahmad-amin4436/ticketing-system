# Indian Ticketing — Developer Documentation

Indian Ticketing is a Windows Forms (.NET 8) desktop app that:

1. Searches trains on a route/date by scraping erail.in's public JSON-ish feed.
2. Lets the user save one or more trains + a passenger list as a "booking" to a local JSON store.
3. Drives an embedded WebView2 browser through the booking flow using the site's visible, normal browser experience. If a CAPTCHA or challenge appears, it stops and preserves diagnostics rather than attempting to solve it.

This folder documents the codebase for engineers picking up the project. Start with [architecture.md](architecture.md) for the big picture, then drill into the area you're touching.

## Contents

- [architecture.md](architecture.md) — components, responsibilities, data flow, threading model.
- [booking-automation.md](booking-automation.md) — how the WebView2 IRCTC automation engine works: click primitives, step-gate pattern, challenge stop policy, and QR capture.
- [authorized-automation.md](authorized-automation.md) — browser/session configuration, diagnostics, and the no-challenge-bypass policy.
- [data-and-config.md](data-and-config.md) — persisted data models (`SavedBooking`, `ProxyConfig`), the erail.in feed format, station list.
- [setup.md](setup.md) — prerequisites, build/run/publish, installer.
- [known-issues.md](known-issues.md) — things a new contributor should know before changing code: hardcoded credentials, dead code, fragile scraping/automation assumptions.

## Project layout

```
indian-ticketing/                      solution root
├── indian-ticketing.sln
└── indian-ticketing/                  the single WinForms project
    ├── Program.cs                     entry point + trial-expiry gate
    ├── Form1.cs / .resx               main window: train search grid
    ├── Form1.Designer.cs              designer-generated layout for Form1
    ├── BookingManagerForm.cs          second window: saved bookings + embedded browser
    ├── PassengersDialog.cs / .Designer.cs   modal dialog to enter passenger details
    ├── QrPopupForm.cs                 always-on-top window showing the captured UPI QR
    ├── TrainScraper.cs                erail.in HTTP client + feed parser (train search)
    ├── IrctcWebViewSession.cs         the WebView2-driven IRCTC booking automation (used)
    ├── IrctcBookingSession.cs         a Selenium/ChromeDriver alternative (NOT used — see known-issues.md)
    ├── ProxyConfig.cs                 HTTP proxy parsing/persistence + Chrome proxy-auth extension generator
    ├── BookingData.cs                 SavedBooking / Passenger models + JSON persistence
    ├── StationData.cs                 static station name/code list + search
    ├── Properties/                    AssemblyInfo, Settings
    └── installer/                     Inno Setup script for the trial installer
```

## Tech stack

- **.NET 8**, WinForms, target `net8.0-windows10.0.19041.0` (see [indian-ticketing.csproj](../indian-ticketing/indian-ticketing.csproj)).
- **Microsoft.Web.WebView2** — embeds an Edge/Chromium browser inside `BookingManagerForm` for the live IRCTC automation.
- **Selenium.WebDriver** — only used by the unused `IrctcBookingSession.cs` (see [known-issues.md](known-issues.md)).
- **HtmlAgilityPack** — listed as a dependency but not currently referenced by any `.cs` file in the project (train search uses `HttpClient` + manual string splitting instead, see [data-and-config.md](data-and-config.md)).
