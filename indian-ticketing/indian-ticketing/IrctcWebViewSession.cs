using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace indian_ticketing;

// Thrown when IRCTC's edge WAF (Akamai) returns its static "Access Denied"
// block page instead of the real site — a signal to the caller to retry
// through a proxy, not a generic failure.
public class IrctcBlockedException : Exception
{
    public string? AkamaiReference { get; }

    public IrctcBlockedException(string? akamaiReference = null)
        : base(akamaiReference != null
            ? $"IRCTC blocked this connection (Access Denied / Akamai edge block). Reference: {akamaiReference}"
            : "IRCTC blocked this connection (Access Denied / Akamai edge block).")
    {
        AkamaiReference = akamaiReference;
    }
}

/// <summary>
/// Automates the complete IRCTC booking workflow (Steps 1-10):
///   1  Open IRCTC + apply saved search filters + Search
///   2  Identify and select the saved train
///   3  Select saved class + date → Book Now becomes active
///   4  Click Book Now → confirmation Yes
///   5  Re-login (IRCTC always asks again after Book Now)
///   6  Fill passenger details
///   7  Select BHIM/UPI payment → Continue
///   8  Review page → captcha handling → Continue
///   9  Pay & Book
///  10  Capture UPI QR → display in app card
/// </summary>
public class IrctcWebViewSession
{
    private readonly WebView2 _wv;
    private TaskCompletionSource<bool>? _userAckTcs;
    private string _lastUser = "";
    private string _lastPass = "";
    private readonly ProxyConfig? _proxy;
    // Best-effort label for diagnostics only — reflects what the caller told
    // us it set the underlying WebView2 up with, since a session created by
    // BookingManagerForm reuses a browser that was already initialized
    // (direct or proxy) before this session object existed, so this class
    // can't observe that choice directly.
    private readonly bool _usingProxy;

    public event Action<string>? OnStatus;
    public event Action<System.Drawing.Bitmap>? OnQrReady;
    // Fired when the QR that OnQrReady last showed has disappeared from the
    // live page (payment completed, or the gateway moved on) — the UI
    // closes the matching QR popup window when this fires.
    public event Action? OnQrGone;

    // ── JS helpers ────────────────────────────────────────────────────────
    internal const string HelperJs = @"
window.__h = {
    fill: function(sel, val) {
        var el = document.querySelector(sel);
        if (!el) return false;
        el.focus();
        try {
            var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
            s.call(el, val);
        } catch(e) { el.value = val; }
        el.dispatchEvent(new InputEvent('input',{bubbles:true,data:val,composed:true}));
        el.dispatchEvent(new Event('change',{bubbles:true}));
        el.dispatchEvent(new FocusEvent('blur',{bubbles:true}));
        return true;
    },
    exists:   function(sel){ return !!document.querySelector(sel); },
    pageHas:  function(t)  { return document.body.innerText.toLowerCase().includes(t.toLowerCase()); },
    captchaImg: function(){
        return document.querySelector(
          'img.captcha-img, .captcha_div img, .captcha_mainDeiv img, ' +
          'img[alt*=""Captcha""], img[src*=""captcha""], img[id*=""captcha""]');
    },
    captchaVisible: function(){
        return !!(this.captchaImg()
            || document.querySelector('input#captcha, input[formcontrolname=""captcha""], input[placeholder*=""Captcha""]'));
    },
    imgSrc:   function(sel){ var e=document.querySelector(sel); return e?e.src:''; },
    canvasUrl:function(sel){ var e=document.querySelector(sel); return e?e.toDataURL():''; },
    rect: function(expr) {
        try {
            var el = eval(expr);
            if (!el) return null;
            el.scrollIntoView({block:'center', inline:'nearest'});
            var r = el.getBoundingClientRect();
            if (!r.width || !r.height) return null;
            return {x:Math.round(r.left+r.width/2), y:Math.round(r.top+r.height/2)};
        } catch(e){ return null; }
    }
};
true;";

    private readonly string _profileFolderName;

    // profileFolderName: WebView2 refuses to run two browser processes
    // against the same user-data folder at once (fails with HRESULT
    // 0x8007139F, "the group or resource is not in the correct state") —
    // confirmed this is exactly what happens when Form1's search WebView2
    // and BookingManagerForm's WebView2 both point at the same shared
    // profile while both windows are open, which is the normal way this app
    // gets used (Booking Manager opens from Form1 and both stay up). Callers
    // that need an independent profile (Form1's search) should pass a
    // distinct name; booking keeps the original shared one for backward
    // compatibility with its existing login/session state.
    public IrctcWebViewSession(
        WebView2 wv, ProxyConfig? proxy = null, string profileFolderName = "WebView2", bool usingProxy = false)
    {
        _wv = wv; _proxy = proxy; _profileFolderName = profileFolderName; _usingProxy = usingProxy;
    }

    private string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Indian Ticketing",
            _profileFolderName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════
    private async Task EnsureCoreWebView2Async(bool useProxy = true)
    {
        if (_wv.CoreWebView2 != null) return;

        var dataFolder = GetWebView2UserDataFolder();
        try
        {
            await InitCoreWebView2Async(dataFolder, useProxy);
        }
        catch (Exception ex) when (IsProfileLockError(ex))
        {
            // HRESULT 0x8007139F: another process still holds this profile
            // folder's lock (a WebView2 child process left running after an
            // abrupt stop — common while iterating via a debugger — or any
            // other transient file lock). Rather than fail hard, fall back
            // to a fresh, uniquely named profile for this run instead of
            // diagnosing exactly who's holding the old one.
            Report("WebView2 profile was locked by another process — starting a fresh profile...");
            dataFolder = $"{dataFolder}-{DateTime.Now:yyyyMMddHHmmss}";
            await InitCoreWebView2Async(dataFolder, useProxy);
        }
    }

    private static bool IsProfileLockError(Exception ex)
        => ex is System.Runtime.InteropServices.COMException com && (uint)com.HResult == 0x8007139F;

    private async Task InitCoreWebView2Async(string dataFolder, bool useProxy)
    {
        Directory.CreateDirectory(dataFolder);

        var envOptions = new CoreWebView2EnvironmentOptions();
        var proxyArg = useProxy ? _proxy?.GetProxyServerArg() : null;
        if (!string.IsNullOrEmpty(proxyArg))
        {
            envOptions.AdditionalBrowserArguments = proxyArg;
            Report($"Proxy configured: {_proxy?.Host}:{_proxy?.Port} (auth: {_proxy?.HasCredentials})");
        }

        var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, envOptions);
        // No CreationProperties assignment: it's only used by the control's
        // own implicit init path (and only if set before it gets a window
        // handle) — passing this explicit environment to
        // EnsureCoreWebView2Async(env) below already carries dataFolder, so
        // it's both unnecessary and (once _wv is already parented/handled)
        // throws "CreationProperties cannot be modified after the
        // initialization of CoreWebView2 has begun."
        await _wv.EnsureCoreWebView2Async(env);

        // Auto-answer the native proxy-auth dialog ("Sign in to access this
        // site") with the configured proxy credentials, so it never blocks
        // the UI waiting for a manual Username/Password/Sign in.
        if (useProxy && _proxy != null && _proxy.HasCredentials)
        {
            _wv.CoreWebView2.BasicAuthenticationRequested += (s, e) =>
            {
                e.Response.UserName = _proxy.Username;
                e.Response.Password = _proxy.Password;
            };
        }

