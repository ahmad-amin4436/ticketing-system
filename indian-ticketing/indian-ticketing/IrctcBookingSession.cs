using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Drawing;
using SeleniumKeys = OpenQA.Selenium.Keys;

namespace indian_ticketing;

public enum BookingStatus
{
    Idle, Starting, LoggingIn, AwaitingOtp,
    Searching, SelectingTrain, FillingPassengers,
    AwaitingCaptcha, Paying, QrReady, Done, Failed
}

public class IrctcBookingSession : IDisposable
{
    private ChromeDriver? _driver;
    private bool _disposed;

    public string       BookingId { get; }
    public SavedBooking Booking   { get; }

    public BookingStatus         Status   { get; private set; } = BookingStatus.Idle;
    public event Action<string>? OnStatus;
    public event Action<Bitmap>? OnQrReady;

    // ── public ───────────────────────────────────────────────────────────────
    public IrctcBookingSession(SavedBooking booking)
    {
        BookingId = booking.Id;
        Booking   = booking;
    }

    public Task StartAsync(string username, string password)
        => Task.Run(() => Run(username, password));

    // ── private session logic ─────────────────────────────────────────────
    private void Run(string username, string password)
    {
        try
        {
            Report(BookingStatus.Starting, "Opening browser...");
            _driver = CreateDriver();

            Report(BookingStatus.LoggingIn, "Navigating to IRCTC...");
            _driver.Navigate().GoToUrl("https://www.irctc.co.in/nget/train-search");
            Pause(3000);

            // Dismiss any cookie/popup banner
            TryClick("button.cookiesBtn, .close, [aria-label='close']");
            Pause(1000);

            Login(username, password);
            if (Status == BookingStatus.Failed) return;

            Pause(3000);
            SearchTrain();
            if (Status == BookingStatus.Failed) return;

            SelectTrain();
            if (Status == BookingStatus.Failed) return;

            FillPassengers();
            if (Status == BookingStatus.Failed) return;

            WatchForQr();
        }
        catch (Exception ex)
        {
            Report(BookingStatus.Failed, $"Error: {ex.Message}");
        }
    }

    // ── step: login ───────────────────────────────────────────────────────
    private void Login(string username, string password)
    {
        Report(BookingStatus.LoggingIn, "Clicking LOGIN...");

        // Try multiple selectors for the login link
        if (!TryClick("a.loginText, a[href*='login'], button.login-btn"))
        {
            // Try finding by text
            TryClickByText("a", "LOGIN");
            TryClickByText("a", "Login");
        }
        Pause(2000);

        // Username
        Report(BookingStatus.LoggingIn, "Entering username...");
        var userField = FindFirst(
            "input[formcontrolname='userid']",
            "input#userid",
            "input[placeholder*='User Name']",
            "input[placeholder*='username']");
        if (userField == null) { Report(BookingStatus.Failed, "Cannot find username field"); return; }
        userField.Clear();
        TypeSlow(userField, username);
        Pause(500);

        // Password
        var passField = FindFirst(
            "input[formcontrolname='password']",
            "input[type='password']",
            "input#password");
        if (passField == null) { Report(BookingStatus.Failed, "Cannot find password field"); return; }
        passField.Clear();
        TypeSlow(passField, password);
        Pause(500);

        // CAPTCHA image shown on login page - alert user
        if (_driver!.FindElements(By.CssSelector("img[src*='captcha'], .captcha-img")).Count > 0)
        {
            Report(BookingStatus.AwaitingCaptcha, "Login CAPTCHA detected — please solve it in the browser window, then click OK in app.");
            WaitForUserAck();
        }

        // Sign In button
        Report(BookingStatus.LoggingIn, "Clicking SIGN IN...");
        if (!TryClick("button.search_btn.train_Search, button[type='submit'].signBtn"))
        {
            TryClickByText("button", "SIGN IN");
            TryClickByText("button", "Sign In");
        }
        Pause(4000);

        // OTP popup?
        if (_driver.FindElements(By.CssSelector("input[maxlength='6'], input[placeholder*='OTP']")).Count > 0 ||
            PageContains("OTP") || PageContains("One Time Password"))
        {
            Report(BookingStatus.AwaitingOtp, "OTP required — enter the OTP in the browser window, then click OK in app.");
            WaitForUserAck();
            Pause(3000);
        }

        // Verify we're logged in
        Pause(3000);
        if (PageContains("Login") && !PageContains("Logout") && !PageContains("My Account"))
        {
            Report(BookingStatus.Failed, "Login failed — check credentials or solve any CAPTCHA/OTP manually, then restart.");
            return;
        }
        Report(BookingStatus.LoggingIn, "Logged in successfully.");
    }

