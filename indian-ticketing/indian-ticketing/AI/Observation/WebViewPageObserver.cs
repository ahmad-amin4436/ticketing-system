using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using indian_ticketing.AI.WebsiteAdapters;

namespace indian_ticketing.AI.Observation;

/// <summary>
/// Talks to the live embedded WebView2 browser: turns the current DOM into a normalized
/// <see cref="PageState"/> (element discovery + visibility, capped/prioritized so the
/// prompt sent to a small local model stays cheap), and exposes the low-level physical
/// interaction primitives (click/type/select/check/scroll/key/back) that
/// <c>ActionExecutor</c> drives.
///
/// This intentionally does not reuse <c>IrctcWebViewSession</c>'s code — that file stays
/// untouched so the existing deterministic booking path is never put at risk — but it
/// reuses the same proven techniques: a "real mouse" CDP click as the primary path with a
/// direct DOM <c>.click()</c> fallback for zero-height PrimeNG wrappers, and native
/// value-setter + input/change/blur event dispatch so Angular reactive forms register
/// typed values.
/// </summary>
public sealed class WebViewPageObserver : IPageObserver
{
    private readonly WebView2 _wv;
    private readonly IReadOnlyList<IWebsiteAdapter> _adapters;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public WebViewPageObserver(WebView2 webView, IEnumerable<IWebsiteAdapter>? adapters = null)
    {
        _wv = webView;
        _adapters = (adapters ?? Array.Empty<IWebsiteAdapter>()).ToList();
    }