        // Load proxy auth extension AFTER profile is available
        if (useProxy && _proxy != null)
        {
            var extPath = ProxyConfig.EnsureAuthExtension(_proxy);
            if (extPath != null && _wv.CoreWebView2?.Profile != null)
            {
                try
                {
                    await _wv.CoreWebView2.Profile.AddBrowserExtensionAsync(extPath);
                    Report("Proxy auth extension loaded.");
                }
                catch
                {
                    Report("Proxy auth extension already loaded or failed (non-critical).");
                }
            }
        }
    }

    public async Task RunAsync(SavedBooking booking, string username, string password)
    {
        try
        {
            await EnsureCoreWebView2Async();
            _lastUser = username;
            _lastPass = password;

            // ── Step 1 — Open IRCTC and search (NO login yet) ─────────────
            Report("Step 1 — Opening IRCTC...");
            await NavAsync("https://www.irctc.co.in/nget/train-search");
            await D(1500); await InjectAsync();   // NavAsync already awaited load

            // This booking flow previously had NO Access-Denied check at all
            // (unlike Form1's search, which does) — a block here meant every
            // later step just failed with vague "not found"/timeout messages
            // instead of a clear reason, and nothing was ever logged to
            // AccessDeniedDiagnostics for this path. Check and stop cleanly.
            bool blocked = await ExecBool("__h.pageHas('Access Denied') && __h.pageHas('have permission')");
            if (blocked)
            {
                var reference = await AccessDeniedDiagnostics.CaptureAsync(
                    _wv.CoreWebView2, _usingProxy, _proxy);
                Report(reference != null
                    ? $"IRCTC blocked this connection (Access Denied). Reference: {reference}. " +
                      "Close and reopen the Booking Manager to retry (it tries direct first, then the " +
                      "configured proxy if one is set)."
                    : "IRCTC blocked this connection (Access Denied). Close and reopen the Booking Manager to retry.");
                return;
            }

            await DismissLanguageAlertAsync();    // "Alert" Hindi/English popup, if shown

            // ── Step 0 — Log in FIRST, before touching the search form ─────
            await Step0_LoginFirstAsync(username, password);
            // (Step1_SearchAsync checks for an unsolicited LOGIN popup itself,
            // right at its own start, in case one shows up mid-form-fill even
            // after this — checking again here would just be the same check
            // twice back-to-back with no page activity in between.)

            await Step1_SearchAsync(booking);           // search with saved filters

            // ── Steps 2-3-4 — Select train → class → date → Book Now → Yes ─
            await Step2_3_4_SelectTrainClassDateBookAsync(booking);

            // ── Step 5 — Login form appears HERE (after Book Now) ─────────
            await Step5_ReLoginAsync();                 // fill credentials

            // ── Step 6 — Passenger details → Continue Booking ─────────────
            await Step6_PassengersAsync(booking);

            // ── Step 6b — Payment-method page: pick BHIM/UPI → Continue ───
            await Step6b_SelectPaymentMethodAsync();

            // ── Step 7 — Auto-resolve captcha on next page ───────────────
            await Step7_ResolveCaptchaAsync();

            // ── Step 8 — Click Continue → "Pay & Book" page ──────────────
            await Step8_ContinueToReviewAsync();

            // ── Step 9 — Click Pay & Book → gateway redirect ─────────────
            await Step9_PayAndBookAsync();

            // ── Step 10 — Extract UPI QR and show in popup ───────────────
            await Step10_CaptureQrAsync();
        }
        catch (Exception ex) { Report($"Error: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TRAIN SEARCH (listing only — no login/booking). Runs the same search
    //  form-fill Step 1 already uses (autocomplete stations, date, dropdowns),
    //  then reads the results directly from IRCTC's own page instead of a
    //  third-party feed (erail.in), so the list — and which classes each
    //  train actually offers — always matches what IRCTC itself shows.
    //
    //  Known limitation: IRCTC's results list doesn't show live seat counts
    //  per class up front (each class box just says "Refresh" until you open
    //  it) — same limitation the erail.in feed had. This returns which
    //  classes each train offers, not live WL/AVAILABLE numbers; those are
    //  fetched during the actual booking flow (Steps 2-3). Day-of-run
    //  (Mon..Sun) isn't populated — IRCTC's markup for that wasn't available
    //  to confirm, so those columns are left blank rather than guessed.
    // ═══════════════════════════════════════════════════════════════════════
    // useProxy: false tries IRCTC direct first (the common case — no proxy
    // dependency, less latency). Pass true to force the configured proxy,
    // typically as a retry after catching IrctcBlockedException.
    public async Task<List<TrainInfo>> SearchTrainsAsync(
        string fromCode, string toCode, string date,
        IProgress<string>? progress = null, bool useProxy = false)
    {
        if (progress != null) OnStatus += progress.Report;

        await EnsureCoreWebView2Async(useProxy);

        progress?.Report("Opening IRCTC...");
        await NavAsync("https://www.irctc.co.in/nget/train-search");
        await InjectAsync();
        // Wait for the search form to actually be interactive rather than a
        // blind fixed delay — this page's Angular hydration time varies
        // enough that a short fixed wait sometimes fires before the From
        // station input even exists yet (confirmed: querying it too early
        // returns nothing to interact with).
        await WaitForAsync("!!document.querySelector('p-autocomplete input')", 8000, pollMs: 200);
        await InjectAsync();

        // IRCTC's edge WAF (Akamai) blocks some IPs outright with a static
        // "Access Denied ... you don't have permission ..." page instead of
        // the real site — surface that distinctly so the caller can retry
        // through a proxy instead of Step1_SearchAsync failing to find form
        // fields on what is actually a block page, not IRCTC.
        bool blocked = await ExecBool("__h.pageHas('Access Denied') && __h.pageHas('have permission')");
        if (blocked)
        {
            var reference = await AccessDeniedDiagnostics.CaptureAsync(_wv.CoreWebView2, useProxy, _proxy);
            throw new IrctcBlockedException(reference);
        }

        await DismissLanguageAlertAsync();
        // (Step1_SearchAsync checks for an unsolicited LOGIN popup itself,
        // right at its own start — see comment in LoginAsync above.)

        progress?.Report($"Searching {fromCode} → {toCode} on {date}...");
        // "All Classes" / "GN" match Step1_SearchAsync's own skip conditions,
        // so it fills from/to/date and searches without narrowing by class or
        // quota — the same breadth erail.in's per-route feed gave us.
        await Step1_SearchAsync(new SavedBooking
        {
            FromCode = fromCode, ToCode = toCode, JourneyDate = date,
            TravelClass = "All Classes", Quota = "GN",
        });

        progress?.Report("Waiting for results...");
        bool resultsReady = await WaitForAsync("!!document.querySelector('app-train-avl-enq')", 15000, pollMs: 300);
        await D(700); await InjectAsync();   // let the rest of the list finish rendering

        progress?.Report("Reading results from IRCTC...");
        var raw = await Exec(ExtractTrainsJs);
        var json = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");

        var trains = ParseIrctcTrains(json);
        foreach (var t in trains) { t.From = fromCode; t.To = toCode; }

        if (trains.Count == 0)
        {
            await ReportEmptySearchAsync(resultsReady);
            progress?.Report("0 trains — see train_search_diag.json for why.");
        }
        else
        {
            progress?.Report($"{trains.Count} trains.");
        }
        return trains;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STATION SUGGESTIONS (Form1's From/To autocomplete) — queries IRCTC's
    //  own live autocomplete widget instead of a hardcoded local station
    //  list, so results always match what IRCTC itself would offer,
    //  including stations added since this app was last updated. Confirmed
    //  live (via CDP network capture) that IRCTC does NOT hit a network
    //  endpoint per keystroke — the whole station list is bundled client-
    //  side and filtered in-page — so this drives the real page's own input
    //  and reads back its own suggestion panel rather than reverse-
    //  engineering a private endpoint.
    // ═══════════════════════════════════════════════════════════════════════
    // Returns (as a bare JS expression, no wrapping quotes) the code of the
    // first real (non-divider, non-"recent journey") row currently in the
    // origin field's suggestion panel, or '' if none. Used both to capture a
    // baseline before typing a new query and to detect when the panel has
    // actually refreshed to match it.
    private const string FirstStationCodeJs = @"(function(){
  var panel = document.querySelector('#origin .ui-autocomplete-panel');
  if (!panel || panel.offsetParent===null) return '';
  var items = panel.querySelectorAll('li.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all');
  for (var i=0; i<items.length; i++) {
    var li = items[i];
    if (li.querySelector('.disable-selection')) continue;
    var t = (li.textContent||'').trim();
    if (!t || t.indexOf('➨') >= 0) continue;
    var m = t.split('\n')[0].match(/-\s*([A-Z0-9]{2,6})\b/);
    if (m) return m[1];
  }
  return '';
})()";

    // After a train search completes, the WebView2 is left sitting on the
    // train-list RESULTS page, not train-search — so the very next station
    // lookup would otherwise have to pay for a full re-navigation (several
    // seconds) before it could even start typing. Form1 calls this right
    // after a search finishes (fire-and-forget, off the UI thread's
    // critical path) to get the page back to train-search early, so by the
    // time the user starts typing again the fast path below is already hot.
    public async Task PrewarmSearchPageAsync()
    {
        try
        {
            await EnsureCoreWebView2Async(false);
            if (await EnsureOnSearchPageAsync())
            {
                // Popups (language alert / login) only ever appear right
                // after a fresh page load, never spontaneously on an
                // already-loaded page — so only pay for checking them here,
                // the one place a fresh load can actually happen.
                await DismissLanguageAlertAsync();
                await DismissLoginPopupAsync();
            }
        }
        catch { /* best-effort — a real lookup will retry this anyway */ }
    }

    // Returns true if a fresh navigation to train-search was needed (i.e.
    // the page wasn't already there) — callers use this to know whether
    // it's worth checking for load-time popups (language alert, login) at
    // all, since those never appear on an already-loaded page.
    private async Task<bool> EnsureOnSearchPageAsync()
    {
        bool onSearchPage = await ExecBool(
            "location.href.indexOf('train-search') >= 0 && !!document.querySelector('p-autocomplete input')");
        if (onSearchPage) return false;

        await NavAsync("https://www.irctc.co.in/nget/train-search");
        await InjectAsync();
        await WaitForAsync("!!document.querySelector('p-autocomplete input')", 8000, pollMs: 200);
        await InjectAsync();
        return true;
    }

    public async Task<List<(string Name, string Code)>> GetStationSuggestionsAsync(string query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length < 2) return new();

        try
        {
            await EnsureCoreWebView2Async(false);
            if (await EnsureOnSearchPageAsync())
            {
                await DismissLanguageAlertAsync();
                await DismissLoginPopupAsync();
            }

            // Confirmed live (via CDP): the panel does NOT clear between
            // queries — it keeps showing the PREVIOUS query's results for
            // roughly 1s (sometimes longer) before IRCTC's own internal
            // debounce actually re-filters, so reading the panel right after
            // setting the input returns stale data, not "no results". Capture
            // the current top result as a baseline and poll until it changes
            // (or, on the very first query of a session, until anything at
            // all appears) instead of trusting a fixed short delay.
            var baselineRaw = await Exec(FirstStationCodeJs);
            var baseline = baselineRaw.Trim('"');

            // Reuse the ORIGIN field for both From/To lookups — a station is
            // a station regardless of which field is asking, and this never
            // touches the destination field the user may already have set.
            await Exec($@"(function(){{
  var inp = document.querySelectorAll('p-autocomplete input')[0];
  if (!inp) return;
  inp.focus();
  var s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(inp,'{Esc(q)}');
  inp.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
}})();");

            bool settled = await WaitForAsync(
                $"(function(){{ var c={FirstStationCodeJs}; return !!c && c!=={JsStr(baseline)}; }})()",
                3000, pollMs: 150);
            if (!settled) return new();

            // Same divider/"recent journey" filtering already proven correct
            // for AutocompleteItemJs — dividers carry a ".disable-selection"
            // child, journey-combo rows contain "➨".
            var raw = await Exec(@"(function(){
  var scope = document.querySelector('#origin .ui-autocomplete-panel');
  if (!scope) return '[]';
  var items = Array.from(scope.querySelectorAll(
    'li.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all'));
  var out = [];
  items.forEach(function(li){
    if (li.querySelector('.disable-selection')) return;
    var t = (li.textContent||'').trim();
    if (!t || t.indexOf('➨') >= 0) return;
    var firstLine = t.split('\n')[0];
    var m = firstLine.match(/-\s*([A-Z0-9]{2,6})\b/);
    if (!m) return;
    var name = firstLine.slice(0, m.index).replace(/-\s*$/,'').trim();
    if (!name) return;
    out.push([name, m[1]]);
  });
  return JSON.stringify(out);
})()");

            var json = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            var pairs = JsonSerializer.Deserialize<List<List<string>>>(json);
            if (pairs == null) return new();

            // De-dupe by code — the panel can list the same station under
            // both a "Journeys" combo row's tail and its own "Stations" row.
            return pairs.Where(p => p.Count == 2)
                        .Select(p => (Name: p[0], Code: p[1]))
                        .GroupBy(p => p.Code)
                        .Select(g => g.First())
                        .Take(15)
                        .ToList();
        }
        catch { return new(); }
    }

    // Diagnostic for a search that came back with zero trains: dumps enough
    // of the live page state to tell WHY — did the results panel never even
    // render (resultsReady=false, e.g. station/date fill or Search click
    // failed), or did it render but the header-parsing pattern just not
    // match anything on this page.
    private async Task ReportEmptySearchAsync(bool resultsReady)
    {
        var raw = await Exec(@"(function(){
  var dlgTitle = document.querySelector('.ui-dialog-title');
  var searchBtn = Array.from(document.querySelectorAll('button')).find(function(b){
    return (b.innerText||'').toUpperCase().indexOf('SEARCH') >= 0;
  });
  var jpForm = document.querySelector('.jp-form form');
  var out = {
    url: location.href,
    resultsReady: null,
    visibilityState: document.visibilityState,
    documentHidden: document.hidden,
    documentHasFocus: document.hasFocus(),
    innerWidth: window.innerWidth,
    innerHeight: window.innerHeight,
    trainCardCount: document.querySelectorAll('app-train-avl-enq').length,
    preAvlCount: document.querySelectorAll('.pre-avl').length,
    fromVal: (document.querySelectorAll('p-autocomplete input')[0]||{}).value || '',
    toVal:   (document.querySelectorAll('p-autocomplete input')[1]||{}).value || '',
    dialogExists: !!dlgTitle,
    dialogVisible: !!(dlgTitle && dlgTitle.offsetParent !== null),
    dialogText: dlgTitle ? dlgTitle.innerText.trim() : '',
    searchBtnExists: !!searchBtn,
    searchBtnVisible: !!(searchBtn && searchBtn.offsetParent !== null),
    searchBtnDisabled: !!(searchBtn && searchBtn.disabled),
    formClasses: jpForm ? jpForm.className : '(no .jp-form form found)',
    bodySnippet: (document.body.innerText||'').replace(/\s+/g,' ').slice(0, 800)
  };
  return JSON.stringify(out);
})()");
        var json = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var obj = new Dictionary<string, object?>();
            foreach (var p in doc.RootElement.EnumerateObject())
                obj[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
            obj["resultsReady"] = resultsReady;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IndianTicketing");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "train_search_diag.json"),
                JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* diagnostic-only, never block the flow */ }
    }

    // Finds every train card via IRCTC's own component boundary
    // (<app-train-avl-enq>, one per train — confirmed from live markup, far
    // more reliable than scanning for header text). Per card:
    //   - name/number from ".train-heading strong" ("NAME (NNNNN)")
    //   - days of run from the "Runs On:" span's 7 child <span class="Y"|"N">
    //   - dep/arr from the two VISIBLE ".time" elements (a hidden mobile-
    //     layout duplicate of the same two also exists, hence the
    //     offsetParent filter) and duration from ".line-hr"
    //   - classes offered from each ".pre-avl" <strong> label
    private const string ExtractTrainsJs = @"(function(){
  var cards = Array.from(document.querySelectorAll('app-train-avl-enq'));
  var trains = [];

  cards.forEach(function(card){
    var headEl = card.querySelector('.train-heading strong');
    var headText = ((headEl && headEl.textContent) || '').replace(/\s+/g,' ').trim();
    var m = /^(.*?)\s*\((\d{5})\)$/.exec(headText);
    if (!m) return;

    var runsOnEl = Array.from(card.querySelectorAll('span')).find(function(s){
      return (s.textContent||'').indexOf('Runs On') >= 0;
    });
    var days = ['x','x','x','x','x','x','x'];
    if (runsOnEl) {
      var dSpans = Array.from(runsOnEl.querySelectorAll('span')).slice(0, 7);
      if (dSpans.length === 7) {
        days = dSpans.map(function(s){ return s.classList.contains('Y') ? 'Y' : 'x'; });
      }
    }

    var timeEls = Array.from(card.querySelectorAll('.time')).filter(function(e){ return e.offsetParent!==null; });
    var depTime = timeEls[0] ? (timeEls[0].textContent||'').replace(/[^\d:]/g,'') : '';
    var arrTime = timeEls[1] ? (timeEls[1].textContent||'').replace(/[^\d:]/g,'') : '';

    var durEl = Array.from(card.querySelectorAll('.line-hr')).find(function(e){ return e.offsetParent!==null; });
    var dur = durEl ? (durEl.textContent||'').trim() : '';

    var classes = Array.from(card.querySelectorAll('.pre-avl')).map(function(box){
      var lbl = box.querySelector('strong');
      return ((lbl && lbl.textContent) || '').trim();
    }).filter(function(s){ return s.length>0; });

    trains.push({
      no: m[2], name: m[1].trim(),
      dep: depTime, arr: arrTime, dur: dur,
      days: days, classes: classes
    });
  });

  return JSON.stringify(trains);
})()";

    private static List<TrainInfo> ParseIrctcTrains(string json)
    {
        var result = new List<TrainInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var classes = el.GetProperty("classes").EnumerateArray()
                    .Select(c => c.GetString() ?? "").ToList();

                var dep = el.GetProperty("dep").GetString() ?? "";
                var arr = el.GetProperty("arr").GetString() ?? "";
                var arrSame = true;
                if (TimeSpan.TryParse(dep, out var dTs) && TimeSpan.TryParse(arr, out var aTs))
                    arrSame = aTs >= dTs;

                var days = el.GetProperty("days").EnumerateArray()
                    .Select(d => d.GetString() ?? "x").ToList();
                string DayFlag(int i) => i < days.Count ? days[i] : "x";

                result.Add(new TrainInfo
                {
                    TrainNo    = el.GetProperty("no").GetString() ?? "",
                    TrainName  = el.GetProperty("name").GetString() ?? "",
                    DepTime    = dep,
                    ArrTime    = arr,
                    Duration   = el.GetProperty("dur").GetString() ?? "",
                    Mon        = DayFlag(0),
                    Tue        = DayFlag(1),
                    Wed        = DayFlag(2),
                    Thu        = DayFlag(3),
                    Fri        = DayFlag(4),
                    Sat        = DayFlag(5),
                    Sun        = DayFlag(6),
                    Avl1A      = ClassCodeOf(classes, "1A"),
                    Avl2A      = ClassCodeOf(classes, "2A"),
                    Avl3A      = ClassCodeOf(classes, "3A"),
                    AvlCC      = ClassCodeOf(classes, "CC"),
                    AvlSL      = ClassCodeOf(classes, "SL"),
                    Avl2S      = ClassCodeOf(classes, "2S"),
                    Avl3E      = ClassCodeOf(classes, "3E"),
                    ArrSameDay = arrSame,
                });
            }
        }
        catch { /* malformed JSON — return whatever was parsed before the failure */ }
        return result;
    }

    private static string ClassCodeOf(List<string> classLabels, string code)
        => classLabels.Any(l => l.Contains($"({code})", StringComparison.OrdinalIgnoreCase)) ? code : "x";

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 1 (pre) — Dismiss the "Alert" language-selection popup, if IRCTC
    //  shows it right after the site loads, by choosing English.
    // ═══════════════════════════════════════════════════════════════════════
    // "offsetParent!==null" matters here, not just querySelector — the
    // dialog's title node can stay in the DOM (contributing to
    // document.body.innerText) after being visually dismissed, so a text-
    // only check can keep reporting it "present" long after it stopped
    // blocking anything.
    private const string AlertVisibleJs = @"(function(){
  var title = document.querySelector('.ui-dialog-title');
  return !!title && title.offsetParent!==null && title.innerText.trim().toUpperCase()==='ALERT';
})()";

    // Confirmed via direct CDP testing against the live site: this popup's
    // appearance is genuinely non-deterministic — it can show immediately on
    // load, only later mid-way through filling the search form, or not at
    // all, and dismissing it once does not reliably prevent it reappearing.
    // One observed run needed to be re-dismissed many times in a row before
    // staying gone, so this retries persistently instead of a few times,
    // re-verifying after every click rather than assuming success.
    private async Task DismissLanguageAlertAsync()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // Faster polling (250ms) than WaitForAsync's default keeps the
            // common "no popup" case cheap — it was previously a guaranteed
            // 4s wait every single search even when nothing ever shows.
            bool present = await WaitForAsync(AlertVisibleJs, attempt == 0 ? 2800 : 1000, pollMs: 250);
            if (!present) return;

            Report($"Step 1 — Language popup detected, selecting English (attempt {attempt + 1})...");
            // A direct DOM .click() rather than a coordinate-based CDP click:
            // ClickAsync/ClickText report success as soon as the button is
            // found and sized, even if the physical click coordinate is
            // actually intercepted by the dialog's own backdrop — a direct
            // .click() on the element can't be swallowed that way.
            await ClickDomAsync(
                "Array.from(document.querySelectorAll('button')).find(function(e){return (e.innerText||'').trim()==='English' && e.offsetParent!==null;})");
            await D(250); await InjectAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 1 (pre) — Close an unsolicited LOGIN popup, if IRCTC shows one
    //  while just searching. Plain search never requires being logged in
    //  (only Tatkal quota does — handled separately), and a still-open login
    //  modal sits on top of the page, swallowing clicks meant for the
    //  autocomplete dropdown / Search button underneath it. Like the
    //  language alert, this is non-deterministic — it may or may not appear
    //  — so it's checked cheaply rather than waited on.
    // ═══════════════════════════════════════════════════════════════════════
    private const string LoginPopupVisibleJs = @"(function(){
  var title = document.querySelector('.ui-dialog-title, .p-dialog-title');
  return !!title && title.offsetParent!==null && title.innerText.trim().toUpperCase()==='LOGIN';
})()";

    private async Task DismissLoginPopupAsync()
    {
        // Short window: a real login modal renders essentially instantly, and
        // the two call sites that matter for speed (Step1_SearchAsync's start
        // and its search-click retry loop) both re-check this on every retry
        // anyway, so a persistently-present popup still gets caught quickly
        // without every search paying a long wait for the common "not there" case.
        bool present = await WaitForAsync(LoginPopupVisibleJs, 400, pollMs: 150);
        if (!present) return;

        Report("Step 1 — Unsolicited login popup detected, closing it...");
        await ClickDomAsync(@"Array.from(document.querySelectorAll(
  '.ui-dialog-titlebar-close, .p-dialog-header-close, .p-dialog-header-icon'
)).find(function(e){return e.offsetParent!==null;})");
        await D(250); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 1 — Apply saved search filters and click Search
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step1_SearchAsync(SavedBooking b)
    {
        Report($"Step 1 — Searching {b.FromCode} → {b.ToCode} on {b.JourneyDate}...");

        await DismissLoginPopupAsync();

        // From station
        await ClickAsync("document.querySelectorAll('p-autocomplete input')[0]");
        await D(150);
        await Exec($"__h.fill('p-autocomplete input', '{Esc(b.FromCode)}')");
        // wait for the autocomplete list to populate (polls, not a fixed sleep)
        var fromItemJs = AutocompleteItemJs("origin", b.FromCode);
        await WaitForAsync($"!!({fromItemJs})", 3000, pollMs: 200);
        await ClickAsync(fromItemJs);
        await D(250);

        // To station
        await Exec($@"(function(){{
  var inp = document.querySelectorAll('p-autocomplete input')[1];
  if (!inp) return;
  inp.focus();
  var s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(inp,'{Esc(b.ToCode)}');
  inp.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
}})();");
        var toItemJs = AutocompleteItemJs("destination", b.ToCode);
        await WaitForAsync($"!!({toItemJs})", 3000, pollMs: 200);
        await ClickAsync(toItemJs);
        await D(250);

        // Date
        var dp = b.JourneyDate.Split('-');
        if (dp.Length == 3)
        {
            var dt = $"{dp[0]}/{MonthNum(dp[1])}/{dp[2]}";
            await Exec($@"(function(){{
  var d=document.querySelector('p-calendar input');
  if(!d) return;
  var s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(d,'{dt}');
  d.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
  d.dispatchEvent(new Event('change',{{bubbles:true}}));
}})();");
        }
        await D(400);

        // Class filter — checks the LIVE dropdown text rather than assuming
        // "All Classes" is the page default (see the quota comment below for
        // why that assumption breaks).
        var wantClassKw = ClassKeyword(b.TravelClass).ToUpper();
        var currentClassText = (await DropdownLabelAsync("journeyClass")).ToUpper();
        if (!string.IsNullOrEmpty(b.TravelClass) && !currentClassText.Contains(wantClassKw))
        {
            bool clsOk = await SelectDropdownAsync("journeyClass", 0, ClassKeyword(b.TravelClass));
            if (!clsOk) Report($"Step 1 — Could not select class '{b.TravelClass}', left at default.");
            await D(250); await InjectAsync();
        }

        // Quota filter — checks the LIVE dropdown text instead of trusting
        // "b.Quota == GN means it's already General". IRCTC's Angular app
        // persists the last-used quota via localStorage in this browser
        // profile, and this app reuses ONE persistent WebView2 profile
        // across both the search and booking features — so a previous
        // booking's Tatkal selection can silently become "the default" for
        // a later plain search. Confirmed live: a search inherited Tatkal
        // from an earlier booking, and Tatkal requires being logged in just
        // to search, so it always returned zero trains.
        var wantQuotaKw = QuotaKeyword(b.Quota).ToUpper();
        var currentQuotaText = (await DropdownLabelAsync("journeyQuota")).ToUpper();
        if (!string.IsNullOrEmpty(b.Quota) && !currentQuotaText.Contains(wantQuotaKw))
        {
            bool qOk = await SelectDropdownAsync("journeyQuota", 1, QuotaKeyword(b.Quota));
            if (!qOk) Report($"Step 1 — Could not select quota '{b.Quota}', left at default.");
            await D(250); await InjectAsync();
        }

        // The language "Alert" popup doesn't reliably appear (or disappear)
        // on any fixed schedule — it can show up mid-way through filling the
        // form, or reappear after being dismissed once, and a still-open
        // modal intercepts the Search click entirely (the page just sits on
        // the search form with no results ever loading). So this isn't a
        // one-shot click: dismiss-then-click is retried against a real
        // verification (results actually showing) rather than assumed to
        // have worked.
        Report("Step 1 — Clicking Search Trains...");
        const string searchDoneJs =
            "location.href.indexOf('train-list')>=0 || !!document.querySelector('app-train-avl-enq') || __h.pageHas('Results for')";
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (await ExecBool(searchDoneJs)) break;

            await DismissLanguageAlertAsync();
            await DismissLoginPopupAsync();
            // Direct DOM .click() rather than the coordinate-based CDP click
            // ClickText/ClickAsync use: this page carries ad iframes
            // (googleads.g.doubleclick.net) that can sit on top of the
            // button's screen position, silently swallowing a physical click
            // at those coordinates while ClickAsync still reports success. A
            // direct .click() on the element can't be intercepted that way.
            bool clicked = await ClickDomAsync(
                "Array.from(document.querySelectorAll('button')).find(function(b){return (b.innerText||'').toUpperCase().indexOf('SEARCH')>=0 && b.offsetParent!==null;})");
            Report($"Step 1 — Search click found+fired: {clicked}");

            // Poll for success rather than a blind fixed wait — Angular's
            // route change is usually near-instant once the click actually
            // lands, so this confirms success as soon as it happens instead
            // of always paying the full settle window.
            await WaitForAsync(searchDoneJs, 1500, pollMs: 200);
            await InjectAsync();
        }
        await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEPS 2-3-4 — Find train → select class → select date → Book Now → Yes
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step2_3_4_SelectTrainClassDateBookAsync(SavedBooking b)
    {
        // Step 2 — find the saved train
        Report($"Step 2 — Looking for train {b.TrainNo} ({b.TrainName})...");
        bool found = await WaitForAsync($"__h.pageHas('{b.TrainNo}')", 12000);
        if (!found)
        {
            Report($"Train {b.TrainNo} not found — select it manually, then click 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }

        // Step 3a — click the class availability box (shows "Refresh ↻") that
        // belongs to THIS saved train. The results list shows several trains
        // at once, each with its own class boxes, so a page-wide text match
        // was clicking whichever train's box happened to come first in the
        // DOM instead of the one the user actually saved.
        var kw   = ClassKeyword(b.TravelClass).ToUpper();
        var code = b.TravelClass.ToUpper();
        Report($"Step 3 — Clicking class [{code}] for train {b.TrainNo}...");

        // Scroll the train's row into view first and poll for its class box
        // rather than a single immediate attempt: each train's fare/class
        // availability appears to load independently of its heading (that's
        // what the "Refresh" placeholder means), so a row further down the
        // results list can still be filling in a moment after Step 2 already
        // found its heading text on the page.
        await ScrollTrainIntoViewAsync(b.TrainNo);
        await D(500); await InjectAsync();

        var classJs = ClassBoxJs(b.TrainNo, kw, code);
        await WaitForAsync($"!!({classJs})", 6000);
        bool classClicked = await ClickAsync(classJs);

        if (!classClicked)
        {
            await ReportClassBoxesAsync(b.TrainNo, kw, code);
            Report($"Class box not clickable — click '{code}' manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }

        // Step 3b — wait for availability data to load (WL/AVAIL/DEPARTED appears)
        Report("Step 3 — Waiting for availability dates to load...");
        bool avlReady = await WaitForAsync(@"(function(){
  return Array.from(document.querySelectorAll('div,span,td'))
    .some(function(e){
      var t=(e.innerText||'').toUpperCase().trim();
      return (t.startsWith('AVAIL')||t.startsWith('WL')||t==='TRAIN DEPARTED'||t.includes('NOT AVAIL'))
          && e.offsetHeight<60;
    });
})()", 10000);

        if (!avlReady)
        {
            Report("Dates didn't load — click the class box manually, then 'OK (Continue)'.");
            await UserAckAsync();
        }
        await D(400); await InjectAsync();

        // Step 3c — click the saved journey date. Confirmed live markup: each
        // date option is ALSO a ".pre-avl" widget (the same component the
        // class tabs used before the panel expanded), with the date text in
        // its own <strong> and the WL/AVAILABLE/NOT AVAILABLE status in a
        // separate sibling <strong> — so match on the date's <strong>
        // specifically instead of a combined-text/height heuristic. The
        // class tabs are now a <p-tabmenu>/<li> structure at this point (not
        // ".pre-avl"), so this selector can't collide with them.
        var dp    = b.JourneyDate.Split('-');
        var day   = dp.Length > 0 ? dp[0].TrimStart('0') : "";
        var month = dp.Length > 1 ? dp[1].ToUpper() : "";
        Report($"Step 3 — Selecting date {day} {month}...");

        bool dateClicked = await ClickAsync($@"(function(){{
  var day = '{day}', month = '{month}';
  return Array.from(document.querySelectorAll('.pre-avl')).find(function(box){{
    var label = box.querySelector('strong');
    var t = ((label && label.textContent) || '').toUpperCase();
    return t.includes(day) && t.includes(month) && !t.includes('DEPARTED');
  }});
}})()");

        if (!dateClicked)
        {
            Report($"Date not found — click {day} {month} manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }

        // Step 3d — wait for Book Now to become enabled, and Step 4 — click
        // it. Every train card has its own "Book Now" button, and a train
        // that hasn't had a class/date picked yet still renders its button
        // WITHOUT the HTML disabled attribute (it just looks muted via CSS)
        // — so a page-wide "!b.disabled" search matched whichever train's
        // button came first in the DOM (e.g. Gitanjali Exp) instead of the
        // saved train's own, now-actually-active one. Scope to this train's
        // card the same way the class box is scoped: find the card first
        // (an ancestor of the train's number label that contains a "Book
        // Now" button at all), then look for the enabled one inside it.
        var bookNowJs = BookNowBtnJs(b.TrainNo);
        Report("Step 3 — Waiting for Book Now to enable...");
        await WaitForAsync($"!!({bookNowJs})", 8000);
        await D(600); await InjectAsync();

        Report("Step 4 — Clicking Book Now...");
        bool bookClicked = await ClickAsync(bookNowJs);

        if (!bookClicked)
        {
            Report("Book Now not found — click it manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }
        await D(800); await InjectAsync();

        // Step 4 — handle date/station confirmation dialog
        bool confirm = await WaitForAsync(
            "__h.pageHas('Do you want to continue') || __h.pageHas('want to continue with')", 5000);
        if (confirm)
        {
            Report("Step 4 — Confirmation dialog: clicking Yes...");
            await ClickText("button", "YES");
            await D(1000); await InjectAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 0 — Log in FIRST, before touching the search form at all.
    //  Confirmed via user-provided DOM: the "LOGIN / REGISTER" link is
    //  <a aria-label="Click here to Login in application">, which LoginAsync's
    //  existing generic "LOGIN" text search already matches — no change
    //  needed there, this just calls it earlier in the sequence. Doesn't
    //  replace Step 5's re-login check below: IRCTC can still re-prompt
    //  after Book Now regardless of an earlier login, so that stays as a
    //  safety net (it's a fast no-op if the session is still authenticated).
    // ═══════════════════════════════════════════════════════════════════════
    private const string LoginLinkVisibleJs = @"(function(){
  var a = document.querySelector('a[aria-label=""Click here to Login in application""]');
  if (a && a.offsetParent!==null) return true;
  var icon = document.querySelector('i.fa-user, .fa.fa-user');
  return !!icon && icon.offsetParent!==null;
})()";

    private async Task Step0_LoginFirstAsync(string user, string pass)
    {
        bool loginLinkPresent = await ExecBool(LoginLinkVisibleJs);
        if (!loginLinkPresent)
        {
            Report("Step 0 — Already logged in.");
            return;
        }

        Report("Step 0 — Logging in before starting the search...");
        try
        {
            await LoginAsync(user, pass);
            await D(500); await InjectAsync();
        }
        catch (Exception ex)
        {
            Report($"Step 0 — Early login didn't complete ({ex.Message}); will retry after Book Now.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 5 — IRCTC always shows login again after Book Now
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step5_ReLoginAsync()
    {
        Report("Step 5 — Checking for re-login prompt...");
        await D(1000); await InjectAsync();

        bool needLogin = await WaitForAsync(
            @"__h.exists('input[placeholder=""User Name""]') || " +
            @"__h.exists('input[formcontrolname=""userid""]') || " +
            @"__h.pageHas('Please login') || __h.pageHas('Login to proceed')", 6000);

        if (needLogin)
        {
            Report("Step 5 — Re-login required. Logging in automatically...");
            await LoginAsync(_lastUser, _lastPass);
            await D(1500); await InjectAsync();
        }

        // Confirm we reached the passenger form
        bool onPassForm = await WaitForAsync(
            "__h.pageHas('Passenger') || __h.pageHas('Traveller') || __h.pageHas('Add Passenger')", 8000);
        if (!onPassForm)
        {
            Report("Passenger form not detected — please complete any steps in browser, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 6 — Passenger details
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step6_PassengersAsync(SavedBooking b)
    {
        Report("Step 6 — Filling passenger details...");
        await D(800); await InjectAsync();

        for (int i = 0; i < b.Passengers.Count; i++)
        {
            var p = b.Passengers[i];

            // Add an extra passenger row first — verify the row count grew.
            if (i > 0)
            {
                int idx = i;
                await EnsureAsync(
                    $"Add passenger row #{idx + 1}",
                    () => ClickText("a,button", "Add Passenger"),
                    $@"document.querySelectorAll(
                        'input[id*=""passengerName""],input[placeholder*=""Name""]').length > {idx}");
            }

            // Validate name length (3-16 chars as per IRCTC requirement)
            var name = p.Name.Trim();
            if (name.Length < 3)  name = (name + "   ").Substring(0, 3);
            if (name.Length > 16) name = name.Substring(0, 16);

            Report($"Step 6 — Passenger {i + 1}: {name}, Age {p.Age}, {p.Gender}...");

            // ── Name — fill then verify the input actually holds the value ──
            await EnsureAsync(
                $"Set name '{name}'",
                () => Exec($@"(function(){{
  var inputs = document.querySelectorAll(
    'input[id*=""passengerName""], input[placeholder*=""Passenger Name""], input[placeholder*=""Name""]');
  var el = inputs[{i}]; if(!el) return;
  el.focus();
  var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(el, '{Esc(name)}');
  el.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
  el.dispatchEvent(new Event('change',{{bubbles:true}}));
  el.dispatchEvent(new FocusEvent('blur',{{bubbles:true}}));
}})();"),
                $@"(function(){{
  var inputs=document.querySelectorAll(
    'input[id*=""passengerName""],input[placeholder*=""Passenger Name""],input[placeholder*=""Name""]');
  var el=inputs[{i}];
  return el && (el.value||'').trim().toUpperCase()==='{name.ToUpper().Trim()}';
}})()");

            // ── Age — fill then verify ──────────────────────────────────────
            await EnsureAsync(
                $"Set age {p.Age}",
                () => Exec($@"(function(){{
  var inputs = document.querySelectorAll('input[id*=""passengerAge""], input[placeholder*=""Age""]');
  var el = inputs[{i}]; if(!el) return;
  el.focus();
  var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(el, '{p.Age}');
  el.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
  el.dispatchEvent(new Event('change',{{bubbles:true}}));
}})();"),
                $@"(function(){{
  var inputs=document.querySelectorAll('input[id*=""passengerAge""],input[placeholder*=""Age""]');
  var el=inputs[{i}];
  return el && (el.value||'').trim()==='{p.Age}';
}})()");

            // ── Gender — open p-dropdown, pick option, verify it's displayed ─
            var gLabel = p.Gender == "F" ? "Female" : p.Gender == "T" ? "Transgender" : "Male";
            int gi = i;
            await EnsureAsync(
                $"Set gender to {gLabel}",
                () => SelectGenderAsync(gi, gLabel),
                GenderVerifyJs(gi, gLabel));
        }

        await D(400); await InjectAsync();
        await SelectCateringOptionAsync();

        // ── Continue Booking → leads to the PAYMENT-METHOD page ────────────
        // (BHIM/UPI selection happens on that next page, not here.)
        // CLICK ONCE ONLY — IRCTC rejects double-clicks. Then poll for the page.
        const string onPaymentPageJs = @"__h.pageHas('Pay through BHIM') || __h.pageHas('BHIM/UPI')
              || __h.pageHas('Convenience Fee')
              || (__h.pageHas('Pay through') && document.querySelectorAll('input[type=""radio""]').length>0)";

        Report("Step 6 — Passenger details verified. Clicking Continue Booking...");
        if (!await ExecBool(onPaymentPageJs))
        {
            bool c = await ClickText("button", "Continue Booking");
            if (!c) c = await ClickText("button", "CONTINUE");
            if (!c) await ClickText("button", "Continue");
            await WaitForAsync(onPaymentPageJs, 15000);   // single click → wait
        }

        await D(1200); await InjectAsync();
    }

    // Verify gender shows the chosen label in the p-dropdown / select
    private static string GenderVerifyJs(int i, string gLabel) => $@"(function(){{
  // native select check
  var sels=Array.from(document.querySelectorAll('select')).filter(function(s){{
    return (s.getAttribute('formcontrolname')||'').toLowerCase().includes('gender')
        || /male|female/i.test(s.innerText);
  }});
  if(sels[{i}]) {{
    var o=sels[{i}].options[sels[{i}].selectedIndex];
    if(o && (o.text||'').toUpperCase().includes('{gLabel.ToUpper()}')) return true;
  }}
  // p-dropdown: the chosen value renders as .ui-dropdown-label / .p-dropdown-label text
  var drops=Array.from(document.querySelectorAll('p-dropdown')).filter(function(d){{
    return (d.getAttribute('formcontrolname')||d.getAttribute('ng-reflect-name')||'')
             .toLowerCase().includes('gender')
        || /gender|male|female/i.test(d.innerText||'');
  }});
  var d=drops[{i}];
  if(!d) return false;
  var lbl=d.querySelector('.ui-dropdown-label,.p-dropdown-label,.ui-dropdown-trigger ~ span');
  var t=(lbl? lbl.innerText : d.innerText||'').toUpperCase();
  return t.includes('{gLabel.ToUpper()}');
}})()";

    // Verify the BHIM/UPI option is the selected one.
    // Robust across IRCTC variants: checks hidden input .checked, PrimeNG highlight
    // classes, aria-checked, AND the visible orange-fill styling.
    private static string UpiVerifyJs() => @"(function(){
  function isBhimRow(el){
    var row = el.closest('tr,label,div') || el;
    // climb a couple levels to capture the option's text
    for (var i=0;i<3 && row;i++){
      if (/bhim\s*\/?\s*upi|bhim|pay through bhim/i.test(row.innerText||'')) return true;
      row = row.parentElement;
    }
    return false;
  }

  // 1) hidden <input type=radio>.checked
  var inputs = Array.from(document.querySelectorAll('input[type=""radio""]'));
  for (var x of inputs){
    if (x.checked && isBhimRow(x)) return true;
  }

  // 2) PrimeNG highlighted box (any class variant) within the BHIM/UPI row
  var boxes = Array.from(document.querySelectorAll(
    '.ui-radiobutton-box,.p-radiobutton-box,.ui-radiobutton,.p-radiobutton,[role=""radio""]'));
  for (var b of boxes){
    var cls = b.className || '';
    var highlighted = /highlight|active|checked|selected/i.test(cls)
      || b.getAttribute('aria-checked') === 'true';
    if (highlighted && isBhimRow(b)) return true;
  }

  // 3) Visual fallback: a filled (orange) radio icon inside the BHIM/UPI row.
  //    PrimeNG renders the inner dot as .ui-radiobutton-icon when selected.
  var icons = Array.from(document.querySelectorAll(
    '.ui-radiobutton-icon,.p-radiobutton-icon'));
  for (var ic of icons){
    var visible = ic.offsetParent !== null
      && getComputedStyle(ic).visibility !== 'hidden';
    if (visible && isBhimRow(ic)) return true;
  }
  return false;
})()";

    // ── Gender selection (PrimeNG p-dropdown OR native select) ─────────────
    private async Task SelectGenderAsync(int passengerIndex, string gLabel)
    {
        Report($"Step 6 — Setting gender to {gLabel}...");
        await InjectAsync();

        // CASE A: native <select> (set value directly)
        bool isNative = await ExecBool($@"(function(){{
  var sels = Array.from(document.querySelectorAll('select'))
    .filter(function(s){{
       var fc=(s.getAttribute('formcontrolname')||'').toLowerCase();
       return fc.includes('gender') || /male|female/i.test(s.innerText);
    }});
  return !!sels[{passengerIndex}];
}})()");

        if (isNative)
        {
            await Exec($@"(function(){{
  var sels = Array.from(document.querySelectorAll('select'))
    .filter(function(s){{
       var fc=(s.getAttribute('formcontrolname')||'').toLowerCase();
       return fc.includes('gender') || /male|female/i.test(s.innerText);
    }});
  var sel = sels[{passengerIndex}]; if(!sel) return;
  var opt = Array.from(sel.options).find(function(o){{
     return (o.text||'').toUpperCase().includes('{gLabel.ToUpper()}');
  }});
  if(opt){{ sel.value = opt.value;
     sel.dispatchEvent(new Event('change',{{bubbles:true}})); }}
}})();");
            await D(400);
            return;
        }

        // CASE B: PrimeNG p-dropdown.
        // 1) Click the dropdown trigger to OPEN it
        bool opened = await ClickAsync($@"(function(){{
  // gender dropdowns: the formcontrolname usually contains 'gender' / 'passengerGender'
  var drops = Array.from(document.querySelectorAll('p-dropdown'))
    .filter(function(d){{
       var fc=(d.getAttribute('formcontrolname')||d.getAttribute('ng-reflect-name')||'').toLowerCase();
       return fc.includes('gender');
    }});
  // fallback: dropdowns whose placeholder/label mentions gender
  if(!drops.length) {{
    drops = Array.from(document.querySelectorAll('p-dropdown')).filter(function(d){{
       return /gender|male|female/i.test(d.innerText||'')
           || /gender/i.test(d.getAttribute('aria-label')||'');
    }});
  }}
  var d = drops[{passengerIndex}];
  if(!d) return null;
  // the clickable trigger inside the p-dropdown
  return d.querySelector('.ui-dropdown-trigger, .p-dropdown-trigger, .ui-dropdown, .p-dropdown') || d;
}})()");

        await D(600); await InjectAsync();

        // 2) Click the matching option in the overlay panel (appended to body)
        bool picked = await ClickAsync($@"(function(){{
  var items = Array.from(document.querySelectorAll(
    '.ui-dropdown-item, .p-dropdown-item, li[role=""option""], .ui-dropdown-items li, .p-dropdown-items li'));
  return items.find(function(li){{
     return (li.innerText||'').trim().toUpperCase().includes('{gLabel.ToUpper()}');
  }}) || null;
}})()");

        // 3) Fallback: type into the dropdown filter then Enter, or use keyboard
        if (!opened || !picked)
        {
            await Exec($@"(function(){{
  // Try setting the underlying Angular control by clicking any visible option text
  var items = Array.from(document.querySelectorAll('li,span,div'))
    .filter(function(e){{
       var t=(e.innerText||'').trim().toUpperCase();
       return t==='{gLabel.ToUpper()}' && e.offsetParent!==null;
    }});
  if(items[0]) items[0].click();
}})();");
        }
        await D(400);
    }

    // ── BHIM/UPI payment radio (PrimeNG p-radiobutton) ─────────────────────
    private async Task SelectUpiPaymentAsync()
    {
        await InjectAsync();

        // PrimeNG renders the visible radio as a .ui-radiobutton-box div next to
        // a hidden <input type=radio>. We must click the BOX or its label.
        bool clicked = await ClickAsync(@"(function(){
  function txt(e){ return (e ? (e.innerText||'') : '').toUpperCase(); }

  // 1) Find a label / container mentioning UPI or BHIM
  var labels = Array.from(document.querySelectorAll('label,div,span,td'))
    .filter(function(e){
       var t = txt(e);
       return (t.includes('BHIM') || t.includes('UPI')) && e.offsetParent!==null
              && t.length < 60;   // avoid huge containers
    })
    .sort(function(a,b){ return a.innerText.length - b.innerText.length; });

  for (var lbl of labels) {
    // Try to find the clickable radio box within or near this label
    var row = lbl.closest('.ui-radiobutton, .p-radiobutton, tr, .col-pad, div') || lbl;
    var box = row.querySelector('.ui-radiobutton-box, .p-radiobutton-box, .ui-radiobutton, .p-radiobutton');
    if (box && box.offsetParent !== null) return box;
  }
  // 2) Fall back to the label itself
  if (labels[0]) return labels[0];
  return null;
})()");

        if (!clicked)
        {
            // Last resort: click any radiobutton box whose row text has UPI
            await ClickText("label,div,span", "BHIM/UPI");
        }

        await D(500); await InjectAsync();

        // Verify the radio is now checked; if not, click the hidden input directly
        bool ok = await ExecBool(@"(function(){
  var r = Array.from(document.querySelectorAll('input[type=""radio""]'))
    .find(function(x){
       var v=(x.value||x.id||'').toLowerCase();
       var lbl=document.querySelector('label[for=""'+x.id+'""]');
       return v.includes('upi')||v.includes('bhim')
           || (lbl && /upi|bhim/i.test(lbl.innerText||''));
    });
  return r && r.checked;
})()");

        if (!ok)
        {
            await Exec(@"(function(){
  var r = Array.from(document.querySelectorAll('input[type=""radio""]'))
    .find(function(x){
       var v=(x.value||x.id||'').toLowerCase();
       var lbl=document.querySelector('label[for=""'+x.id+'""]');
       return v.includes('upi')||v.includes('bhim')
           || (lbl && /upi|bhim/i.test(lbl.innerText||''));
    });
  if(r){ r.checked=true;
         r.click();
         r.dispatchEvent(new Event('change',{bubbles:true})); }
})();");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 6b — Payment-method page: select BHIM/UPI radio → Continue
    //  (This page shows: "Pay through Cards/.../UPI_CC"  and
    //   "Pay through BHIM/UPI". We must pick BHIM/UPI then Continue.)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step6b_SelectPaymentMethodAsync()
    {
        // Make sure we're actually on the payment-method page first.
        Report("Step 6b — Waiting for payment-method page...");
        await WaitForAsync(
            @"__h.pageHas('Pay through BHIM') || __h.pageHas('BHIM/UPI')
              || __h.pageHas('Convenience Fee')", 12000);
        await D(800); await InjectAsync();

        // 1) Select BHIM/UPI — best-effort with a few retries. We do NOT hard-gate
        //    on verification here, because the visible orange dot can register
        //    inconsistently; the Continue step below confirms real progress.
        for (int i = 0; i < 4; i++)
        {
            await SelectUpiPaymentAsync();
            await D(600); await InjectAsync();
            if (await ExecBool(UpiVerifyJs())) { Report("Step 6b — BHIM/UPI selected."); break; }
            Report($"Step 6b — Re-selecting BHIM/UPI ({i + 1}/4)...");
        }

        // 2) Re-assert the UPI radio, then click Continue EXACTLY ONCE and wait.
        //    IRCTC rejects double-clicks ("Sorry!! Please Try again"), so we must
        //    NOT retry the Continue click — click once, then poll for progress.
        await Exec(@"(function(){
  var r = Array.from(document.querySelectorAll('input[type=""radio""]')).find(function(x){
    var v=(x.value||x.id||'').toLowerCase();
    var lbl=document.querySelector('label[for=""'+x.id+'""]');
    return v.includes('upi')||v.includes('bhim')
        || (lbl && /upi|bhim/i.test(lbl.innerText||''));
  });
  if(r && !r.checked){ r.checked=true; r.click();
                       r.dispatchEvent(new Event('change',{bubbles:true})); }
})();");
        await D(250);

        const string leftPaymentMethodJs = @"__h.captchaVisible() || __h.pageHas('Enter Captcha')
              || Array.from(document.querySelectorAll('button,a')).some(function(b){
                   var t=(b.innerText||'').toUpperCase();
                   return t.includes('PAY')&&t.includes('BOOK');
                 })
              || /scan|qr code/i.test(document.body.innerText||'')
              || !__h.pageHas('Convenience Fee')";

        Report("Step 6b — Clicking Continue (once)...");
        if (!await ExecBool(leftPaymentMethodJs))
        {
            bool c = await ClickAsync(@"(function(){
  var cands = Array.from(document.querySelectorAll('button,a,span,div'))
    .filter(function(e){
       var t=(e.innerText||'').trim().toUpperCase();
       return t==='CONTINUE' && e.offsetParent!==null && !e.disabled;
    })
    .sort(function(a,b){
       var ab=(a.tagName==='BUTTON'||a.tagName==='A')?0:1;
       var bb=(b.tagName==='BUTTON'||b.tagName==='A')?0:1;
       if(ab!==bb) return ab-bb;
       return (a.innerText.length)-(b.innerText.length);
    });
  return cands[0] || null;
})()");
            if (!c) c = await ClickText("button", "CONTINUE");
            if (!c) await ClickText("button,a", "Continue");
            await WaitForAsync(leftPaymentMethodJs, 15000);   // single click → wait
        }

        await D(1200); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 7 — Auto-resolve CAPTCHA on the next page (no human interaction)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step7_ResolveCaptchaAsync()
    {
        Report("Step 7 — Waiting for captcha page...");
        bool hasCaptcha = await WaitForAsync(
            @"__h.captchaVisible() || __h.pageHas('Enter Captcha') || __h.pageHas('Captcha')",
            15000);

        if (!hasCaptcha)
        {
            Report("Step 7 — No captcha detected on this page. Proceeding...");
            return;
        }

        // Automatically read + enter the captcha (retries internally)
        await AutoSolveCaptchaAsync();
        await D(500); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 8 — Two pages after captcha:
    //    8a) Review / Fare Summary page  → click Continue
    //    8b) Payment Methods page        → select 'BHIM/UPI/USSD' tile → Continue
    //  We do NOT advance to Step 9 until a real 'Pay & Book' button is present.
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step8_ContinueToReviewAsync()
    {
        // ── 8a) Review/Fare Summary page → Continue ───────────────────────
        // CRITICAL: IRCTC rejects the booking ("Sorry!! Please Try again" — reason:
        // "double clicked on any options/buttons") if Continue is clicked more than
        // once. So we click EXACTLY ONCE, then WAIT (poll) for the Payment Methods
        // page — we never re-click. Solve captcha first if it's still up.
        const string onPaymentMethodsTextJs = @"(function(){
   if (__h.captchaVisible()) return false;
   var t=(document.body.innerText||'').toUpperCase();
   return t.includes('PAYMENT METHODS')
       || t.includes('BHIM/ UPI/ USSD') || t.includes('BHIM/UPI/USSD')
       || t.includes('IRCTC IPAY')
       || t.includes('MULTIPLE PAYMENT SERVICE');
})()";

        if (await ExecBool(@"__h.captchaVisible()"))
        {
            await AutoSolveCaptchaAsync(3); await D(400);
        }

        if (!await ExecBool(onPaymentMethodsTextJs))
        {
            await SelectCateringOptionAsync();
            Report("Step 8a — Clicking Continue (once) on Review page...");
            await ClickContinueAsync();
            // Single click only — then poll for the next page. No re-clicking.
            await WaitForAsync(onPaymentMethodsTextJs, 15000);
        }

        await D(400); await InjectAsync();

        // ── 8b) → Click "Continue" on the Payment Methods page ────────────────
        // Wait for the Continue button to exist, clear any notification popup that
        // could be covering it, then click it.
        Report("Step 8b — Waiting for Continue button...");
        await WaitForAsync($"!!({ContinueBtnJs})", 15000);
        await DismissPopupsAsync();

        Report("Step 8b — Clicking Continue on Payment Methods page...");
        bool dom = await ClickDomAsync(ContinueBtnJs);
        bool cdp = dom || await ClickAsync(ContinueBtnJs);
        Report($"Step 8b — clicked Continue (dom={dom}, cdp={cdp})");

        await D(1200); await InjectAsync();
    }

    // IRCTC added a "Catering Service Option*" dropdown that must have a
    // value selected before Continue will proceed — clicking without it just
    // shows a validation toast ("Please select your Catering Service
    // Choice") and leaves the flow stuck on the same page forever, since
    // Step 8a is deliberately a single click (never re-clicked, to avoid
    // IRCTC's double-click rejection) with no logic to recover from a
    // validation failure. This is an optional add-on unrelated to booking a
    // plain ticket, so it picks a "no thanks"-style option if one exists,
    // else just the first real (non-placeholder) option — whichever page
    // this shows up on (seen on the review/continue page; called
    // defensively after Step 6's passenger form too, in case IRCTC shows it
    // there for some bookings).
    private async Task SelectCateringOptionAsync()
    {
        bool handled = await ExecBool(@"(function(){
  var sel = Array.from(document.querySelectorAll('select')).find(function(s){
    return /catering/i.test(s.outerHTML) || /catering/i.test((s.closest('div')||{}).innerText||'');
  });
  if (!sel) return false;
  if (sel.value && sel.selectedIndex > 0) return true; // already has a real selection

  var opts = Array.from(sel.options);
  var pick = opts.find(function(o){ return /\bno\b|not required|don.?t want|none/i.test(o.textContent||''); })
          || opts.find(function(o, idx){ return idx>0 && (o.textContent||'').trim().length>0; });
  if (!pick) return false;

  sel.value = pick.value;
  sel.dispatchEvent(new Event('change', {bubbles:true}));
  return true;
})()");

        if (handled)
        {
            Report("Step 8 — Selected a Catering Service option (required by IRCTC to continue).");
            await D(300); await InjectAsync();
        }
    }

    // Click whatever visible button reads exactly "CONTINUE" (button > anchor > bar)
    private async Task ClickContinueAsync()
    {
        bool c = await ClickAsync(@"(function(){
  var cands = Array.from(document.querySelectorAll('button,a,span,div'))
    .filter(function(e){
       var t=(e.innerText||'').trim().toUpperCase();
       return t==='CONTINUE' && e.offsetParent!==null && !e.disabled;
    })
    .sort(function(a,b){
       var ab=(a.tagName==='BUTTON'||a.tagName==='A')?0:1;
       var bb=(b.tagName==='BUTTON'||b.tagName==='A')?0:1;
       if(ab!==bb) return ab-bb;
       return a.innerText.length-b.innerText.length;
    });
  return cands[0]||null;
})()");
        if (!c) await ClickText("button", "Continue");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 9 — Two sequential clicks → payment gateway:
    //    9a) Payment Methods page (BHIM/UPI/USSD selected) → click "Continue"
    //    9b) Review / Pay&Book page                        → click "Pay & Book"
    //    → IRCTC redirects to the UPI gateway where the QR renders.
    //  Each click WAITS for the next page to fully load before proceeding.
    // ═══════════════════════════════════════════════════════════════════════

    // Shared visibility test. offsetParent alone is unreliable here: the mobile
    // bottom action bars live inside a <p-sidebar style=""height:0"> wrapper yet are
    // rendered/clickable (opacity:1, fixed position). So we check real geometry +
    // computed style instead, and ignore the price button (""₹ ..."").
    private const string VisibleBtnTest = @"
    function __vis(el){
      if(!el || el.disabled) return false;
      var t=(el.innerText||'').trim();
      if(/^₹/.test(t)) return false;                       // skip the fare button
      // 'Rendered' = the element participates in layout. We use getClientRects(),
      // which is non-empty whenever the element is painted — even when an ancestor
      // <p-sidebar style='height:0'> clips its bounding box to zero (the old rect
      // check wrongly rejected the visible orange Continue/Pay&Book bottom bar).
      if(el.getClientRects().length === 0) return false;
      var cs=getComputedStyle(el);
      if(cs.visibility==='hidden' || cs.display==='none' || +cs.opacity===0) return false;
      // Reject only if the element itself or an ancestor is explicitly display:none
      // (that genuinely removes it). We do NOT reject on zero height, since the
      // sidebar wrapper legitimately has height:0 while its button still renders.
      for(var p=el.parentElement; p; p=p.parentElement){
        if(getComputedStyle(p).display==='none') return false;
      }
      return true;
    }";

    // visible, enabled "CONTINUE" button — desktop body or mobile bottom bar.
    // Prefer a __vis-passing match; if none (e.g. geometry clipped by a height:0
    // sidebar), fall back to any enabled CONTINUE button — a DOM click still works.
    private const string ContinueBtnJs = @"(function(){" + VisibleBtnTest + @"
  function pick(strict){
    return Array.from(document.querySelectorAll('button,a'))
      .filter(function(b){
         if(b.disabled) return false;
         var t=(b.innerText||'').trim().toUpperCase();
         if(t!=='CONTINUE') return false;
         return strict ? __vis(b) : true;
      })
      .sort(function(a,b){ return a.innerText.length-b.innerText.length; })[0] || null;
  }
  return pick(true) || pick(false);
})()";

    // visible, enabled "Pay & Book" button — desktop body or mobile bottom bar.
    private const string PayBookBtnJs = @"(function(){" + VisibleBtnTest + @"
  function pick(strict){
    return Array.from(document.querySelectorAll('button,a'))
      .filter(function(b){
         if(b.disabled) return false;
         var t=(b.innerText||'').trim().toUpperCase();
         if(!(t.includes('PAY')&&t.includes('BOOK'))) return false;
         return strict ? __vis(b) : true;
      })
      .sort(function(a,b){ return a.innerText.length-b.innerText.length; })[0] || null;
  }
  return pick(true) || pick(false);
})()";

    // true once the IRCTC payment gateway / UPI QR screen has rendered
    private const string GatewayReadyJs = @"
      /scan.*qr|qr.*scan|click here to pay|upi.*qr|phonepe|paytm|razorpay|billdesk|payment gateway|bharatqr|order id/i
        .test(document.body.innerText||'')";

    // true when IRCTC has bounced the booking to its error/logout page
    // ('Sorry!! please Try Again !!!' / 'To login click here'). When this shows,
    // the session/transaction was rejected — no QR will ever appear, so we must
    // stop instead of waiting at Step 10 forever.
    private const string BookingFailedJs = @"(function(){
      var t=(document.body.innerText||'').toLowerCase();
      return (t.includes('please try again') || t.includes('sorry'))
          && (t.includes('to login') || t.includes('click here'));
    })()";

    // true while we are STILL on the Payment Methods page (heading + BHIM/UPI/USSD
    // tile). Needed because a 'Pay & Book' button is ALSO present on this page (the
    // desktop body button), so 'Pay & Book exists' can NOT be used to detect that
    // we left it — that made Step 9a think it was already done and skip Continue.
    private const string OnPaymentMethodsJs = @"(function(){
      var t=(document.body.innerText||'').toUpperCase();
      return t.includes('PAYMENT METHODS')
          && (t.includes('BHIM/ UPI/ USSD') || t.includes('BHIM/UPI/USSD'));
    })()";

    // Picks the correct station-autocomplete suggestion for a code. Confirmed
    // live (via direct CDP testing against irctc.co.in):
    //  1. The dropdown's <li class="ui-autocomplete-list-item"> rows include
    //     non-selectable section dividers ("----- Journeys -----" /
    //     "----- Stations -----", marked by a ".disable-selection" child
    //     span) and "recent journey" combo shortcuts (contain "➨", pick BOTH
    //     from and to at once) — both carry the exact same list-item class as
    //     a real single-station row, so clicking the first DOM match could
    //     land on a divider or the wrong entry instead of the searched
    //     station.
    //  2. Each field's suggestion panel lives inside that field's own
    //     <p-autocomplete id="origin"|"destination">. An UNSCOPED
    //     document-wide query can match a stale leftover row from the OTHER
    //     field's already-closed panel — reproduced live: filling "To" with
    //     LTT matched a leftover "NEW DELHI - NDLS" row from the From field.
    //     Scoping to the specific field's own panel fixes this.
    // Filters dividers/journeys, then prefers the row whose text actually
    // contains "- CODE" (falls back to the first real station row in that
    // field's own panel if no exact code match is found).
    private static string AutocompleteItemJs(string fieldId, string code) => $@"(function(){{
  var scope = document.querySelector('#{fieldId} .ui-autocomplete-panel') || document.querySelector('#{fieldId}') || document;
  var items = Array.from(scope.querySelectorAll('li.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all'));
  var real = items.filter(function(li){{
    return !li.querySelector('.disable-selection') && (li.textContent||'').indexOf('➨') < 0;
  }});
  var codeRe = new RegExp('-\\s*{code}\\b', 'i');
  return real.find(function(li){{ return codeRe.test(li.textContent||''); }}) || real[0];
}})()";

    // Finds the class-availability box for a specific train. Confirmed live
    // markup (via DevTools) for each class is:
    //   <div class="pre-avl" tabindex="0">
    //     <div><strong>AC 2 Tier (2A)</strong></div>
    //     <div class="col-xs-12 link"> Refresh <span class="fa fa-repeat"></span></div>
    //   </div>
    // The class label and the "Refresh" text are separate sibling divs — no
    // single element contains both — so matching must target the actual
    // ".pre-avl" widget (it even carries tabindex="0", marking it as the
    // real clickable unit) via its <strong> label, not a fuzzy combined-text
    // search. Scoped to the train's own card first (walk up from its number
    // label to the nearest ancestor containing a ".pre-avl"), falling back
    // to a page-wide match if the card can't be located.
    private static string ClassBoxJs(string trainNo, string kw, string code) => $@"(function(){{
  function findBox(root){{
    return Array.from(root.querySelectorAll('.pre-avl')).find(function(box){{
      var label = box.querySelector('strong');
      var t = ((label && label.textContent) || box.innerText || '').toUpperCase();
      return t.includes('{kw}') || t.includes('{code}');
    }});
  }}

  var trainNo = '{trainNo}';
  var labelEls = Array.from(document.querySelectorAll('*')).filter(function(e){{
    return e.offsetParent!==null && e.children.length<=3 && (e.textContent||'').includes(trainNo);
  }});
  labelEls.sort(function(a,b){{ return (a.textContent||'').length-(b.textContent||'').length; }});
  var trainEl = labelEls[0];

  if (trainEl) {{
    var anc = trainEl, hops = 0;
    while (anc) {{
      if (anc.querySelector && anc.querySelector('.pre-avl')) {{
        var scoped = findBox(anc);
        if (scoped) return scoped;
        break;               // found the card but no matching class inside it — fall through
      }}
      anc = anc.parentElement; hops++;
      if (hops > 15) break;
    }}
  }}
  return findBox(document);  // fall back to a page-wide match rather than failing outright
}})()";

    // Finds the ENABLED "Book Now" button for a specific train. Every train
    // card has its own button, and a train with no class/date picked yet
    // still renders its button without the HTML disabled attribute (only
    // muted via CSS) — so an unscoped "not disabled" search matches
    // whichever train's button comes first in the DOM. Scope to the train's
    // card first (an ancestor of its number label containing a "Book Now"
    // button at all, enabled or not), then require the enabled one inside
    // it — falling back to a page-wide match if the card can't be located.
    private static string BookNowBtnJs(string trainNo) => $@"(function(){{
  function anyBtn(root){{
    return Array.from(root.querySelectorAll('button')).find(function(b){{
      return b.innerText.trim().toUpperCase()==='BOOK NOW';
    }});
  }}
  function enabledBtn(root){{
    return Array.from(root.querySelectorAll('button')).find(function(b){{
      return b.innerText.trim().toUpperCase()==='BOOK NOW' && !b.disabled;
    }});
  }}

  var trainNo = '{trainNo}';
  var labelEls = Array.from(document.querySelectorAll('*')).filter(function(e){{
    return e.offsetParent!==null && e.children.length<=3 && (e.textContent||'').includes(trainNo);
  }});
  labelEls.sort(function(a,b){{ return (a.textContent||'').length-(b.textContent||'').length; }});
  var trainEl = labelEls[0];

  if (trainEl) {{
    var anc = trainEl, hops = 0;
    while (anc) {{
      if (anyBtn(anc)) {{
        var scoped = enabledBtn(anc);
        if (scoped) return scoped;
        break;               // found the card but its button isn't enabled yet
      }}
      anc = anc.parentElement; hops++;
      if (hops > 20) break;
    }}
  }}
  return enabledBtn(document);  // fall back to a page-wide match rather than failing outright
}})()";

    // Scrolls a specific train's row into view before Step 3a searches for
    // its class box — the results list can be long enough that the saved
    // train sits below the fold, and some sites only finish computing a
    // row's fare/availability text once it's near the viewport.
    private async Task ScrollTrainIntoViewAsync(string trainNo)
    {
        await Exec($@"(function(){{
  var trainNo = '{trainNo}';
  var labelEls = Array.from(document.querySelectorAll('*')).filter(function(e){{
    return e.offsetParent!==null && e.children.length<=3 && (e.textContent||'').includes(trainNo);
  }});
  labelEls.sort(function(a,b){{ return (a.textContent||'').length-(b.textContent||'').length; }});
  var el = labelEls[0];
  if (el) el.scrollIntoView({{block:'center', inline:'nearest'}});
}})()");
    }

    // Diagnostic for a failed Step 3a class-box click: reports every ".pre-avl"
    // box on the page (its <strong> label, size, and whether it sits inside
    // this train's card) so a failure is debuggable from the status log
    // instead of a blind guess about the page's markup.
    private async Task ReportClassBoxesAsync(string trainNo, string kw, string code)
    {
        var raw = await Exec($@"(function(){{
  function describe(box){{
    var r = box.getBoundingClientRect();
    var label = box.querySelector('strong');
    return {{ label:((label&&label.textContent)||box.innerText||'').replace(/\s+/g,' ').slice(0,50),
      offsetH:box.offsetHeight, offsetW:box.offsetWidth,
      rectW:Math.round(r.width), rectH:Math.round(r.height) }};
  }}

  var trainNo='{trainNo}', kw='{kw}', code='{code}';
  var out = {{ trainNo:trainNo, kw:kw, code:code, trainElFound:false, trainElTag:'', trainElText:'',
    scopeFound:false, scopeHops:-1, scopeTag:'', boxesInScope:[], boxesGlobal:[] }};

  var labelEls = Array.from(document.querySelectorAll('*')).filter(function(e){{
    return e.offsetParent!==null && e.children.length<=3 && (e.textContent||'').includes(trainNo);
  }});
  labelEls.sort(function(a,b){{ return (a.textContent||'').length-(b.textContent||'').length; }});
  var trainEl = labelEls[0];
  if (trainEl) {{
    out.trainElFound = true; out.trainElTag = trainEl.tagName;
    out.trainElText = (trainEl.textContent||'').slice(0,60);

    var anc = trainEl, hops = 0;
    while (anc) {{
      if (anc.querySelector && anc.querySelector('.pre-avl')) {{
        out.scopeFound = true; out.scopeHops = hops; out.scopeTag = anc.tagName;
        out.boxesInScope = Array.from(anc.querySelectorAll('.pre-avl')).slice(0,10).map(describe);
        break;
      }}
      anc = anc.parentElement; hops++;
      if (hops>15) break;
    }}
  }}

  out.boxesGlobal = Array.from(document.querySelectorAll('.pre-avl')).slice(0,20).map(describe);

  return JSON.stringify(out);
}})()");
        var json = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IndianTicketing");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "class_box_diag.json"), json);
        }
        catch { /* diagnostic-only, never block the flow */ }

        Report($"Step 3 diag — train {trainNo}, class {code}: see class_box_diag.json");
    }

    // Report the visible action buttons currently on the page — turns a silent
    // race into hard data in the status log so we can see what Step 9 is seeing.
    private async Task ReportButtonsAsync(string tag)
    {
        var info = (await Exec(@"(function(){" + VisibleBtnTest + @"
  var out = Array.from(document.querySelectorAll('button,a'))
    .filter(__vis)
    .map(function(b){ return (b.innerText||'').trim().replace(/\s+/g,' '); })
    .filter(function(t){ return t.length>0 && t.length<40; });
  return out.join(' | ');
})()")).Trim('"');
        Report($"{tag} — visible buttons: {info}");
    }

    // Deep probe of the CONTINUE button: does it exist at all, does __vis pass,
    // how many client rects, is any ancestor display:none. Pins down exactly why
    // the click branch is or isn't taken — no more guessing about hidden-* classes.
    private async Task ProbeContinueAsync()
    {
        var info = (await Exec(@"(function(){" + VisibleBtnTest + @"
  var all = Array.from(document.querySelectorAll('button,a'))
    .filter(function(b){ return (b.innerText||'').trim().toUpperCase()==='CONTINUE'; });
  if(all.length===0) return 'CONTINUE: none in DOM';
  var b = all[0];
  var rects = b.getClientRects().length;
  var vis = __vis(b);
  var hiddenAnc='none';
  for(var p=b.parentElement;p;p=p.parentElement){
    if(getComputedStyle(p).display==='none'){ hiddenAnc=(p.tagName+'.'+(p.className||'').split(' ')[0]); break; }
  }
  return 'CONTINUE count='+all.length+' vis='+vis+' clientRects='+rects+' hiddenAncestor='+hiddenAnc;
})()")).Trim('"');
        Report($"Step 9 — {info}");
    }

    // Dismiss overlays that intercept clicks — chiefly the web-push
    // "Click on allow to subscribe to notifications" prompt (TrueNotify/izooto),
    // which sits ON TOP of the page and swallows the Continue click. We click its
    // 'Later'/'No thanks'/close control. Returns true if something was dismissed.
    private async Task<bool> DismissPopupsAsync()
    {
        await InjectAsync();
        bool dismissed = await ExecBool(@"(function(){
  var hit = false;

  // 1) Notification-permission prompts: a button reading Later / No thanks / Deny / Maybe later.
  var btns = Array.from(document.querySelectorAll('button,a,span,div'));
  var dismiss = btns.find(function(e){
     var t=(e.innerText||'').trim().toLowerCase();
     return (t==='later' || t==='no thanks' || t==='no, thanks' || t==='maybe later'
             || t==='deny' || t==='not now' || t==='dismiss')
            && e.offsetParent!==null && t.length<20;
  });
  if(dismiss){ try{ dismiss.click(); hit=true; }catch(e){} }

  // 2) izooto / truenotify / push overlays by id/class.
  var killSel = '[id*=""izooto""],[class*=""izooto""],[id*=""truenotify""],'
              + '[class*=""truenotify""],[id*=""onesignal""],[class*=""push""][class*=""prompt""]';
  Array.from(document.querySelectorAll(killSel)).forEach(function(el){
     try{ el.style.display='none'; el.style.pointerEvents='none'; hit=true; }catch(e){}
  });

  // 3) PrimeNG toast / dialog (e.g. the pink 'Info: undefined' box) — click its
  //    close icon so it stops covering the page / action buttons.
  var closers = Array.from(document.querySelectorAll(
     '.ui-toast-close-icon, .p-toast-icon-close, .ui-dialog-titlebar-close, '
   + '.p-dialog-header-close, [class*=""toast""] [class*=""close""], '
   + '[class*=""close-icon""]'));
  closers.forEach(function(el){
     if(el.offsetParent!==null){ try{ el.click(); hit=true; }catch(e){} }
  });

  return hit;
})()");
        if (dismissed) { Report("Step 9 — dismissed a popup/overlay."); await D(500); await InjectAsync(); }
        return dismissed;
    }

    private async Task Step9_PayAndBookAsync()
    {
        await InjectAsync();

        // Continue was clicked in Step 8b → the Pay & Book page needs a moment to
        // load. Wait for the button to actually exist before clicking it (clicking
        // before it renders is why it "didn't click"). Clear any notification popup
        // that could be sitting on top of the button, then click it.
        Report("Step 9 — Waiting for Pay & Book button...");
        await WaitForAsync($"!!({PayBookBtnJs})", 15000);
        await DismissPopupsAsync();

        Report("Step 9 — Clicking Pay & Book...");
        bool dom = await ClickDomAsync(PayBookBtnJs);
        //bool cdp = dom || await ClickAsync(PayBookBtnJs);
        Report($"Step 9 — clicked Pay & Book");

        await D(1500); await InjectAsync();

        // After Pay & Book, IRCTC either shows the payment gateway/QR OR bounces to
        // its "Sorry!! please Try Again / To login click here" error page (session /
        // transaction rejected). Detect the failure so we don't wait at Step 10 for
        // a QR that will never come.
        if (await WaitForAsync(BookingFailedJs, 4000))
        {
            Report("IRCTC rejected the booking (\"Sorry, please Try Again\" / login page). " +
                   "The session was lost or the transaction was declined — restart the booking.");
            throw new Exception(
                "IRCTC returned its 'Sorry, please Try Again' page after Pay & Book — " +
                "the booking session was rejected. Start the booking again.");
        }
    }

    // Wait until (a) the document has finished loading AND (b) `readyJs` is true.
    // Used between clicks so we never act on a half-rendered page.
    private async Task<bool> WaitForPageReadyAsync(string readyJs, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await InjectAsync();
                bool docLoaded = await ExecBool("document.readyState === 'complete'");
                bool ready     = await ExecBool($"!!({readyJs})");
                if (docLoaded && ready) return true;
            }
            catch { }
            await D(400);
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 10 — Capture UPI QR and display in app
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step10_CaptureQrAsync()
    {
        Report("Step 10 — Waiting for UPI payment QR code...");

        // Broad selector: img/canvas/svg whose attrs hint QR, plus the IRCTC iPay
        // gateway QR container.
        const string qrSelector =
            @"img[src*=""qr""], img[alt*=""qr""], img[alt*=""QR""], img[id*=""qr""], img[class*=""qr""], " +
            @".qr-code img, [class*=""qr""] img, [id*=""qr""] img, [class*=""Qr""] img, " +
            @"canvas[class*=""qr""], canvas[id*=""qr""], canvas.qrcode, " +
            @".qrImg, .qr_img, #qrImage, [class*=""qrcode""] img, [class*=""qrcode""] canvas, svg[class*=""qr""]";

        bool clickedReveal    = false;
        bool clickedQrElement = false;

        for (int attempt = 0; attempt < 120; attempt++)
        {
            await D(1500); await InjectAsync();

            // Bail out if IRCTC has shown its 'Sorry, please Try Again' / login page —
            // the QR will never come.
            if (await ExecBool(BookingFailedJs))
            {
                Report("Step 10 — IRCTC error page detected (\"Sorry, please Try Again\"). " +
                       "Booking was rejected; no QR will appear. Restart the booking.");
                return;
            }

            // 0) Some gateways show a placeholder with "Click here to pay through QR".
            //    Click it ONCE to render the real scannable QR. Confirmed via a live
            //    DOM dump (IRCTC's iPay UPI widget): this is a <span onclick=
            //    "submitUpiQrForm()"> inside #PayByQrButton, and clicking it swaps
            //    #blurredImg's src to the real QR asynchronously — so wait for
            //    IRCTC's own ready signal (#canPayTxt "Scan & Pay" / #upiQrLoad
            //    "Checking Payment Status" becoming visible) rather than guessing
            //    how long that takes with a fixed delay.
            if (!clickedReveal)
            {
                // Precise match first (the exact element confirmed via live DOM
                // dump: <span onclick="submitUpiQrForm()">), falling back to the
                // broader text search for other gateway variants. A direct DOM
                // .click() (not the coordinate-based click) — reliable regardless
                // of exactly where this renders on the page.
                bool clicked = await ClickDomAsync(@"
  document.querySelector('[onclick*=""submitUpiQrForm""]') ||
  Array.from(document.querySelectorAll('div,span,a,p,button,img'))
    .find(function(e){
       var t=(e.innerText||'')+' '+(e.getAttribute&&e.getAttribute('alt')||'');
       return /click here to pay through qr|click .*qr|tap .*qr|view qr/i.test(t)
              && e.offsetParent!==null;
    })
");
                if (clicked)
                {
                    clickedReveal = true;
                    await WaitForAsync(@"(function(){
  var c = document.querySelector('#canPayTxt'), l = document.querySelector('#upiQrLoad');
  return (c && c.offsetParent!==null) || (l && l.offsetParent!==null);
})()", 10000, pollMs: 300);
                    await InjectAsync();
                }
            }

            // Try, in order: the QR element's own image/canvas source, a
            // screenshot crop of that same element, then (last resort) the
            // largest square-ish image/canvas on the page.
            System.Drawing.Bitmap? candidate = null;

            var src = (await Exec($@"(function(){{
  var el = document.querySelector('{qrSelector.Replace("\"", "\\\"")}');
  if (!el) return '';
  if (el.tagName === 'CANVAS') {{ try {{ return el.toDataURL('image/png'); }} catch(e) {{ return ''; }} }}
  return el.src || '';
}})()")).Trim('"');

            // Known-fake fast path: IRCTC's placeholder image (confirmed via live
            // DOM dump) is literally a static demo asset, "QR_Prakhar_Demo.png" —
            // if the src still points at it, there's no point decoding/analyzing
            // it as a candidate at all.
            bool knownPlaceholder = src.Contains("QR_Prakhar_Demo", StringComparison.OrdinalIgnoreCase);

            if (!knownPlaceholder && src.Length > 30 && src != "null")
            {
                await D(1000);
                candidate = await CaptureQrBitmapAsync(src);
            }
            if (!knownPlaceholder)
            {
                candidate ??= await CropQrFromScreenshotAsync(qrSelector);
                candidate ??= await CropLargestSquareImageAsync();
            }

            if (candidate == null)
            {
                // Still the known placeholder (e.g. the reveal click didn't land,
                // or the ready-signal wait above timed out) — try clicking the QR
                // element itself directly as a recovery, same as the low-contrast
                // fallback below.
                if (knownPlaceholder && !clickedQrElement)
                {
                    Report("Step 10 — Still showing the placeholder QR — clicking it directly...");
                    await ClickDomAsync($"document.querySelector('{qrSelector.Replace("\"", "\\\"")}')");
                    clickedQrElement = true;
                    await D(1500); await InjectAsync();
                }
                continue;
            }

            // Confirmed by direct observation on a live payment page: IRCTC's
            // gateway first shows a flat, low-contrast GREY QR-shaped
            // placeholder — clicking it (not a separate "click here" caption,
            // the QR image itself) reveals the real, high-contrast
            // black-and-white scannable QR. Tell them apart by pixel
            // contrast rather than markup, since a real QR is (by scanning
            // necessity) almost entirely pure black/white with very little
            // mid-tone, while the placeholder is dominated by mid-grey.
            if (LooksLikeRealQr(candidate))
            {
                Report("Step 10 — UPI QR code extracted! Scan to pay.");
                OnQrReady?.Invoke(candidate);
                await MonitorQrUntilGoneAsync(qrSelector);
                return;
            }

            candidate.Dispose();
            if (!clickedQrElement)
            {
                Report("Step 10 — QR looks like a placeholder — clicking it to reveal the real one...");
                await ClickDomAsync($"document.querySelector('{qrSelector.Replace("\"", "\\\"")}')");
                clickedQrElement = true;
                await D(1500); await InjectAsync();
            }
        }
        Report("Step 10 — QR not auto-detected. Scan it in the browser to complete payment.");
    }

    // See the comment above where this is used: distinguishes a real,
    // high-contrast black/white QR from a flat grey placeholder by sampling
    // pixel luminance rather than relying on markup we have no live access
    // to verify.
    private static bool LooksLikeRealQr(System.Drawing.Bitmap bmp)
    {
        try
        {
            int w = bmp.Width, h = bmp.Height;
            if (w < 20 || h < 20) return false;

            int step = Math.Max(1, Math.Min(w, h) / 60); // ~60x60 samples max
            int dark = 0, light = 0, mid = 0, total = 0;
            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    var c = bmp.GetPixel(x, y);
                    int lum = (c.R + c.G + c.B) / 3;
                    if (lum < 70) dark++;
                    else if (lum > 195) light++;
                    else mid++;
                    total++;
                }
            }
            if (total == 0) return false;

            double midFrac   = (double)mid   / total;
            double darkFrac  = (double)dark  / total;
            double lightFrac = (double)light / total;

            // A real QR is high-contrast black/white with very little
            // mid-tone, and has a genuine mix of both dark AND light pixels
            // (a flat grey placeholder is mostly mid-tone; a blank/loading
            // image is mostly one extreme with almost none of the other).
            return midFrac < 0.25 && darkFrac > 0.10 && lightFrac > 0.10;
        }
        catch { return true; } // fail open — don't block display on a detection bug
    }

    // After the real QR is shown, keep a light watch on the page so the
    // popup can be closed automatically once IRCTC's own QR disappears
    // (payment completed, or the gateway moved on) instead of leaving a
    // stale "scan to pay" window open indefinitely.
    private async Task MonitorQrUntilGoneAsync(string qrSelector)
    {
        const int maxChecks = 200; // ~200 * 3s = 10 minutes ≈ typical UPI session validity
        for (int i = 0; i < maxChecks; i++)
        {
            await D(3000);
            bool stillThere;
            try
            {
                await InjectAsync();
                stillThere = await ExecBool(
                    $"!!document.querySelector('{qrSelector.Replace("\"", "\\\"")}')");
            }
            catch { stillThere = true; } // on error, assume nothing changed yet

            if (!stillThere)
            {
                Report("Step 10 — QR is no longer shown on the page (payment likely completed) — closing popup.");
                OnQrGone?.Invoke();
                return;
            }
        }
    }

    // Find the largest near-square <img>/<canvas> on the page — on a UPI gateway
    // that's the QR code — and crop it from a screenshot.
    private async Task<System.Drawing.Bitmap?> CropLargestSquareImageAsync()
    {
        try
        {
            // Only attempt this on an actual payment/QR gateway page.
            bool gateway = await ExecBool(
                @"/scan.*qr|pay through qr|upi|phonepe|paytm|razorpay|billdesk|bharatqr|order id/i
                   .test(document.body.innerText||'')");
            if (!gateway) return null;

            var rectRaw = (await Exec(@"(function(){
  var els = Array.from(document.querySelectorAll('img,canvas,svg'));
  var best=null, bestArea=0;
  for (var e of els){
     var r=e.getBoundingClientRect();
     if (r.width<80 || r.height<80) continue;
     var ratio = r.width/r.height;
     if (ratio<0.8 || ratio>1.25) continue;          // near-square only
     var area=r.width*r.height;
     if (area>bestArea){ bestArea=area; best=e; }
  }
  if(!best) return '';
  best.scrollIntoView({block:'center'});
  var r=best.getBoundingClientRect();
  return JSON.stringify({x:Math.round(r.left),y:Math.round(r.top),
                         w:Math.round(r.width),h:Math.round(r.height),
                         dpr:window.devicePixelRatio||1});
})()")).Trim('"').Replace("\\\"", "\"");

            if (string.IsNullOrEmpty(rectRaw) || !rectRaw.StartsWith("{")) return null;
            await D(400);
            return await CropByRectJsonAsync(rectRaw);
        }
        catch { return null; }
    }

    // Crop ONLY the QR element from a full-page screenshot using its bounding rect
    private async Task<System.Drawing.Bitmap?> CropQrFromScreenshotAsync(string qrSelector)
    {
        try
        {
            var rectRaw = (await Exec($@"(function(){{
  var el = document.querySelector('{qrSelector.Replace("\"", "\\\"")}');
  if (!el) return '';
  el.scrollIntoView({{block:'center'}});
  var r = el.getBoundingClientRect();
  if (r.width < 40 || r.height < 40) return '';
  return JSON.stringify({{x:Math.round(r.left), y:Math.round(r.top),
                          w:Math.round(r.width), h:Math.round(r.height),
                          dpr: window.devicePixelRatio || 1}});
}})()")).Trim('"').Replace("\\\"", "\"");

            if (string.IsNullOrEmpty(rectRaw) || !rectRaw.StartsWith("{")) return null;
            await D(400); // allow scroll to settle
            return await CropByRectJsonAsync(rectRaw);
        }
        catch { return null; }
    }

    // Crop a screenshot to the rectangle described by a JSON {x,y,w,h,dpr} string.
    private async Task<System.Drawing.Bitmap?> CropByRectJsonAsync(string rectJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rectJson);
            var root = doc.RootElement;
            double dpr = root.GetProperty("dpr").GetDouble();
            int x = (int)(root.GetProperty("x").GetDouble() * dpr);
            int y = (int)(root.GetProperty("y").GetDouble() * dpr);
            int w = (int)(root.GetProperty("w").GetDouble() * dpr);
            int h = (int)(root.GetProperty("h").GetDouble() * dpr);

            using var full = await TakeScreenshotAsync();
            if (full == null) return null;

            x = Math.Max(0, Math.Min(x, full.Width  - 1));
            y = Math.Max(0, Math.Min(y, full.Height - 1));
            w = Math.Max(1, Math.Min(w, full.Width  - x));
            h = Math.Max(1, Math.Min(h, full.Height - y));

            var crop = new System.Drawing.Rectangle(x, y, w, h);
            return full.Clone(crop, full.PixelFormat);
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOGIN (used in Step 1 and Step 5)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task LoginAsync(string user, string pass)
    {
        // Open login dialog if not already open. Prefers the exact LOGIN
        // link (confirmed via live DOM), then falls back to the "LOGIN" text
        // search, then — confirmed via a separate live DOM dump — a plain
        // user-icon (<i class="fa fa-user">) with no visible text at all,
        // which the text-based searches above can't match on their own.
        if (!await ExecBool(@"__h.exists('input[placeholder=""User Name""]') || __h.exists('input[formcontrolname=""userid""]')"))
        {
            bool clicked = await ClickDomAsync(@"
  document.querySelector('a[aria-label=""Click here to Login in application""]')");
            if (!clicked) clicked = await ClickText("a,button", "LOGIN");
            if (!clicked)
            {
                await ClickDomAsync(@"(function(){
  var icon = document.querySelector('i.fa-user, .fa.fa-user');
  return icon ? (icon.closest('a,button') || icon) : null;
})()");
            }
            await D(2000); await InjectAsync();
        }

        bool ready = await WaitForAsync(
            @"__h.exists('input[placeholder=""User Name""]') || __h.exists('input[formcontrolname=""userid""]')");
        if (!ready) { Report("Login form not found — open it manually, then 'OK (Continue)'."); await UserAckAsync(); }
        await InjectAsync(); await D(300);

        // Fill username
        foreach (var sel in new[] { "input[placeholder=\"User Name\"]",
                                    "input[formcontrolname=\"userid\"]",
                                    "input[type=\"text\"]" })
            if (await ExecBool($"__h.fill('{sel}','{Esc(user)}')")) break;
        await D(500);

        // Fill password
        foreach (var sel in new[] { "input[placeholder=\"Password\"]",
                                    "input[formcontrolname=\"password\"]",
                                    "input[type=\"password\"]" })
            if (await ExecBool($"__h.fill('{sel}','{Esc(pass)}')")) break;
        await D(500);

        // CAPTCHA on login page? — auto-solve it
        if (await ExecBool("__h.captchaVisible() || __h.pageHas('Enter Captcha')"))
        {
            Report("Login CAPTCHA — auto-solving...");
            await AutoSolveCaptchaAsync();
            await D(400);
        }

        // Click SIGN IN
        await ClickText("button", "SIGN IN");
        await D(3000); await InjectAsync();

        // OTP?
        if (await ExecBool("__h.pageHas('OTP') || __h.exists('input[maxlength=\"6\"]')"))
        {
            Report("OTP required — enter it in the browser, then click 'OK (Continue)'.");
            await UserAckAsync();
            await D(3000); await InjectAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CORE CLICK ENGINE — DevTools Protocol (real physical mouse events)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<bool> ClickAsync(string jsExpr, int pauseAfterScrollMs = 350)
    {
        await InjectAsync();
        var raw = await Exec($"JSON.stringify(__h.rect({JsStr(jsExpr)}))");
        raw = raw.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (raw == "null" || string.IsNullOrWhiteSpace(raw) || raw == "undefined") return false;

        double x, y;
        try
        {
            // Handle possible double-encoded JSON from WebView2
            if (raw.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(raw);
                x = doc.RootElement.GetProperty("x").GetDouble();
                y = doc.RootElement.GetProperty("y").GetDouble();
            }
            else return false;
        }
        catch { return false; }

        await D(pauseAfterScrollMs);

        var cdp = _wv.CoreWebView2;
        await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            $@"{{""type"":""mouseMoved"",  ""x"":{x},""y"":{y},""button"":""none""}}");
        await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            $@"{{""type"":""mousePressed"",""x"":{x},""y"":{y},""button"":""left"",""clickCount"":1}}");
        await D(60);
        await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            $@"{{""type"":""mouseReleased"",""x"":{x},""y"":{y},""button"":""left"",""clickCount"":1}}");
        return true;
    }

    // Direct in-DOM click on the element returned by `jsExpr`. Unlike ClickAsync
    // (which dispatches CDP mouse events at viewport coordinates and can miss when
    // an ad iframe overlays the target, the button sits below the fold, or DPR/zoom
    // shifts the hit point), this calls the element's own .click() plus a full
    // synthetic pointer/mouse sequence ON the element — which Angular (click)
    // handlers respond to reliably. Returns true if an element was found & clicked.
    private async Task<bool> ClickDomAsync(string jsExpr)
    {
        await InjectAsync();
        var ok = await Exec($@"(function(){{
  try {{
    var el = ({jsExpr});
    if (!el) return false;
    // If the match is an inner <span>, click the real button/anchor wrapping it.
    var target = el.closest ? (el.closest('button,a') || el) : el;
    target.scrollIntoView({{block:'center', inline:'nearest'}});

    // SINGLE activation only. A real user click triggers the handler ONCE.
    // We previously dispatched a synthetic 'click' event AND called .click() AND
    // an Enter keypress — three activations, which IRCTC flags as a double-click
    // ('Sorry!! Please Try again', reason #3). Use exactly one .click().
    try {{ target.focus(); }} catch(e) {{}}
    target.click();
    return true;
  }} catch(e) {{ return false; }}
}})()");
        return ok.Trim('"') is "true" or "1";
    }

    // Click first element (from given tags) whose text contains txt
    private Task<bool> ClickText(string tags, string txt)
        => ClickAsync(
            $"Array.from(document.querySelectorAll('{tags}'))" +
            $".find(function(e){{return (e.innerText||'').toUpperCase().includes('{txt.ToUpper().Replace("'", "\\'")}')&&e.offsetParent!==null;}})");

    // PrimeNG p-dropdown options (<li>) only exist in the DOM once the panel
    // is opened (they're rendered into an overlay on demand) — so unlike a
    // native <select>, we must open the dropdown first, wait for its panel,
    // then click the item whose text matches. formControlName is tried first
    // Reads a p-dropdown's currently displayed label text (e.g. "GENERAL",
    // "All Classes") by formcontrolname, so callers can check what's
    // actually selected right now instead of assuming a fixed page default.
    private async Task<string> DropdownLabelAsync(string formControlName)
    {
        var raw = await Exec($@"(function(){{
  var d = document.querySelector('p-dropdown[formcontrolname=""{formControlName}""] .ui-dropdown-label, p-dropdown[formcontrolname=""{formControlName}""] .p-dropdown-label');
  return d ? d.innerText.trim() : '';
}})()");
        return raw.Trim('"');
    }

    // (stable, since it's a literal template attribute); positionalIndex is
    // the fallback rank among all <p-dropdown> elements on the page.
    private async Task<bool> SelectDropdownAsync(string formControlName, int positionalIndex, string keyword)
    {
        var triggerJs = $@"(function(){{
  var host = document.querySelector('p-dropdown[formcontrolname=""{formControlName}""]');
  if (!host) host = document.querySelectorAll('p-dropdown')[{positionalIndex}];
  if (!host) return null;
  return host.querySelector('.ui-dropdown, .p-dropdown') || host;
}})()";
        bool opened = await ClickAsync(triggerJs);
        if (!opened) return false;

        bool panelReady = await WaitForAsync(
            "__h.exists('.p-dropdown-item, li.ui-dropdown-item, .ui-dropdown-panel li, .p-dropdown-panel li')", 2500);
        if (!panelReady) return false;

        var kw = Esc(keyword.ToUpper());
        bool picked = await ClickAsync(
            $"Array.from(document.querySelectorAll('.p-dropdown-item, li.ui-dropdown-item, .ui-dropdown-panel li, .p-dropdown-panel li'))" +
            $".find(function(e){{return (e.innerText||'').toUpperCase().includes('{kw}');}})");
        await D(300);
        return picked;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP-GATE PRIMITIVE
    //  Repeats `action` until JS `verifyJs` returns true. The workflow NEVER
    //  advances past a step until that step's outcome is verified on the page.
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<bool> EnsureAsync(
        string what,
        Func<Task> action,
        string verifyJs,
        int maxAttempts = 6,
        int settleMs = 700,
        bool promptOnFail = true)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await InjectAsync();
            if (await ExecBool(verifyJs)) return true;     // already done

            Report($"{what} — attempt {attempt}/{maxAttempts}...");
            await action();
            await D(settleMs); await InjectAsync();

            if (await ExecBool(verifyJs)) return true;     // verified done
        }

        if (promptOnFail)
        {
            // Genuinely stuck step — pause and let the user help; do NOT advance.
            Report($"{what} could NOT be completed automatically. " +
                   $"Please do it in the browser, then click 'OK (Continue)'.");
            await UserAckAsync();
            await InjectAsync();
            return await ExecBool(verifyJs);
        }

        // Autonomous mode — never prompt; report and let the caller continue.
        Report($"{what} — not verified after {maxAttempts} attempts; continuing.");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  QR CAPTURE HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<System.Drawing.Bitmap?> CaptureQrBitmapAsync(string src)
    {
        try
        {
            byte[] bytes;
            if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                bytes = Convert.FromBase64String(src[(src.IndexOf(',') + 1)..]);
            else { using var http = new System.Net.Http.HttpClient(); bytes = await http.GetByteArrayAsync(src); }
            using var ms = new System.IO.MemoryStream(bytes);
            return new System.Drawing.Bitmap(ms);
        }
        catch { return await TakeScreenshotAsync(); }
    }

    private async Task<System.Drawing.Bitmap?> TakeScreenshotAsync()
    {
        try
        {
            using var ms = new System.IO.MemoryStream();
            await _wv.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            return new System.Drawing.Bitmap(ms);
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILITIES
    // ═══════════════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════════════
    //  CAPTCHA AUTO-SOLVE — Windows OCR (no external service needed)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tries up to <paramref name="maxAttempts"/> times to automatically read
    /// the IRCTC image captcha using Windows OCR and enter the solution.
    /// Falls back to asking the user only if all OCR attempts fail.
    /// </summary>
    // Fully autonomous captcha solver — NEVER asks the user.
    // Reads the captcha, types it, and if rejected, refreshes and retries.
    private async Task AutoSolveCaptchaAsync(int maxAttempts = 8)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await InjectAsync();

            bool visible = await ExecBool(@"__h.captchaVisible()");
            if (!visible) return; // captcha gone → solved/left page

            Report($"Auto-solving CAPTCHA (attempt {attempt}/{maxAttempts})...");

            var text = await OcrCaptchaAsync();

            // Clean common OCR confusions; keep only plausible captcha chars
            text = (text ?? "").Trim();

            if (text.Length < 4 || text.Length > 8)
            {
                Report($"OCR '{text}' implausible — refreshing captcha...");
                await RefreshCaptchaAsync();
                await D(600);
                continue;
            }

            Report($"OCR result: '{text}' — entering...");

            // Type the captcha into the Angular reactive-form input (#captcha).
            // Use the native value setter + per-character key events so Angular's
            // FormControl registers the value (clears ng-pristine).
            await Exec($@"(function(){{
  var inp = document.querySelector(
    'input#captcha, input[formcontrolname=""captcha""], input[name=""captcha""], input[placeholder*=""Captcha""]');
  if (!inp) return;
  var setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  inp.focus();
  inp.dispatchEvent(new Event('focus',{{bubbles:true}}));

  // clear
  setter.call(inp, '');
  inp.dispatchEvent(new Event('input',{{bubbles:true}}));

  // type char-by-char so Angular sees real keystrokes
  var val = '{Esc(text)}';
  for (var i=0;i<val.length;i++){{
    var ch = val[i];
    var cur = val.substring(0,i+1);
    setter.call(inp, cur);
    inp.dispatchEvent(new KeyboardEvent('keydown',{{key:ch,bubbles:true}}));
    inp.dispatchEvent(new Event('input',{{bubbles:true}}));
    inp.dispatchEvent(new KeyboardEvent('keyup',{{key:ch,bubbles:true}}));
  }}
  inp.dispatchEvent(new Event('change',{{bubbles:true}}));
  inp.dispatchEvent(new Event('blur',{{bubbles:true}}));
}})();");
            await D(300);

            // Verify the value actually landed in the input
            var landed = (await Exec(
                @"(document.querySelector('input#captcha,input[formcontrolname=""captcha""],input[name=""captcha""]')||{}).value || ''"))
                .Trim('"');
            if (!string.Equals(landed, text, StringComparison.OrdinalIgnoreCase))
            {
                Report($"Captcha value didn't stick ('{landed}') — retrying...");
                await D(300);
                continue;
            }
            await D(300);

            // Check for rejection. If rejected, refresh + retry automatically.
            bool invalid = await ExecBool(
                "__h.pageHas('Invalid Captcha') || __h.pageHas('invalid captcha') " +
                "|| __h.pageHas('incorrect captcha')");
            if (invalid)
            {
                Report($"Captcha '{text}' rejected — fresh captcha + retry...");
                await RefreshCaptchaAsync();
                await D(700);
                continue;
            }
            return; // accepted (or no error shown yet)
        }

        // Exhausted attempts — leave last guess entered; the caller's page-change
        // verification will keep things moving. NO user prompt.
        Report("Captcha auto-solve attempts exhausted — proceeding with last guess.");
    }

    private async Task<string> OcrCaptchaAsync()
    {
        try
        {
            // ── 1. Get captcha image bytes ────────────────────────────────
            // IRCTC uses <img class=""captcha-img"" src=""data:image/jpg;base64,..."">
            // The base64 src does NOT contain the word 'captcha', so match by class.
            var src = (await Exec(@"(function(){
  var img = document.querySelector(
    'img.captcha-img, .captcha_div img, .captcha_mainDeiv img, img[alt*=""Captcha""], img[src*=""captcha""], img[id*=""captcha""]');
  return img ? img.src : '';
})()")).Trim('"');
            if (string.IsNullOrEmpty(src)) return "";

            byte[] bytes;
            if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                bytes = Convert.FromBase64String(src[(src.IndexOf(',') + 1)..]);
            }
            else
            {
                // Use WebView2 fetch to keep session cookies
                var b64 = (await Exec($@"(async function(){{
  try {{
    var r = await fetch('{src}',{{credentials:'include'}});
    var blob = await r.blob();
    return await new Promise(function(ok){{
      var fr = new FileReader(); fr.onload=function(){{ok(fr.result);}};
      fr.readAsDataURL(blob);
    }});
  }} catch(e) {{ return ''; }}
}})()")).Trim('"');
                if (!b64.Contains(',')) return "";
                bytes = Convert.FromBase64String(b64[(b64.IndexOf(',') + 1)..]);
            }

            using var ms = new System.IO.MemoryStream(bytes);
            using var orig = new System.Drawing.Bitmap(ms);

            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(
                            new Windows.Globalization.Language("en-US"))
                       ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return "";

            // Run OCR on several preprocessing variants; keep the most plausible.
            var candidates = new List<string>();
            foreach (var variant in BuildCaptchaVariants(orig))
            {
                using (variant)
                {
                    using var pngMs = new System.IO.MemoryStream();
                    variant.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
                    pngMs.Position = 0;

                    var ras = pngMs.AsRandomAccessStream();
                    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                    var soft = await decoder.GetSoftwareBitmapAsync();
                    var res  = await engine.RecognizeAsync(soft);

                    var clean = new string(res.Text.Where(char.IsLetterOrDigit).ToArray());
                    if (!string.IsNullOrWhiteSpace(clean)) candidates.Add(clean);
                }
            }

            if (candidates.Count == 0) return "";

            // Prefer a 4-8 char result (typical captcha length); else the longest.
            var best = candidates
                .OrderByDescending(c => (c.Length is >= 4 and <= 8) ? 1 : 0)
                .ThenByDescending(c => c.Length)
                .First();
            return best;
        }
        catch { return ""; }
    }

    // Build a few preprocessing variants: auto-polarity, forced-invert, plain grayscale.
    private static IEnumerable<System.Drawing.Bitmap> BuildCaptchaVariants(System.Drawing.Bitmap orig)
    {
        yield return PreprocessCaptchaImage(orig);            // auto-detect polarity
        yield return PreprocessCaptchaImage(orig, forceInvert: true);
        yield return ScaleGrayscale(orig);                    // no threshold, just upscale+gray
    }

    private static System.Drawing.Bitmap ScaleGrayscale(System.Drawing.Bitmap src)
    {
        const int scale = 4;
        var wide = new System.Drawing.Bitmap(src.Width * scale, src.Height * scale);
        using (var g = System.Drawing.Graphics.FromImage(wide))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, wide.Width, wide.Height);
        }
        for (int x = 0; x < wide.Width; x++)
            for (int y = 0; y < wide.Height; y++)
            {
                var p = wide.GetPixel(x, y);
                int l = (int)(p.R * 0.299 + p.G * 0.587 + p.B * 0.114);
                wide.SetPixel(x, y, System.Drawing.Color.FromArgb(l, l, l));
            }
        return wide;
    }

    // Produce a clean black-text-on-white image for OCR.
    // Auto-detects polarity: IRCTC captchas are often LIGHT text on a DARK
    // background, which must be inverted (Windows OCR expects dark-on-light).
    private static System.Drawing.Bitmap PreprocessCaptchaImage(
        System.Drawing.Bitmap src, bool forceInvert = false)
    {
        const int scale = 4;
        var wide = new System.Drawing.Bitmap(src.Width * scale, src.Height * scale);
        using (var g = System.Drawing.Graphics.FromImage(wide))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.DrawImage(src, 0, 0, wide.Width, wide.Height);
        }

        int w = wide.Width, h = wide.Height;

        // 1) Compute luminance + average to estimate background brightness.
        var lum = new int[w, h];
        long total = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                var p = wide.GetPixel(x, y);
                int l = (int)(p.R * 0.299 + p.G * 0.587 + p.B * 0.114);
                lum[x, y] = l;
                total += l;
            }
        double avg = total / (double)(w * h);

        // If the image is mostly dark, the text is light → invert so text is dark.
        // forceInvert flips whatever the auto-detection decided (used as a 2nd variant).
        bool darkBackground = (avg < 128) ^ forceInvert;

        // 2) Threshold around the mean (Otsu-lite) then output dark-on-white.
        int threshold = (int)avg;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                bool isTextPixel = darkBackground
                    ? lum[x, y] > threshold    // light text on dark bg
                    : lum[x, y] < threshold;   // dark text on light bg
                wide.SetPixel(x, y, isTextPixel
                    ? System.Drawing.Color.Black     // text → black
                    : System.Drawing.Color.White);   // background → white
            }
        return wide;
    }

    private async Task RefreshCaptchaAsync()
    {
        // IRCTC refresh control: <a aria-label="Click to refresh Captcha">
        //                          <span class="glyphicon glyphicon-repeat"></span></a>
        await Exec(@"(function(){
  // 1) the dedicated refresh anchor / glyphicon
  var a = document.querySelector('a[aria-label*=""refresh Captcha""], a[aria-label*=""Refresh Captcha""]');
  if (a) { a.click(); return; }
  var g = document.querySelector('.glyphicon-repeat, .glyphicon-refresh');
  if (g) { (g.closest('a,button')||g).click(); return; }
  // 2) refresh-by-class fallback
  var ref2 = document.querySelector('[class*=""refresh""],[id*=""refresh""]');
  if (ref2) { ref2.click(); return; }
  // 3) clicking the captcha image itself often reloads it
  var img = document.querySelector('img.captcha-img, .captcha_div img');
  if (img) img.click();
})();");
    }

    private async Task NavAsync(string url)
    {
        var tcs = new TaskCompletionSource<bool>();
        void H(object? s, CoreWebView2NavigationCompletedEventArgs e)
        { _wv.CoreWebView2.NavigationCompleted -= H; tcs.TrySetResult(true); }
        _wv.CoreWebView2.NavigationCompleted += H;
        _wv.CoreWebView2.Navigate(url);
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Page load timed out after 45s: {url}");
        }
    }

    private async Task InjectAsync()
    { try { await _wv.CoreWebView2.ExecuteScriptAsync(HelperJs); } catch { } }

    private Task<string> Exec(string js)  => _wv.CoreWebView2.ExecuteScriptAsync(js);
    private async Task<bool> ExecBool(string js) => (await Exec(js)).Trim('"') is "true" or "1";


    private async Task<bool> WaitForAsync(string js, int timeoutMs = 10000, int pollMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { if (await ExecBool(js)) return true; } catch { }
            await D(pollMs);
        }
        return false;
    }

    private static string ClassKeyword(string code) => code.ToUpper() switch
    {
        "SL"=>"Sleeper","3A"=>"3 Tier","2A"=>"2 Tier","1A"=>"First Class",
        "CC"=>"Chair Car","2S"=>"2nd Sitting","3E"=>"Economy","FC"=>"First Class",_=>code,
    };

    private static string QuotaKeyword(string code) => code.ToUpper() switch
    {
        "GN"=>"GENERAL","TQ"=>"TATKAL","PT"=>"PREMIUM TATKAL","LD"=>"LADIES",
        "SS"=>"SENIOR CITIZEN","HP"=>"HANDICAP","DP"=>"DUTY PASS",_=>code,
    };

    private static string MonthNum(string m) => m.ToLower() switch
    {
        "jan"=>"01","feb"=>"02","mar"=>"03","apr"=>"04","may"=>"05","jun"=>"06",
        "jul"=>"07","aug"=>"08","sep"=>"09","oct"=>"10","nov"=>"11","dec"=>"12",_=>m
    };

    private static string Esc(string s)
        => s.Replace("\\","\\\\").Replace("'","\\'").Replace("\n","\\n");

    // Wrap JS expression string for passing to __h.rect()
    private static string JsStr(string expr)
        => "\"" + expr.Replace("\\","\\\\").Replace("\"","\\\"")
                      .Replace("\r\n","\\n").Replace("\n","\\n").Replace("\r","\\n") + "\"";

    private static Task D(int ms) => Task.Delay(ms);
    private void Report(string m) => OnStatus?.Invoke(m);

    public void AcknowledgeUserAction() => _userAckTcs?.TrySetResult(true);
    private Task UserAckAsync()
    {
        _userAckTcs = new TaskCompletionSource<bool>();
        return _userAckTcs.Task.WaitAsync(TimeSpan.FromMinutes(10));
    }
}