    // ── step: search train ────────────────────────────────────────────────
    private void SearchTrain()
    {
        Report(BookingStatus.Searching, "Navigating to train search...");
        _driver!.Navigate().GoToUrl("https://www.irctc.co.in/nget/train-search");
        Pause(3000);

        // From station
        Report(BookingStatus.Searching, $"Filling From: {Booking.FromCode}...");
        FillStation("from", Booking.FromCode);
        Pause(1500);

        // To station
        Report(BookingStatus.Searching, $"Filling To: {Booking.ToCode}...");
        FillStation("to", Booking.ToCode);
        Pause(1500);

        // Date
        Report(BookingStatus.Searching, $"Setting date: {Booking.JourneyDate}...");
        FillDate();
        Pause(1000);

        // Search button
        Report(BookingStatus.Searching, "Clicking Search...");
        if (!TryClick("button.search_btn.train_Search, button#search"))
            TryClickByText("button", "SEARCH");
        Pause(5000);
    }

    // ── step: select the specific train ──────────────────────────────────
    private void SelectTrain()
    {
        Report(BookingStatus.SelectingTrain, $"Looking for train {Booking.TrainNo}...");
        Pause(3000);

        // Find the row containing our train number
        var rows = _driver!.FindElements(By.CssSelector("app-train-avl-enq, .train-list, .train_avl_container"));
        if (rows.Count == 0)
            rows = _driver.FindElements(By.XPath($"//*[contains(text(),'{Booking.TrainNo}')]/../.."));

        // Try clicking Book Now for our train
        var bookBtn = _driver.FindElements(By.XPath(
            $"//*[contains(text(),'{Booking.TrainNo}')]/ancestor::*[contains(@class,'train')]//button[contains(text(),'Book Now') or contains(text(),'BOOK NOW')]"))
            .FirstOrDefault();

        if (bookBtn == null)
        {
            // Fallback: look for the class availability cell matching our class
            bookBtn = _driver.FindElements(By.XPath(
                $"//*[contains(text(),'{Booking.TrainNo}')]/following::button[contains(text(),'Book')]"))
                .FirstOrDefault();
        }

        if (bookBtn == null)
        {
            Report(BookingStatus.SelectingTrain, $"Train {Booking.TrainNo} not auto-selected. Please click Book Now in the browser, then click OK in app.");
            WaitForUserAck();
        }
        else
        {
            // First click the availability class cell, then Book Now
            TryClickClassCell();
            Pause(1000);
            ScrollAndClick(bookBtn);
            Report(BookingStatus.SelectingTrain, "Clicked Book Now.");
        }
        Pause(4000);
    }

