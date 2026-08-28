# Booking automation (`IrctcWebViewSession`)

This is the engine actually used by the app (see [architecture.md](architecture.md) for how it compares to the unused Selenium path). Everything below refers to [IrctcWebViewSession.cs](../indian-ticketing/IrctcWebViewSession.cs).

## Entry point and the 10 steps

`RunAsync(booking, username, password)` lazily creates the `WebView2` environment on first use (applying the proxy launch argument and loading the proxy-auth extension if configured — see [data-and-config.md](data-and-config.md)), then runs the steps in order. IRCTC's own flow forces a **second login after "Book Now"**, which is why login appears twice (Step 1's opportunistic auto-fill in `BookingManagerForm`, and Step 5 here):

| Step | Method | What happens |
|---|---|---|
| 1 | `Step1_SearchAsync` | Fill From/To autocomplete inputs, pick the first suggestion, set the journey date, optionally set the class filter, click Search. |
| 2–4 | `Step2_3_4_SelectTrainClassDateBookAsync` | Locate the row for `booking.TrainNo`, click the class-availability cell (which reveals per-date availability), click the saved journey date, wait for "Book Now" to become enabled, click it, confirm the "Do you want to continue" dialog. |
| 5 | `Step5_ReLoginAsync` | IRCTC re-prompts for login after Book Now; detect and fill it via `LoginAsync`. |
| 6 | `Step6_PassengersAsync` | For each `Passenger`: add a passenger row (if not the first), fill name/age, select gender — each field is set-then-verified (see "Step-gate pattern" below). Then click Continue Booking. |
| 6b | `Step6b_SelectPaymentMethodAsync` | On the payment-method page, select the BHIM/UPI radio option and click Continue **exactly once** (see "Single-click discipline"). |
| 7 | `Step7_ResolveCaptchaAsync` | Detects a CAPTCHA/challenge, captures diagnostics, and stops the workflow for the site's normal manual process. |
| 8 | `Step8_ContinueToReviewAsync` | Click Continue on the review/fare-summary page, then Continue again on the resulting Payment Methods page, waiting for each transition rather than re-clicking. |
| 9 | `Step9_PayAndBookAsync` | Click "Pay & Book". Detects IRCTC's "Sorry!! please Try Again" rejection page and throws if it appears (no QR will ever come after that). |
| 10 | `Step10_CaptureQrAsync` | Poll (up to 120 × 1.5s ≈ 3 minutes) for a UPI QR to render, extract it as a bitmap, and raise `OnQrReady`. |

`OnStatus` fires a human-readable message at nearly every sub-step; `BookingManagerForm` marshals these onto `BookingCard.SetStatus` via `this.Invoke`.

## Core primitives

All page interaction goes through a small set of helpers at the bottom of the file:

- **`InjectAsync()`** — (re)injects `HelperJs`, a small `window.__h` object exposing `fill`, `exists`, `pageHas`, `captchaImg`/`captchaVisible`, and `rect` (resolve a JS expression to an element, scroll it into view, return its screen-space click point). It's re-injected after nearly every navigation/DOM change because SPA navigations can lose it.
- **`Exec(js)` / `ExecBool(js)`** — thin wrappers over `CoreWebView2.ExecuteScriptAsync`, with `ExecBool` interpreting `"true"`/`"1"` as `true`.
- **`WaitForAsync(js, timeoutMs)`** — polls a JS boolean expression every 500ms until it's true or the timeout elapses. This is the primary way the code waits for SPA state changes instead of fixed `Task.Delay`s.
- **`ClickAsync(jsExpr)`** — the "real mouse" click path: resolves `jsExpr` to an element via `__h.rect`, then dispatches `Input.dispatchMouseEvent` (moved → pressed → released) through the DevTools Protocol at that screen point. Used for most clicks because Angular's `(click)` handlers respond more reliably to real input events than synthetic DOM events.
- **`ClickDomAsync(jsExpr)`** — calls `.click()` directly on the resolved element (or its closest `button`/`a` ancestor). Used specifically for Step 8b/9's Continue and "Pay & Book" buttons, which can sit in a `<p-sidebar style="height:0">` wrapper whose real bounding box is zero — geometry-based `ClickAsync` misses it, but a DOM `.click()` still fires Angular's handler. See the `VisibleBtnTest`/`__vis` JS constant for how visibility is determined in that case (uses `getClientRects().length` rather than the bounding rect, since PrimeNG's mobile bottom action bar has zero-height ancestors but is still visually rendered).
- **`ClickText(tags, txt)`** — convenience wrapper: find the first element among `tags` whose `innerText` contains `txt`, then `ClickAsync` it.

## Step-gate pattern (`EnsureAsync`)

```
EnsureAsync(what, action, verifyJs, maxAttempts = 6, settleMs = 700, promptOnFail = true)
```

This is the workhorse for anything that must be *verified*, not just attempted — e.g. "did the passenger name field actually end up holding the value we set" or "is the gender dropdown now showing Female." It loops: check `verifyJs` → if false, run `action()`, wait `settleMs`, re-check. After `maxAttempts` failed rounds it either pauses for a manual user "OK (Continue)" (`promptOnFail: true`, the default) or gives up silently and lets the caller continue (`promptOnFail: false`).

This matters because IRCTC's Angular forms sometimes don't register a value set via plain `element.value = x` — the code uses the native `HTMLInputElement` value setter plus dispatched `input`/`change`/`blur` events to make Angular's `FormControl` see the change, and `EnsureAsync` is the safety net if that still doesn't stick.

## Single-click discipline

Several comments call out a specific IRCTC behavior: **clicking "Continue" or "Pay & Book" twice makes IRCTC reject the entire transaction** with "Sorry!! Please Try again" (reason: "double clicked on any options/buttons"). Consequently, Steps 6b, 8, and 9 all follow the same shape:

1. Click **once** (`ClickDomAsync`/`ClickAsync`/`ClickContinueAsync`, never both as separate attempts on the same button).
2. `WaitForAsync` the *next* page's ready-condition (e.g. `onPaymentMethodsTextJs`, `leftPaymentMethodJs`) rather than re-clicking on failure.

If you're extending this flow, preserve that pattern — retrying a click here is not a safe default the way it is elsewhere in the codebase.

## CAPTCHA and challenge handling

When a CAPTCHA/challenge is detected, the active workflow captures redacted HTML,
a screenshot, and a JSON diagnostic record, then stops. The application never
reads, refreshes, enters, or retries a CAPTCHA. Complete the site's visible,
normal challenge flow manually before beginning a new workflow.

## UPI QR capture (Step 10)

Three fallback strategies, tried in order, each polled once per iteration:

1. Read the QR `<img>`/`<canvas>` element's `src`/`toDataURL()` directly and decode it as a bitmap (`CaptureQrBitmapAsync`).
2. If the element exists but isn't readable that way, screenshot the whole page and crop to the element's bounding rect (`CropQrFromScreenshotAsync` → `CropByRectJsonAsync`), scaling by `devicePixelRatio`.
3. Last resort: find the largest near-square `<img>/<canvas>/<svg>` on a page that otherwise looks like a payment gateway, and crop that (`CropLargestSquareImageAsync`) — for gateway variants where none of the QR-specific selectors match.

Before any of that, some gateways show a "Click here to pay through QR" placeholder that must be clicked once to render the real QR (`clickedReveal` flag). The loop also bails immediately if `BookingFailedJs` (IRCTC's "Sorry, please Try Again" / login bounce) is detected, since no QR will ever appear after that.

## Selector fragility

Nearly every selector in this file is IRCTC-specific (PrimeNG class names like `.ui-dropdown-item`/`.p-dropdown-item`, `formcontrolname` attributes, literal button text like `"BOOK NOW"`/`"CONTINUE"`/`"Pay & Book"`). IRCTC changes its frontend periodically; when a step silently "does nothing" the first thing to check is whether the corresponding selector in this file still matches the live DOM. The dual `.ui-*`/`.p-*` selectors throughout are already a sign of one such PrimeNG version migration having been patched over.
