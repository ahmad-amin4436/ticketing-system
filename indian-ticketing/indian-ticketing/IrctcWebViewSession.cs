using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace indian_ticketing;

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

    public event Action<string>? OnStatus;
    public event Action<System.Drawing.Bitmap>? OnQrReady;

    // ── JS helpers ────────────────────────────────────────────────────────
    private const string HelperJs = @"
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

    public IrctcWebViewSession(WebView2 wv) { _wv = wv; }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════
    public async Task RunAsync(SavedBooking booking, string username, string password)
    {
        try
        {
            if (_wv.CoreWebView2 == null) await _wv.EnsureCoreWebView2Async();
            _lastUser = username;
            _lastPass = password;

            // ── Step 1 — Open IRCTC and search (NO login yet) ─────────────
            Report("Step 1 — Opening IRCTC...");
            await NavAsync("https://www.irctc.co.in/nget/train-search");
            await D(4000); await InjectAsync();

            await Step1_SearchAsync(booking);           // search with saved filters

            // ── Steps 2-3-4 — Select train → class → date → Book Now → Yes ─
            await Step2_3_4_SelectTrainClassDateBookAsync(booking);

            // ── Step 5 — Login form appears HERE (after Book Now) ─────────
            await Step5_ReLoginAsync();                 // fill credentials

            // ── Step 6 — Passenger details ────────────────────────────────
            await Step6_PassengersAsync(booking);

            // ── Step 7 — Select BHIM/UPI → Continue ──────────────────────
            await Step7_SelectUpiAsync();

            // ── Step 8 — Review + captcha → Continue ─────────────────────
            await Step8_ReviewAndCaptchaAsync();

            // ── Step 9 — Pay & Book ───────────────────────────────────────
            await Step9_PayAndBookAsync();

            // ── Step 10 — Capture UPI QR ──────────────────────────────────
            await Step10_CaptureQrAsync();
        }
        catch (Exception ex) { Report($"Error: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 1 — Apply saved search filters and click Search
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step1_SearchAsync(SavedBooking b)
    {
        Report($"Step 1 — Searching {b.FromCode} → {b.ToCode} on {b.JourneyDate}...");

        // From station
        await ClickAsync("document.querySelectorAll('p-autocomplete input')[0]");
        await D(300);
        await Exec($"__h.fill('p-autocomplete input', '{Esc(b.FromCode)}')");
        await D(1800);
        await ClickAsync("document.querySelector('.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all')");
        await D(700);

        // To station
        await Exec($@"(function(){{
  var inp = document.querySelectorAll('p-autocomplete input')[1];
  if (!inp) return;
  inp.focus();
  var s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(inp,'{Esc(b.ToCode)}');
  inp.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
}})();");
        await D(1800);
        await ClickAsync("document.querySelector('.ui-autocomplete-list-item, li.p-autocomplete-item')");
        await D(700);

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
        await D(600);

        // Class filter (if available)
        if (!string.IsNullOrEmpty(b.TravelClass) && b.TravelClass != "All Classes")
        {
            await ClickAsync(
                $"Array.from(document.querySelectorAll('p-dropdown option, p-dropdown li, select option'))" +
                $".find(function(e){{return (e.innerText||'').includes('{b.TravelClass}');}})");
            await D(400);
        }

        // Click Search
        Report("Step 1 — Clicking Search Trains...");
        await ClickText("button", "SEARCH");
        await D(7000); await InjectAsync();
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

        // Step 3a — click the class availability box (shows "Refresh ↻")
        var kw   = ClassKeyword(b.TravelClass).ToUpper();
        var code = b.TravelClass.ToUpper();
        Report($"Step 3 — Clicking class [{code}]...");

        bool classClicked = await ClickAsync(
            $"Array.from(document.querySelectorAll('div,span,td'))" +
            $".filter(function(e){{" +
            $"  var t=(e.innerText||'').toUpperCase().trim();" +
            $"  return (t.includes('{kw}')||t.includes('{code}'))" +
            $"      && t.includes('REFRESH') && e.offsetHeight>0 && e.offsetHeight<150;" +
            $"}}).sort(function(a,b){{return a.offsetHeight-b.offsetHeight;}})[0]");

        if (!classClicked)
        {
            Report($"Class box not clickable — click '{code}' manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }

        // Step 3b — wait for availability data to load (WL/AVAIL/DEPARTED appears)
        Report("Step 3 — Waiting for availability dates to load...");
        bool avlReady = await WaitForAsync(@"(function(){
  return Array.from(document.querySelectorAll('div,span,td'))
    .some(function(e){
      var t=(e.innerText||'').toUpperCase().trim();
      return (t.startsWith('AVAIL')||t.startsWith('WL')||t==='TRAIN DEPARTED')
          && e.offsetHeight<60;
    });
})()", 10000);

        if (!avlReady)
        {
            Report("Dates didn't load — click the class box manually, then 'OK (Continue)'.");
            await UserAckAsync();
        }
        await D(400); await InjectAsync();

        // Step 3c — click the saved journey date
        var dp    = b.JourneyDate.Split('-');
        var day   = dp.Length > 0 ? dp[0].TrimStart('0') : "";
        var month = dp.Length > 1 ? dp[1].ToUpper() : "";
        Report($"Step 3 — Selecting date {day} {month}...");

        bool dateClicked = await ClickAsync(
            $"Array.from(document.querySelectorAll('div,span,td,li'))" +
            $".filter(function(e){{" +
            $"  var t=(e.innerText||'').trim();" +
            $"  return t.length<80 && t.includes('{day}')" +
            $"      && t.toUpperCase().includes('{month}')" +
            $"      && !t.includes('DEPARTED')" +
            $"      && e.offsetHeight>0 && e.offsetHeight<100;" +
            $"}}).sort(function(a,b){{return a.offsetHeight-b.offsetHeight;}})[0]");

        if (!dateClicked)
        {
            Report($"Date not found — click {day} {month} manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }

        // Step 3d — wait for Book Now to become enabled
        Report("Step 3 — Waiting for Book Now to enable...");
        await WaitForAsync(
            "Array.from(document.querySelectorAll('button')).some(function(b){return b.innerText.trim().toUpperCase()==='BOOK NOW'&&!b.disabled;})",
            8000);
        await D(600); await InjectAsync();

        // Step 4 — click Book Now
        Report("Step 4 — Clicking Book Now...");
        bool bookClicked = await ClickAsync(
            "Array.from(document.querySelectorAll('button')).find(function(b){return b.innerText.trim().toUpperCase()==='BOOK NOW'&&!b.disabled;})");

        if (!bookClicked)
        {
            Report("Book Now not found — click it manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }
        await D(1500); await InjectAsync();

        // Step 4 — handle date/station confirmation dialog
        bool confirm = await WaitForAsync(
            "__h.pageHas('Do you want to continue') || __h.pageHas('want to continue with')", 5000);
        if (confirm)
        {
            Report("Step 4 — Confirmation dialog: clicking Yes...");
            await ClickText("button", "YES");
            await D(2000); await InjectAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 5 — IRCTC always shows login again after Book Now
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step5_ReLoginAsync()
    {
        Report("Step 5 — Checking for re-login prompt...");
        await D(2000); await InjectAsync();

        bool needLogin = await WaitForAsync(
            @"__h.exists('input[placeholder=""User Name""]') || " +
            @"__h.exists('input[formcontrolname=""userid""]') || " +
            @"__h.pageHas('Please login') || __h.pageHas('Login to proceed')", 6000);

        if (needLogin)
        {
            Report("Step 5 — Re-login required. Logging in automatically...");
            await LoginAsync(_lastUser, _lastPass);
            await D(3000); await InjectAsync();
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
        await D(1500); await InjectAsync();

        for (int i = 0; i < b.Passengers.Count; i++)
        {
            var p = b.Passengers[i];

            if (i > 0)
            {
                await ClickText("a,button", "Add Passenger");
                await D(1000); await InjectAsync();
            }

            // Validate name length (3-16 chars as per IRCTC requirement)
            var name = p.Name.Trim();
            if (name.Length < 3)  name = (name + "   ").Substring(0, 3);
            if (name.Length > 16) name = name.Substring(0, 16);

            Report($"Step 6 — Passenger {i + 1}: {name}, Age {p.Age}...");

            // Name field
            await Exec($@"(function(){{
  var inputs = document.querySelectorAll(
    'input[id*=""passengerName""], input[placeholder*=""Passenger Name""], input[placeholder*=""Name""]');
  var el = inputs[{i}]; if(!el) return;
  var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(el, '{Esc(name)}');
  el.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
  el.dispatchEvent(new Event('change',{{bubbles:true}}));
}})();");
            await D(300);

            // Age field
            await Exec($@"(function(){{
  var inputs = document.querySelectorAll(
    'input[id*=""passengerAge""], input[placeholder*=""Age""]');
  var el = inputs[{i}]; if(!el) return;
  var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(el, '{p.Age}');
  el.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
}})();");
            await D(300);

            // Gender — find the dropdown that contains Male/Female options
            var gLabel = p.Gender == "F" ? "Female" : p.Gender == "T" ? "Transgender" : "Male";
  Report($"Step 6 — Setting gender to {gLabel}...");

     // Step 1: Find and click the gender dropdown trigger for this passenger
     bool gDropClicked = await ClickAsync($@"(function(){{
  var all = Array.from(document.querySelectorAll('p-dropdown, select'));
  var genderDrops = all.filter(function(d){{
    var fc = (d.getAttribute('formcontrolname')||'').toLowerCase();
    var txt = (d.innerText||'').toUpperCase();
    return fc.includes('gender') || txt.includes('MALE') || txt.includes('FEMALE');
  }});
  var drop = genderDrops[{i}];
  if (!drop) return null;
  // Find the trigger element (usually .p-dropdown-trigger or similar)
  var trigger = drop.querySelector('.p-dropdown-trigger, .p-dropdown-label, [role=""button""], .ui-dropdown-trigger');
  return trigger || drop;
}})()");
            await D(600); await InjectAsync();

    // Step 2: Wait for the dropdown menu to appear and then click the option
            bool optionFound = await WaitForAsync(@"(function(){{
  var opts = Array.from(document.querySelectorAll(
    'li[data-label], .p-dropdown-item, li.ui-dropdown-item, option'));
  return opts.some(function(o){{
    var txt = (o.innerText || o.textContent || '').trim().toUpperCase();
    return txt.includes('{gLabel.ToUpper()}');
  }});
}})();", 3000);

   if (optionFound)
          {
          // Click the matching option
     await ClickText("li,option", gLabel);
       await D(800); // Wait for dropdown to close and selection to register
   }
     else
   {
    // Fallback: Try direct value setting on select or p-dropdown
    Report($"Step 6 — Gender dropdown fallback for {gLabel}...");
         await Exec($@"(function(){{
  var all = Array.from(document.querySelectorAll('p-dropdown, select'));
  var gDrops = all.filter(function(d){{
  return (d.getAttribute('formcontrolname')||'').toLowerCase().includes('gender')
        || (d.innerText||'').toUpperCase().includes('MALE');
  }});
  var drop = gDrops[{i}];
  if (!drop) return;
  
  // If it's a native select
  if (drop.tagName === 'SELECT') {{
    var opt = Array.from(drop.options).find(function(o){{
      return (o.textContent||'').trim().toUpperCase().includes('{gLabel.ToUpper()}');
    }});
    if (opt) {{
      drop.value = opt.value;
      drop.dispatchEvent(new Event('change', {{bubbles: true}}));
    }}
    return;
  }}
  
  // If it's a p-dropdown, click it first to open
  var trigger = drop.querySelector('.p-dropdown-trigger, .p-dropdown-label, [role=""button""]');
  if (trigger) trigger.click();
}})();");
     await D(800); await InjectAsync();

        // Now click the option that appears
  await ClickText("li", gLabel);
     await D(800);
            }
            await D(400);
        }

        // Berth preference (optional — scroll to find it)
        await D(500);

        bool paymentModeReady = await WaitForAsync(
            @"__h.exists('input[name=""paymentType""][value=""2""]') || __h.pageHas('Pay through BHIM/UPI')",
            5000);
        if (paymentModeReady)
        {
            bool upiSelected = await SelectBhimUpiPaymentAsync("Step 6");
            if (!upiSelected)
            {
                Report("Step 6 - Could not auto-select BHIM/UPI. Please click the BHIM/UPI radio button manually, then click 'OK (Continue)'.");
                await UserAckAsync();
                await InjectAsync();
            }
            await D(500);
        }

        Report("Step 6 - Clicking Continue...");
        bool contClicked = await ClickPassengerContinueAsync();
        if (!contClicked)
        {
            Report("Step 6 - Continue button not found. Click Continue manually, then 'OK (Continue)'.");
            await UserAckAsync();
            await InjectAsync();
        }
        await D(3000); await InjectAsync();
        
        // Wait for page to transition to next step before proceeding
        Report("Step 6 — Waiting for page to load next step...");
        await WaitForAsync(
            "__h.pageHas('Payment') || __h.pageHas('payment') || __h.pageHas('UPI') || " +
            "__h.pageHas('Review') || __h.pageHas('review') || __h.pageHas('Confirmation')",
          8000);
        await D(2000); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 7 — Select BHIM/UPI payment and click Continue
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step7_SelectUpiAsync()
    {
        Report("Step 7 - Checking whether payment mode still needs selection...");
        await D(1000);
        await InjectAsync();

        bool hasPaymentOptions = await WaitForAsync(
            @"__h.exists('input[name=""paymentType""][value=""2""]') || __h.pageHas('Pay through BHIM/UPI')",
            4000);
        if (!hasPaymentOptions)
        {
            Report("Step 7 - Payment mode was already handled on the passenger details page.");
            return;
        }

        bool upiSelected = await SelectBhimUpiPaymentAsync("Step 7");
        if (!upiSelected)
        {
            Report("Step 7 - Could not auto-select BHIM/UPI. Please click the BHIM/UPI radio button manually, then click 'OK (Continue)'.");
            await UserAckAsync();
            await InjectAsync();
        }

        await D(800);
        await InjectAsync();

        Report("Step 7 - Clicking Continue button...");
        bool contClicked = await ClickPassengerContinueAsync();
        if (!contClicked)
        {
            Report("Step 7 - Continue button not found. Click Continue manually, then 'OK (Continue)'.");
            await UserAckAsync();
            await InjectAsync();
        }

        await D(3000);
        await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 8 — Review page: CAPTCHA must be solved before proceeding
    // ═══════════════════════════════════════════════════════════════════════
    private async Task<bool> SelectBhimUpiPaymentAsync(string stepName)
    {
        Report($"{stepName} - Selecting BHIM/UPI payment mode...");
        await InjectAsync();

        bool selected = await ExecBool(@"(function(){
  function event(el, name) {
    if (!el) return;
    el.dispatchEvent(new Event(name, {bubbles:true, cancelable:true, composed:true}));
  }
  function mouse(el, name) {
    if (!el) return;
    el.dispatchEvent(new MouseEvent(name, {bubbles:true, cancelable:true, composed:true, view:window}));
  }

  var input = document.querySelector('input[name=""paymentType""][value=""2""]');
  if (!input) {
    input = Array.from(document.querySelectorAll('input[type=""radio""]')).find(function(r) {
      var root = r.closest('label,tr,div') || r;
      var text = (root.innerText || root.textContent || '').toUpperCase();
      return text.includes('PAY THROUGH BHIM/UPI') || (text.includes('BHIM') && text.includes('UPI'));
    });
  }

  var widget = input ? input.closest('p-radiobutton') : null;
  var root = widget ? widget.closest('label,tr') : (input ? input.closest('label,tr,div') : null);

  if (!root) {
    root = Array.from(document.querySelectorAll('label,tr,div,span'))
      .filter(function(el) {
        var text = (el.innerText || el.textContent || '').toUpperCase();
        return text.includes('PAY THROUGH BHIM/UPI') || (text.includes('BHIM') && text.includes('UPI'));
      })
      .sort(function(a, b) {
        return (a.innerText || a.textContent || '').length - (b.innerText || b.textContent || '').length;
      })[0];
  }

  if (!root) return null;
  var box = root.querySelector('.ui-radiobutton-box,[role=""radio""]');
  var target = box || widget || input || root;

  try { target.scrollIntoView({block:'center', inline:'nearest'}); } catch(e) {}

  if (input) {
    try {
      var setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'checked').set;
      setter.call(input, true);
    } catch(e) {
      input.checked = true;
    }
    input.setAttribute('checked', 'checked');
  }

  mouse(target, 'pointerdown');
  mouse(target, 'mousedown');
  mouse(target, 'mouseup');
  if (target.click) target.click();

  if (input) {
    if (input.click) input.click();
    event(input, 'input');
    event(input, 'change');
    event(input, 'blur');
  }

  if (box) {
    box.classList.add('ui-state-active');
    box.setAttribute('aria-checked', 'true');
  }

  return !!(input || target);
})()");

        if (!selected)
        {
            selected = await ClickAsync(@"(function(){
  var input = document.querySelector('input[name=""paymentType""][value=""2""]');
  var widget = input ? input.closest('p-radiobutton') : null;
  var root = widget ? widget.closest('label,tr') : null;
  return root ? (root.querySelector('.ui-radiobutton-box,[role=""radio""]') || root) : null;
})()");
        }

        if (!selected) return false;

        await D(800);
        await InjectAsync();
        return await ExecBool(@"(function(){
  var input = document.querySelector('input[name=""paymentType""][value=""2""]');
  if (input && input.checked) return true;
  var root = input ? input.closest('label,tr') : null;
  if (!root) return false;
  var box = root.querySelector('.ui-radiobutton-box,[role=""radio""]');
  return !!(box && (box.classList.contains('ui-state-active') || box.getAttribute('aria-checked') === 'true'));
})()") || selected;
    }

    private async Task<bool> ClickPassengerContinueAsync()
    {
        await InjectAsync();

        bool domClicked = await ExecBool(@"(function(){
  function usable(el) {
    if (!el || el.disabled) return false;
    var text = (el.innerText || el.textContent || '').toUpperCase().trim();
    if (!text.includes('CONTINUE') || text.includes('BOOKING')) return false;
    var style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden') return false;
    var rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }
  function fire(el, name) {
    el.dispatchEvent(new MouseEvent(name, {bubbles:true, cancelable:true, composed:true, view:window}));
  }

  var all = Array.from(document.querySelectorAll('button,input[type=""submit""],a,[role=""button""],span,div'));
  var candidates = all.filter(usable).sort(function(a, b) {
    var ar = a.getBoundingClientRect();
    var br = b.getBoundingClientRect();
    var as = ((a.className || '') + ' ' + (a.type || '')).toLowerCase();
    var bs = ((b.className || '') + ' ' + (b.type || '')).toLowerCase();
    var aw = (as.includes('mob-bot-btn') || as.includes('search_btn') || as.includes('train_search') || as.includes('btndefault') || a.type === 'submit') ? 10000 : 0;
    var bw = (bs.includes('mob-bot-btn') || bs.includes('search_btn') || bs.includes('train_search') || bs.includes('btndefault') || b.type === 'submit') ? 10000 : 0;
    return (bw + br.bottom) - (aw + ar.bottom);
  });

  var target = candidates[0];
  if (!target) {
    target = Array.from(document.querySelectorAll('button[type=""submit""],input[type=""submit""]'))
      .filter(function(el) { return !el.disabled; })
      .sort(function(a, b) { return b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom; })[0];
  }
  if (!target) return false;

  var button = target.closest('button,a,[role=""button""]') || target;
  try { button.scrollIntoView({block:'center', inline:'nearest'}); } catch(e) {}
  fire(button, 'pointerdown');
  fire(button, 'mousedown');
  fire(button, 'mouseup');
  if (button.click) button.click();

  var form = button.closest('form') || document.querySelector('app-passenger-input form, form');
  if (form && button.tagName === 'BUTTON' && button.type === 'submit') {
    try { form.requestSubmit(button); }
    catch(e) { form.dispatchEvent(new Event('submit', {bubbles:true, cancelable:true})); }
  }
  return true;
})()");

        if (domClicked) return true;

        return await ClickAsync(@"(function(){
  var buttons = Array.from(document.querySelectorAll('button,input[type=""submit""],a,[role=""button""]'));
  return buttons.filter(function(b) {
    var text = (b.innerText || b.textContent || b.value || '').toUpperCase().trim();
    return text.includes('CONTINUE') && !text.includes('BOOKING') && !b.disabled;
  }).sort(function(a, b) {
    return b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom;
  })[0] || null;
})()");
    }

    private async Task Step8_ReviewAndCaptchaAsync()
    {
        Report("Step 8 - Waiting for review / captcha page...");
        bool reviewReady = await WaitForAsync(
            @"__h.pageHas('Review Journey') || " +
            @"__h.pageHas('Journey Details') || " +
            @"__h.pageHas('Booking Details') || " +
            @"__h.pageHas('Total Fare') || " +
            @"__h.exists('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""],input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""]') || " +
            @"__h.pageHas('Captcha')",
            30000);

        if (!reviewReady)
        {
            Report("Step 8 - Review page did not appear yet. Waiting for user confirmation to continue.");
            await UserAckAsync();
        }

        await D(1000); await InjectAsync();

        // Wait for captcha image to appear (up to 15 s)
        bool hasCaptcha = await WaitForAsync(
            @"__h.exists('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""]') || " +
            @"__h.exists('input[id*=""captcha""]') || " +
            @"__h.exists('input[name*=""captcha""]') || " +
            @"__h.exists('input[formcontrolname*=""captcha""]') || " +
            @"__h.exists('input[placeholder*=""Captcha""]') || " +
            @"__h.pageHas('Enter Captcha') || " +
            @"__h.pageHas('Captcha')",
            15000);

        if (hasCaptcha)
        {
            await Exec(@"(function(){
  var el = document.querySelector('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""],input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""]');
  if(el) el.scrollIntoView({block:'center'});
})();");

            Report("Step 8 - CAPTCHA visible. Solving automatically...");
            await AutoSolveCaptchaAsync();
            await D(800); await InjectAsync();
        }
        else
        {
            Report("Step 8 - No captcha detected. Proceeding...");
        }

        // Click Continue (after captcha is solved)
        Report("Step 8 - Clicking Continue...");
        bool cont = await ClickPassengerContinueAsync();
        if (!cont) cont = await ClickText("button,a", "CONTINUE");
        if (!cont) cont = await ClickText("button,a", "Continue");
        if (!cont)
        {
            Report("Step 8 - Continue not found - click it manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }
        await D(3000); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 9 — Pay & Book (captcha already solved in Step 8)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step9_PayAndBookAsync()
    {
        Report("Step 9 — Clicking Pay & Book...");
        await D(1500); await InjectAsync();

        // One more captcha check — some IRCTC flows show it right before Pay & Book
        bool lateCaptcha = await WaitForAsync(
            @"__h.exists('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""]') || " +
            @"__h.exists('input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""]')",
            4000);
        if (lateCaptcha)
        {
            await Exec(@"(function(){
  var el = document.querySelector('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""],input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""]');
  if(el) el.scrollIntoView({block:'center'});
})();");
            Report("Step 9 - CAPTCHA on payment page. Solving automatically...");
            await AutoSolveCaptchaAsync();
            await D(500); await InjectAsync();
        }

        bool payClicked = await ClickText("button,a", "Pay & Book");
        if (!payClicked) payClicked = await ClickText("button,a", "PAY & BOOK");
        if (!payClicked) payClicked = await ClickText("button,a", "PAY AND BOOK");

        if (!payClicked)
        {
            Report("Step 9 — Pay & Book not found — click it manually, then 'OK (Continue)'.");
            await UserAckAsync(); await InjectAsync();
        }
        await D(5000); await InjectAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STEP 10 — Capture UPI QR and display in app
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step10_CaptureQrAsync()
    {
        Report("Step 10 — Waiting for UPI payment QR code...");

        for (int attempt = 0; attempt < 60; attempt++)
        {
            await D(2000); await InjectAsync();

            // Try to find QR image or canvas
            var src = (await Exec(@"
__h.imgSrc('img[src*=""qr""]')  ||
__h.imgSrc('img[alt*=""QR""]')  ||
__h.imgSrc('img[alt*=""qr""]')  ||
__h.imgSrc('.qr-code img')     ||
__h.imgSrc('[class*=""qr""] img') ||
__h.canvasUrl('canvas.qrcode') ||
__h.canvasUrl('[class*=""qr""] canvas') || ''''")).Trim('"');

            if (src.Length > 10 && src != "null" && src != "''")
            {
                await D(1500); // let QR fully render
                var bmp = await CaptureQrBitmapAsync(src);
                if (bmp != null)
                {
                    Report("Step 10 — UPI QR code ready! Scan to pay.");
                    OnQrReady?.Invoke(bmp);
                    return;
                }
            }

            // If still on review/gateway page, check for "Scan & Pay" text
            if (await ExecBool("__h.pageHas('Scan') || __h.pageHas('scan') || __h.pageHas('QR')"))
            {
                // Take full page screenshot as fallback
                var fullBmp = await TakeScreenshotAsync();
                if (fullBmp != null)
                {
                    Report("Step 10 — Payment page open. QR shown in app (full page capture).");
                    OnQrReady?.Invoke(fullBmp);
                    return;
                }
            }
        }
        Report("Step 10 — Payment page open in browser. Scan QR manually to complete payment.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOGIN (used in Step 1 and Step 5)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task LoginAsync(string user, string pass)
    {
        // Open login dialog if not already open
        if (!await ExecBool(@"__h.exists('input[placeholder=""User Name""]') || __h.exists('input[formcontrolname=""userid""]')"))
        {
            await ClickText("a,button", "LOGIN");
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

        // CAPTCHA on login page?
        if (await ExecBool("__h.exists('img[src*=captcha]') || __h.pageHas('Enter Captcha')"))
        {
            Report("Login CAPTCHA — solve it in the browser, then click 'OK (Continue)'.");
            await UserAckAsync();
        }

        // Click SIGN IN
        await ClickText("button", "SIGN IN");
        await D(5000); await InjectAsync();

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

    // Click first element (from given tags) whose text contains txt
    private Task<bool> ClickText(string tags, string txt)
        => ClickAsync(
            $"Array.from(document.querySelectorAll('{tags}'))" +
            $".find(function(e){{return (e.innerText||'').toUpperCase().includes('{txt.ToUpper().Replace("'", "\\'")}')&&e.offsetParent!==null;}})");

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
    private async Task AutoSolveCaptchaAsync(int maxAttempts = 3)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await InjectAsync();

            // Check captcha is actually visible
            bool visible = await ExecBool(
                @"__h.exists('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""],canvas') || " +
                @"__h.exists('input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""],input[placeholder*=""captcha""]') || " +
                @"__h.pageHas('Captcha')");
            if (!visible) return; // no captcha on this page

            Report($"Auto-solving CAPTCHA (attempt {attempt}/{maxAttempts})...");

            var text = await OcrCaptchaAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                Report($"OCR attempt {attempt} got no text — retrying...");
                await RefreshCaptchaAsync();
                await D(1000);
                continue;
            }

            Report($"OCR result: '{text}' — entering...");

            // Enter the captcha text
            await Exec($@"(function(){{
  var inp = document.querySelector(
    'input[id*=""captcha""],input[name*=""captcha""],input[formcontrolname*=""captcha""],input[placeholder*=""Captcha""],input[placeholder*=""captcha""]');
  if (!inp) return;
  var s = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(inp, '{Esc(text)}');
  inp.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
  inp.dispatchEvent(new Event('change',{{bubbles:true}}));
  inp.dispatchEvent(new FocusEvent('blur',{{bubbles:true}}));
}})();");
            await D(400);

            // If this is not the last attempt, verify no "invalid" error
            if (attempt < maxAttempts)
            {
                await D(800);
                bool invalid = await ExecBool("__h.pageHas('Invalid Captcha') || __h.pageHas('invalid captcha')");
                if (invalid)
                {
                    Report($"Captcha '{text}' rejected — getting fresh captcha...");
                    await RefreshCaptchaAsync();
                    await D(1200);
                    continue;
                }
            }
            return; // solution entered
        }

        // All OCR attempts failed — ask user
        Report("Auto-solve failed — please type the captcha in the browser, then click 'OK (Continue)'.");
        await UserAckAsync();
    }

    private async Task<string> OcrCaptchaAsync()
    {
        try
        {
            // ── 1. Get captcha image bytes ────────────────────────────────
            var src = (await Exec(@"(function(){
  var img = document.querySelector('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""]')
    || Array.from(document.images).find(function(i) {
      var text = ((i.id || '') + ' ' + (i.alt || '') + ' ' + (i.className || '') + ' ' + (i.src || '')).toLowerCase();
      var r = i.getBoundingClientRect();
      return (text.includes('captcha') || (r.width >= 80 && r.width <= 400 && r.height >= 25 && r.height <= 150))
        && r.width > 0 && r.height > 0;
    });
  if (img) return img.src;
  var canvas = document.querySelector('canvas');
  return canvas ? canvas.toDataURL() : '';
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

            // ── 2. Preprocess: scale up 3x, grayscale, threshold ─────────
            using var ms = new System.IO.MemoryStream(bytes);
            using var orig = new System.Drawing.Bitmap(ms);
            using var processed = PreprocessCaptchaImage(orig);

            // ── 3. Convert to WinRT SoftwareBitmap ────────────────────────
            using var pngMs = new System.IO.MemoryStream();
            processed.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            pngMs.Position = 0;

            var ras = pngMs.AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            var softBitmap = await decoder.GetSoftwareBitmapAsync();

            // ── 4. Run Windows OCR ────────────────────────────────────────
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(
                            new Windows.Globalization.Language("en-US"))
                       ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return "";

            var ocrResult = await engine.RecognizeAsync(softBitmap);

            // ── 5. Extract only alphanumeric chars ────────────────────────
            var clean = new string(ocrResult.Text.Where(char.IsLetterOrDigit).ToArray()).ToUpper();
            return clean;
        }
        catch { return ""; }
    }

    private static System.Drawing.Bitmap PreprocessCaptchaImage(System.Drawing.Bitmap src)
    {
        const int scale = 3;
        var wide = new System.Drawing.Bitmap(src.Width * scale, src.Height * scale);
        using (var g = System.Drawing.Graphics.FromImage(wide))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, wide.Width, wide.Height);
        }
        // Grayscale + threshold (binarise at 140)
        for (int x = 0; x < wide.Width; x++)
            for (int y = 0; y < wide.Height; y++)
            {
                var p   = wide.GetPixel(x, y);
                var lum = (int)(p.R * 0.299 + p.G * 0.587 + p.B * 0.114);
                wide.SetPixel(x, y, lum < 140
                    ? System.Drawing.Color.Black
                    : System.Drawing.Color.White);
            }
        return wide;
    }

    private async Task RefreshCaptchaAsync()
    {
        // Click captcha image or a refresh icon to get a new one
        await Exec(@"(function(){
  var img = document.querySelector('img[src*=""captcha""],img[id*=""captcha""],img[alt*=""captcha""]');
  if (img) img.click();
  var ref2 = document.querySelector('[class*=""refresh""],[id*=""refresh""],[title*=""refresh""],[aria-label*=""refresh""]');
  if (ref2) ref2.click();
})();");
    }

    private Task NavAsync(string url)
    {
        var tcs = new TaskCompletionSource<bool>();
        void H(object? s, CoreWebView2NavigationCompletedEventArgs e)
        { _wv.CoreWebView2.NavigationCompleted -= H; tcs.TrySetResult(true); }
        _wv.CoreWebView2.NavigationCompleted += H;
        _wv.CoreWebView2.Navigate(url);
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private async Task InjectAsync()
    { try { await _wv.CoreWebView2.ExecuteScriptAsync(HelperJs); } catch { } }

    private Task<string> Exec(string js)  => _wv.CoreWebView2.ExecuteScriptAsync(js);
    private async Task<bool> ExecBool(string js) => (await Exec(js)).Trim('"') is "true" or "1";


    private async Task<bool> WaitForAsync(string js, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { if (await ExecBool(js)) return true; } catch { }
            await D(500);
        }
        return false;
    }

    private static string ClassKeyword(string code) => code.ToUpper() switch
    {
        "SL"=>"Sleeper","3A"=>"3 Tier","2A"=>"2 Tier","1A"=>"First Class",
        "CC"=>"Chair Car","2S"=>"2nd Sitting","3E"=>"Economy","FC"=>"First Class",_=>code,
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
        => "\"" + expr.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n") + "\"";

    private static Task D(int ms) => Task.Delay(ms);
    private void Report(string m) => OnStatus?.Invoke(m);

    public void AcknowledgeUserAction() => _userAckTcs?.TrySetResult(true);
    private Task UserAckAsync()
    {
        _userAckTcs = new TaskCompletionSource<bool>();
        return _userAckTcs.Task.WaitAsync(TimeSpan.FromMinutes(10));
    }
}
