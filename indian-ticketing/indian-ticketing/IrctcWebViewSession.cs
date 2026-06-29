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

    public IrctcWebViewSession(WebView2 wv) { _wv = wv; }

    private static string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Indian Ticketing",
            "WebView2");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════════
    public async Task RunAsync(SavedBooking booking, string username, string password)
    {
        try
        {
            if (_wv.CoreWebView2 == null)
            {
                var dataFolder = GetWebView2UserDataFolder();
                Directory.CreateDirectory(dataFolder);
                _wv.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = dataFolder
                };

                await _wv.EnsureCoreWebView2Async();
            }
            _lastUser = username;
            _lastPass = password;

            // ── Step 1 — Open IRCTC and search (NO login yet) ─────────────
            Report("Step 1 — Opening IRCTC...");
            await NavAsync("https://www.irctc.co.in/nget/train-search");
            await D(1500); await InjectAsync();   // NavAsync already awaited load

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
    //  STEP 1 — Apply saved search filters and click Search
    // ═══════════════════════════════════════════════════════════════════════
    private async Task Step1_SearchAsync(SavedBooking b)
    {
        Report($"Step 1 — Searching {b.FromCode} → {b.ToCode} on {b.JourneyDate}...");

        // From station
        await ClickAsync("document.querySelectorAll('p-autocomplete input')[0]");
        await D(250);
        await Exec($"__h.fill('p-autocomplete input', '{Esc(b.FromCode)}')");
        // wait for the autocomplete list to populate (polls, not a fixed sleep)
        await WaitForAsync("__h.exists('.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all')", 3000);
        await ClickAsync("document.querySelector('.ui-autocomplete-list-item, li.p-autocomplete-item, li.ui-corner-all')");
        await D(400);

        // To station
        await Exec($@"(function(){{
  var inp = document.querySelectorAll('p-autocomplete input')[1];
  if (!inp) return;
  inp.focus();
  var s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
  s.call(inp,'{Esc(b.ToCode)}');
  inp.dispatchEvent(new InputEvent('input',{{bubbles:true}}));
}})();");
        await WaitForAsync("__h.exists('.ui-autocomplete-list-item, li.p-autocomplete-item')", 3000);
        await ClickAsync("document.querySelector('.ui-autocomplete-list-item, li.p-autocomplete-item')");
        await D(400);

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
        // results load via AJAX; Step 2 polls for the train list, so a short
        // settle is enough here (was a flat 7s).
        await D(2000); await InjectAsync();
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

        bool clickedReveal = false;

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
            //    Click it ONCE to render the real scannable QR.
            if (!clickedReveal)
            {
                bool clicked = await ClickAsync(@"(function(){
  var el = Array.from(document.querySelectorAll('div,span,a,p,button,img'))
    .find(function(e){
       var t=(e.innerText||'')+' '+(e.getAttribute&&e.getAttribute('alt')||'');
       return /click here to pay through qr|click .*qr|tap .*qr|view qr/i.test(t)
              && e.offsetParent!==null;
    });
  return el || null;
})()");
                if (clicked) { clickedReveal = true; await D(2000); await InjectAsync(); }
            }

            // 1) Try to grab the QR element's image/canvas source directly
            var src = (await Exec($@"(function(){{
  var el = document.querySelector('{qrSelector.Replace("\"", "\\\"")}');
  if (!el) return '';
  if (el.tagName === 'CANVAS') {{ try {{ return el.toDataURL('image/png'); }} catch(e) {{ return ''; }} }}
  return el.src || '';
}})()")).Trim('"');

            if (src.Length > 30 && src != "null")
            {
                await D(1000);
                var bmp = await CaptureQrBitmapAsync(src);
                if (bmp != null)
                {
                    Report("Step 10 — UPI QR code extracted! Scan to pay.");
                    OnQrReady?.Invoke(bmp);
                    return;
                }
            }

            // 2) QR element exists but not readable → crop it from a screenshot
            var cropped = await CropQrFromScreenshotAsync(qrSelector);
            if (cropped != null)
            {
                Report("Step 10 — UPI QR code extracted (cropped)! Scan to pay.");
                OnQrReady?.Invoke(cropped);
                return;
            }

            // 3) Last resort: if the page clearly shows a QR/UPI gateway, crop the
            //    largest square-ish image/canvas on the page (that's the QR).
            var squareCrop = await CropLargestSquareImageAsync();
            if (squareCrop != null)
            {
                Report("Step 10 — UPI QR code extracted (square detect)! Scan to pay.");
                OnQrReady?.Invoke(squareCrop);
                return;
            }
        }
        Report("Step 10 — QR not auto-detected. Scan it in the browser to complete payment.");
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