    // ── injected JS: element discovery + physical interaction ──────────────────────────
    // Parallel to IrctcWebViewSession's window.__h, but generic/site-agnostic — any
    // site-specific selector knowledge is supplied per call, never hardcoded here.
    private const string ObserverJs = @"
window.__aiObs = window.__aiObs || (function(){
  var nextId = 1;

  function visible(el){
    if (!el) return false;
    if (el.getClientRects().length === 0) return false;
    var cs = getComputedStyle(el);
    if (cs.visibility === 'hidden' || cs.display === 'none' || +cs.opacity === 0) return false;
    for (var p = el.parentElement; p; p = p.parentElement){
      if (getComputedStyle(p).display === 'none') return false;
    }
    return true;
  }

  function enabled(el){
    if (el.disabled) return false;
    if (el.getAttribute && el.getAttribute('aria-disabled') === 'true') return false;
    return true;
  }

  function labelFor(el){
    if (el.id){
      try {
        var l = document.querySelector('label[for=""' + CSS.escape(el.id) + '""]');
        if (l && l.innerText && l.innerText.trim()) return l.innerText.trim().slice(0,80);
      } catch(e){}
    }
    var closestLabel = el.closest ? el.closest('label') : null;
    if (closestLabel && closestLabel.innerText && closestLabel.innerText.trim())
      return closestLabel.innerText.trim().slice(0,80);
    var aria = el.getAttribute && el.getAttribute('aria-label');
    if (aria) return aria.slice(0,80);
    var ph = el.getAttribute && el.getAttribute('placeholder');
    if (ph) return ph.slice(0,80);
    var fc = el.getAttribute && el.getAttribute('formcontrolname');
    if (fc) return fc.slice(0,80);
    var txt = (el.innerText || el.value || '').trim();
    if (txt) return txt.slice(0,60);
    return null;
  }

  function roleOf(el){
    var explicitRole = el.getAttribute && el.getAttribute('role');
    if (explicitRole) return explicitRole.toLowerCase();
    var tag = el.tagName.toLowerCase();
    if (tag === 'input'){
      var t = (el.getAttribute('type')||'text').toLowerCase();
      if (t === 'checkbox') return 'checkbox';
      if (t === 'radio') return 'radio';
      if (t === 'submit' || t === 'button') return 'button';
      return 'textbox';
    }
    if (tag === 'textarea') return 'textbox';
    if (tag === 'select') return 'combobox';
    if (tag === 'button') return 'button';
    if (tag === 'a') return 'link';
    return null;
  }

  function valueOf(el){
    var tag = el.tagName.toLowerCase();
    if (tag === 'select'){
      var opt = el.options[el.selectedIndex];
      return opt ? opt.text : '';
    }
    if (tag === 'input' || tag === 'textarea') return el.value || '';
    return null;
  }

  function selectedOf(el){
    var tag = el.tagName.toLowerCase();
    if (tag === 'input'){
      var t = (el.getAttribute('type')||'').toLowerCase();
      if (t === 'checkbox' || t === 'radio') return !!el.checked;
    }
    var aria = el.getAttribute && el.getAttribute('aria-checked');
    if (aria) return aria === 'true';
    return false;
  }

  function collect(extraSelectors, cap){
    cap = cap || 40;
    var baseSel = ['input','button','select','textarea','a[href]',
      '[role=""button""]','[role=""link""]','[role=""checkbox""]','[role=""radio""]',
      '[role=""textbox""]','[role=""combobox""]','[formcontrolname]','[contenteditable=""true""]'];
    var all = baseSel.concat(extraSelectors || []);
    var seen = new Set();
    var nodes = [];
    all.forEach(function(sel){
      try {
        document.querySelectorAll(sel).forEach(function(el){
          if (!seen.has(el)){ seen.add(el); nodes.push(el); }
        });
      } catch(e){}
    });

    var controls = [];
    var actionable = [];
    nodes.forEach(function(el){
      if (!visible(el)) return;
      var tag = el.tagName.toLowerCase();
      var role = roleOf(el);
      var isControl = tag==='input' || tag==='select' || tag==='textarea'
        || ['textbox','combobox','checkbox','radio'].indexOf(role) >= 0
        || el.isContentEditable;
      (isControl ? controls : actionable).push(el);
    });

    var picked = controls.slice(0, Math.ceil(cap*0.6)).concat(actionable.slice(0, Math.floor(cap*0.4)));

    var out = [];
    picked.forEach(function(el){
      if (!el.getAttribute('data-ai-id')) el.setAttribute('data-ai-id', 'e' + (nextId++));
      var id = el.getAttribute('data-ai-id');
      out.push({
        id: id,
        type: el.tagName.toLowerCase(),
        role: roleOf(el),
        text: (el.innerText||'').trim().slice(0,80) || null,
        label: labelFor(el),
        placeholder: el.getAttribute ? el.getAttribute('placeholder') : null,
        value: valueOf(el),
        visible: true,
        enabled: enabled(el),
        selected: selectedOf(el)
      });
    });

    var bodyText = (document.body && document.body.innerText || '').replace(/\s+/g,' ').trim().slice(0,500);
    return JSON.stringify({ elements: out, visibleText: bodyText });
  }

  function rectFor(id){
    var el = document.querySelector('[data-ai-id=""' + id + '""]');
    if (!el) return null;
    el.scrollIntoView({block:'center', inline:'nearest'});
    var r = el.getBoundingClientRect();
    if (!r.width || !r.height) return null;
    return {x: Math.round(r.left + r.width/2), y: Math.round(r.top + r.height/2)};
  }

  function clickDom(id){
    var el = document.querySelector('[data-ai-id=""' + id + '""]');
    if (!el) return false;
    var target = el.closest ? (el.closest('button,a') || el) : el;
    try { target.focus(); } catch(e){}
    target.click();
    return true;
  }

  function fill(id, val){
    var el = document.querySelector('[data-ai-id=""' + id + '""]');
    if (!el) return false;
    el.focus();
    try {
      var proto = el.tagName.toLowerCase()==='textarea' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      var setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
      setter.call(el, val);
    } catch(e) { el.value = val; }
    el.dispatchEvent(new InputEvent('input', {bubbles:true}));
    el.dispatchEvent(new Event('change', {bubbles:true}));
    el.dispatchEvent(new FocusEvent('blur', {bubbles:true}));
    return true;
  }

  function clearVal(id){ return fill(id, ''); }

  function selectOption(id, text){
    var el = document.querySelector('[data-ai-id=""' + id + '""]');
    if (!el) return false;
    if (el.tagName.toLowerCase() === 'select'){
      var opt = Array.from(el.options).find(function(o){
        return (o.text||'').toLowerCase().indexOf((text||'').toLowerCase()) >= 0;
      });
      if (!opt) return false;
      el.value = opt.value;
      el.dispatchEvent(new Event('change', {bubbles:true}));
      return true;
    }
    el.click();
    return true;
  }

  function setChecked(id, want){
    var el = document.querySelector('[data-ai-id=""' + id + '""]');
    if (!el) return false;
    var isChecked = !!el.checked;
    if (isChecked !== want){ el.click(); }
    return true;
  }

  return { visible: visible, collect: collect, rectFor: rectFor, clickDom: clickDom,
           fill: fill, clearVal: clearVal, selectOption: selectOption, setChecked: setChecked };
})();
true;";

    private async Task InjectAsync()
    {
        try { await _wv.CoreWebView2.ExecuteScriptAsync(ObserverJs); } catch { }
    }

    private Task<string> ExecAsync(string js) => _wv.CoreWebView2.ExecuteScriptAsync(js);