    private void TryClickClassCell()
    {
        var classMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SL"]  = "SLEEPER",  ["2A"]  = "AC 2 TIER", ["3A"] = "AC 3 TIER",
            ["1A"]  = "FIRST AC", ["CC"]  = "CHAIR CAR", ["2S"] = "SECOND SITTING",
            ["3E"]  = "3RD AC ECONOMY",
        };
        if (!classMap.TryGetValue(Booking.TravelClass, out var label)) return;
        var cell = _driver!.FindElements(By.XPath(
            $"//*[contains(text(),'{Booking.TrainNo}')]/following::*[contains(@class,'avl-status') or contains(@class,'class-avl')]"))
            .FirstOrDefault();
        cell?.Click();
    }

    // ── step: fill passenger details ──────────────────────────────────────
    private void FillPassengers()
    {
        Report(BookingStatus.FillingPassengers, "Filling passenger details...");
        Pause(3000);

        for (int i = 0; i < Booking.Passengers.Count; i++)
        {
            var p = Booking.Passengers[i];
            Report(BookingStatus.FillingPassengers, $"Adding passenger {i + 1}: {p.Name}...");

            // If it's not the first passenger, click "Add Passenger"
            if (i > 0)
            {
                if (!TryClick("button.addPassenger, a.addPax"))
                    TryClickByText("a,button", "Add Passenger");
                Pause(1000);
            }

            // Find passenger fields (IRCTC repeats these per passenger)
            var nameFields  = _driver!.FindElements(By.CssSelector("input[id*='passengerName'], input[placeholder*='Passenger Name']"));
            var ageFields   = _driver.FindElements(By.CssSelector("input[id*='passengerAge'], input[placeholder*='Age']"));
            var genSelects  = _driver.FindElements(By.CssSelector("select[id*='gender'], p-dropdown[id*='gender']"));

            int idx = Math.Min(i, nameFields.Count - 1);
            if (idx >= 0 && idx < nameFields.Count)
            {
                nameFields[idx].Clear();
                TypeSlow(nameFields[idx], p.Name);
            }
            if (idx >= 0 && idx < ageFields.Count)
            {
                ageFields[idx].Clear();
                TypeSlow(ageFields[idx], p.Age.ToString());
            }

            // Gender dropdown
            if (idx >= 0 && idx < genSelects.Count)
                SetDropdown(genSelects[idx], p.Gender == "F" ? "Female" : p.Gender == "T" ? "Transgender" : "Male");

            Pause(500);
        }

        // Mobile / contact (may be required)
        var mobileField = FindFirst("input[id*='mobileNumber'], input[placeholder*='Mobile']");
        if (mobileField != null && string.IsNullOrEmpty(mobileField.GetAttribute("value")))
            TypeSlow(mobileField, "9999999999");

        // Click Continue Booking
        Report(BookingStatus.FillingPassengers, "Clicking Continue Booking...");
        if (!TryClick("button.train_Search.btnDefault"))
            TryClickByText("button,a", "Continue Booking");
        Pause(4000);

        // CAPTCHA before payment
        if (_driver!.FindElements(By.CssSelector("img[src*='captcha'], .captcha-img")).Count > 0
            || PageContains("Enter Captcha") || PageContains("captcha"))
        {
            Report(BookingStatus.AwaitingCaptcha, "Booking CAPTCHA detected — please solve it in the browser, then click OK in app.");
            WaitForUserAck();
        }
    }

    // ── step: watch for UPI QR ────────────────────────────────────────────
    private void WatchForQr()
    {
        Report(BookingStatus.Paying, "Waiting for payment page...");

        for (int attempt = 0; attempt < 60; attempt++)
        {
            Pause(2000);
            // Look for UPI / QR elements
            bool hasQr = _driver!.FindElements(By.CssSelector(
                "img[src*='qr'], .qr-code, canvas.qrcode, img[alt*='QR'], img[alt*='qr']")).Count > 0
                || PageContains("QR Code") || PageContains("UPI");

            if (hasQr)
            {
                Pause(1500);  // let QR render fully
                var qrBitmap = CaptureQr();
                if (qrBitmap != null)
                {
                    Report(BookingStatus.QrReady, "UPI QR code ready — scan to pay.");
                    OnQrReady?.Invoke(qrBitmap);
                    return;
                }
            }
        }
        Report(BookingStatus.Paying, "Payment page open in browser — please complete payment.");
    }

    // ── QR capture ────────────────────────────────────────────────────────
    private Bitmap? CaptureQr()
    {
        // First try to find the QR image element directly
        var qrElem = _driver!.FindElements(By.CssSelector(
            "img[src*='qr'], .qr-code img, img[alt*='QR']")).FirstOrDefault();

        if (qrElem != null)
        {
            try
            {
                var bytes  = ((ITakesScreenshot)qrElem).GetScreenshot().AsByteArray;
                using var ms = new System.IO.MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch { }
        }

        // Fallback: full page screenshot, crop top portion
        try
        {
            var ss     = _driver.GetScreenshot();
            var bytes  = ss.AsByteArray;
            using var ms = new System.IO.MemoryStream(bytes);
            var full   = new Bitmap(ms);
            // Return full screenshot if we can't isolate QR
            return full;
        }
        catch { return null; }
    }

    // ── helpers ───────────────────────────────────────────────────────────
    private static ChromeDriver CreateDriver()
    {
        var opts = new ChromeOptions();
        // Visible mode — user can see and interact
        opts.AddArgument("--no-sandbox");
        opts.AddArgument("--disable-gpu");
        opts.AddArgument("--disable-extensions");
        opts.AddArgument("--no-first-run");
        opts.AddArgument("--window-size=1366,768");
        // Mask webdriver detection
        opts.AddArgument("--disable-blink-features=AutomationControlled");
        opts.AddExcludedArgument("enable-automation");
        opts.AddAdditionalOption("useAutomationExtension", false);
        opts.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

        var svc = ChromeDriverService.CreateDefaultService();
        svc.HideCommandPromptWindow = true;

        var driver = new ChromeDriver(svc, opts);
        // Remove navigator.webdriver property
        driver.ExecuteScript(
            "Object.defineProperty(navigator,'webdriver',{get:()=>undefined})");
        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        return driver;
    }

    private void FillStation(string direction, string code)
    {
        // IRCTC uses Angular p-autocomplete; find the right input by proximity to label
        var xpath = direction == "from"
            ? "//label[contains(text(),'From') or contains(text(),'FROM')]/..//input | //p-autocomplete[1]//input"
            : "//label[contains(text(),'To') or contains(text(),'TO')]/..//input | //p-autocomplete[2]//input";

        IWebElement? field = null;
        try { field = _driver!.FindElement(By.XPath(xpath)); } catch { }
        field ??= FindFirst("input[placeholder*='From']", "input[placeholder*='To']");

        if (field == null) return;
        field.Clear();
        field.SendKeys(code);
        Pause(1500);

        // Select first suggestion
        var suggestion = FindFirst(".ui-autocomplete-list-item, li.ui-autocomplete-list-item, .ng-star-inserted li");
        if (suggestion != null) suggestion.Click();
        else field.SendKeys(SeleniumKeys.Return);
        Pause(500);
    }

    private void FillDate()
    {
        // Try p-calendar input
        var dateInput = FindFirst("p-calendar input", "input[placeholder*='Date'], input[name*='date']");
        if (dateInput == null) return;

        // IRCTC date format: dd/mm/yyyy
        var parts = Booking.JourneyDate.Split('-');
        string irctcDate = parts.Length == 3
            ? $"{parts[0]}/{MonthToNum(parts[1])}/{parts[2]}"
            : Booking.JourneyDate;

        // Clear via JS and set value
        _driver!.ExecuteScript("arguments[0].value=''; arguments[0].dispatchEvent(new Event('input'));", dateInput);
        dateInput.SendKeys(irctcDate);
        Pause(500);
        dateInput.SendKeys(SeleniumKeys.Escape);
    }

    private static string MonthToNum(string mon) => mon.ToLower() switch
    {
        "jan" => "01", "feb" => "02", "mar" => "03", "apr" => "04",
        "may" => "05", "jun" => "06", "jul" => "07", "aug" => "08",
        "sep" => "09", "oct" => "10", "nov" => "11", "dec" => "12",
        _     => mon
    };

    private IWebElement? FindFirst(params string[] selectors)
    {
        foreach (var sel in selectors)
            try
            {
                var el = _driver!.FindElement(By.CssSelector(sel));
                if (el.Displayed) return el;
            }
            catch { }
        return null;
    }

    private bool TryClick(string cssSelector)
    {
        foreach (var sel in cssSelector.Split(',').Select(s => s.Trim()))
            try
            {
                var el = _driver!.FindElement(By.CssSelector(sel));
                if (el.Displayed) { ScrollAndClick(el); return true; }
            }
            catch { }
        return false;
    }

    private bool TryClickByText(string tag, string text)
    {
        try
        {
            var el = _driver!.FindElement(By.XPath(
                $"//{tag}[contains(normalize-space(text()),'{text}')]"));
            if (el.Displayed) { ScrollAndClick(el); return true; }
        }
        catch { }
        return false;
    }

    private void ScrollAndClick(IWebElement el)
    {
        _driver!.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", el);
        Pause(300);
        try { el.Click(); }
        catch { _driver.ExecuteScript("arguments[0].click();", el); }
    }

    private void SetDropdown(IWebElement el, string visibleText)
    {
        // Native <select> — find matching <option> by text
        try
        {
            var option = el.FindElement(By.XPath(
                $".//option[contains(normalize-space(text()),'{visibleText}')]"));
            option.Click();
            return;
        }
        catch { }
        // Angular p-dropdown — open then click item
        el.Click();
        Pause(400);
        try
        {
            var item = _driver!.FindElement(By.XPath(
                $"//*[contains(@class,'ui-dropdown-item') or contains(@class,'p-dropdown-item')]" +
                $"[contains(normalize-space(text()),'{visibleText}')]"));
            item.Click();
        }
        catch { }
    }

    private static void TypeSlow(IWebElement el, string text)
    {
        foreach (var ch in text)
        {
            el.SendKeys(ch.ToString());
            Thread.Sleep(30);
        }
    }

    private bool PageContains(string text)
    {
        try { return _driver!.PageSource.Contains(text, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static void Pause(int ms) => Thread.Sleep(ms);

    private void Report(BookingStatus status, string message)
    {
        Status = status;
        OnStatus?.Invoke(message);
    }

    // Pause automation and wait for user to click OK in the BookingManagerForm
    private readonly ManualResetEventSlim _userAck = new(false);
    public void AcknowledgeUserAction() => _userAck.Set();

    private void WaitForUserAck()
    {
        _userAck.Reset();
        _userAck.Wait(TimeSpan.FromMinutes(10));
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        try { _driver?.Quit(); } catch { }
        try { _driver?.Dispose(); } catch { }
        _driver   = null;
        _disposed = true;
    }
}