    // WebView2's ExecuteScriptAsync JSON-encodes whatever the JS expression evaluates to.
    // A JS string result therefore comes back double-encoded (quoted); a raw JS
    // boolean/number/"null" comes back as-is. This normalizes both cases in one place.
    private static string Unwrap(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
            catch { return raw.Trim('"'); }
        }
        return raw;
    }

    private IEnumerable<string> ExtraSelectorsForCurrentUrl(string url)
    {
        var probe = new PageState { Url = url };
        return _adapters.Where(a => a.CanHandle(probe)).SelectMany(a => a.ExtraElementSelectors).Distinct();
    }

    public async Task<PageState> ObserveAsync(CancellationToken cancellationToken = default)
    {
        if (_wv.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 is not yet initialized.");

        await InjectAsync();
        var url = _wv.CoreWebView2.Source ?? "";
        var title = _wv.CoreWebView2.DocumentTitle ?? "";

        var extra = ExtraSelectorsForCurrentUrl(url);
        var extraJsArray = "[" + string.Join(",", extra.Select(s => JsonSerializer.Serialize(s))) + "]";

        var raw = await ExecAsync($"window.__aiObs.collect({extraJsArray}, 40)");
        var json = Unwrap(raw);

        ObserveDto? dto = null;
        try { dto = JsonSerializer.Deserialize<ObserveDto>(json, JsonOpts); } catch { }

        var state = new PageState
        {
            Url = url,
            Title = title,
            VisibleText = dto?.VisibleText ?? "",
            Elements = (IReadOnlyList<PageElement>?)dto?.Elements ?? Array.Empty<PageElement>(),
            ObservedAt = DateTime.UtcNow,
        };

        foreach (var adapter in _adapters)
            if (adapter.CanHandle(state)) state = adapter.Normalize(state);

        return state;
    }

    // ── low-level interaction, driven by ActionExecutor ─────────────────────────────────
    public async Task<bool> ClickElementAsync(string id, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        var rectRaw = Unwrap(await ExecAsync($"JSON.stringify(window.__aiObs.rectFor('{Escape(id)}'))"));
        if (!string.IsNullOrWhiteSpace(rectRaw) && rectRaw != "null" && _wv.CoreWebView2 is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(rectRaw);
                var x = doc.RootElement.GetProperty("x").GetDouble();
                var y = doc.RootElement.GetProperty("y").GetDouble();
                await Task.Delay(200, cancellationToken);
                var cdp = _wv.CoreWebView2;
                await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    $@"{{""type"":""mouseMoved"",""x"":{x},""y"":{y},""button"":""none""}}");
                await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    $@"{{""type"":""mousePressed"",""x"":{x},""y"":{y},""button"":""left"",""clickCount"":1}}");
                await Task.Delay(60, cancellationToken);
                await cdp.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    $@"{{""type"":""mouseReleased"",""x"":{x},""y"":{y},""button"":""left"",""clickCount"":1}}");
                return true;
            }
            catch { /* fall through to DOM click */ }
        }

        var domOk = Unwrap(await ExecAsync($"window.__aiObs.clickDom('{Escape(id)}')"));
        return domOk is "true";
    }

    public async Task<bool> TypeIntoElementAsync(string id, string text, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        var ok = Unwrap(await ExecAsync($"window.__aiObs.fill('{Escape(id)}', '{Escape(text)}')"));
        return ok is "true";
    }

    public async Task<bool> ClearElementAsync(string id, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        var ok = Unwrap(await ExecAsync($"window.__aiObs.clearVal('{Escape(id)}')"));
        return ok is "true";
    }

    public async Task<bool> SelectOptionAsync(string id, string value, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        var ok = Unwrap(await ExecAsync($"window.__aiObs.selectOption('{Escape(id)}', '{Escape(value)}')"));
        return ok is "true";
    }

    public async Task<bool> SetCheckedAsync(string id, bool check, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        var ok = Unwrap(await ExecAsync($"window.__aiObs.setChecked('{Escape(id)}', {(check ? "true" : "false")})"));
        return ok is "true";
    }

    public async Task ScrollAsync(int amount, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        await ExecAsync($"window.scrollBy(0, {amount});");
    }

    public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        await InjectAsync();
        await ExecAsync($@"(function(){{
  var el = document.activeElement || document.body;
  el.dispatchEvent(new KeyboardEvent('keydown', {{key:'{Escape(key)}', bubbles:true}}));
  el.dispatchEvent(new KeyboardEvent('keyup', {{key:'{Escape(key)}', bubbles:true}}));
}})();");
    }

    public Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        try { _wv.CoreWebView2?.GoBack(); } catch { }
        return Task.CompletedTask;
    }

    public async Task<bool> WaitForConditionAsync(string js, int timeoutMs, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var r = Unwrap(await ExecAsync($"!!({js})"));
                if (r == "true") return true;
            }
            catch { }
            await Task.Delay(300, cancellationToken);
        }
        return false;
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");

    private sealed class ObserveDto
    {
        public List<PageElement>? Elements { get; set; }
        public string? VisibleText { get; set; }
    }
}
