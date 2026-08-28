namespace indian_ticketing;

partial class Form1
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // ── controls ─────────────────────────────────────────────────────────
        pnlSearch    = new System.Windows.Forms.Panel();
        pnlSearchBar = new System.Windows.Forms.Panel();
        // row-1 controls
        lblFromHdr   = new System.Windows.Forms.Label();
        chkFromOnly  = new System.Windows.Forms.CheckBox();
        lblToHdr     = new System.Windows.Forms.Label();
        chkToOnly    = new System.Windows.Forms.CheckBox();
        chkSortDate  = new System.Windows.Forms.CheckBox();
        chkFirstLast = new System.Windows.Forms.CheckBox();
        cmbTrainType = new System.Windows.Forms.ComboBox();
        btnGetTrains = new System.Windows.Forms.Button();
        // row-2 controls
        txtFrom      = new System.Windows.Forms.TextBox();
        btnSwap      = new System.Windows.Forms.Button();
        txtTo        = new System.Windows.Forms.TextBox();
        dtpDate      = new System.Windows.Forms.DateTimePicker();
        cmbQuota     = new System.Windows.Forms.ComboBox();
        cmbClass      = new System.Windows.Forms.ComboBox();
        btnSaveTrain  = new System.Windows.Forms.Button();
        btnBookingMgr = new System.Windows.Forms.Button();
        // status + grid
        pnlStatus    = new System.Windows.Forms.Panel();
        lblStatus    = new System.Windows.Forms.Label();
        dgvTrains    = new System.Windows.Forms.DataGridView();

        pnlSearch.SuspendLayout();
        pnlStatus.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvTrains).BeginInit();
        SuspendLayout();

        // ── pnlSearch ────────────────────────────────────────────────────────
        // A plain white search "card" sitting on the app's light-grey
        // background, with a single 1px accent rule along the bottom instead
        // of a hard box border all the way round — reads as one clean panel
        // instead of a boxed-in form control.
        pnlSearch.BackColor = UiTheme.Surface;
        pnlSearch.Dock      = System.Windows.Forms.DockStyle.Top;
        pnlSearch.Height    = 96;
        pnlSearch.Padding   = new System.Windows.Forms.Padding(0, 0, 0, 2);
        pnlSearch.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            lblFromHdr, chkFromOnly, lblToHdr, chkToOnly,
            chkSortDate, chkFirstLast, cmbTrainType, btnGetTrains,
            txtFrom, btnSwap, txtTo, dtpDate, cmbQuota, cmbClass,
            btnSaveTrain, btnBookingMgr, pnlSearchBar
        });

        pnlSearchBar.BackColor = UiTheme.Primary;
        pnlSearchBar.Dock      = System.Windows.Forms.DockStyle.Bottom;
        pnlSearchBar.Height    = 3;

        // ── ROW 1 — labels, filters, and the primary "Get Trains" action ──────
        int y1 = 10;

        lblFromHdr.AutoSize  = true;
        lblFromHdr.Font      = UiTheme.FontLabel;
        lblFromHdr.ForeColor = UiTheme.TextSecondary;
        lblFromHdr.Location  = new System.Drawing.Point(10, y1 + 2);
        lblFromHdr.Text      = "FROM";

        chkFromOnly.AutoSize  = true;
        chkFromOnly.Font      = UiTheme.FontSmall;
        chkFromOnly.ForeColor = UiTheme.TextSecondary;
        chkFromOnly.Location  = new System.Drawing.Point(58, y1);
        chkFromOnly.Text      = "Only";

        lblToHdr.AutoSize  = true;
        lblToHdr.Font      = UiTheme.FontLabel;
        lblToHdr.ForeColor = UiTheme.TextSecondary;
        lblToHdr.Location  = new System.Drawing.Point(231, y1 + 2);
        lblToHdr.Text      = "TO";

        chkToOnly.AutoSize  = true;
        chkToOnly.Font      = UiTheme.FontSmall;
        chkToOnly.ForeColor = UiTheme.TextSecondary;
        chkToOnly.Location  = new System.Drawing.Point(257, y1);
        chkToOnly.Text      = "Only";

        chkSortDate.AutoSize  = true;
        chkSortDate.Checked   = true;
        chkSortDate.Font      = UiTheme.FontSmall;
        chkSortDate.ForeColor = UiTheme.TextSecondary;
        chkSortDate.Location  = new System.Drawing.Point(364, y1);
        chkSortDate.Text      = "Sort on Date";

        chkFirstLast.AutoSize  = true;
        chkFirstLast.Font      = UiTheme.FontSmall;
        chkFirstLast.ForeColor = UiTheme.TextSecondary;
        chkFirstLast.Location  = new System.Drawing.Point(474, y1);
        chkFirstLast.Text      = "First / Last Stn Seats";

        cmbTrainType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbTrainType.Location = new System.Drawing.Point(627, y1 - 1);
        cmbTrainType.Size     = new System.Drawing.Size(120, 24);
        UiTheme.StyleCombo(cmbTrainType);
        cmbTrainType.Items.AddRange(new object[]
        {
            "All Train Types", "Super Fast", "Rajdhani", "Sampark Kranti",
            "Garib Rath", "Mail & Express", "Special", "Duranto"
        });
        cmbTrainType.SelectedIndex = 0;

        // "Get Trains" — the one primary action on this screen, styled to
        // stand out (solid accent fill) rather than blend in with the rest.
        // Spans both rows vertically, so its X has to clear the WIDEST of
        // row 1's cmbTrainType (ends 747) and row 2's cmbClass (ends 808) —
        // not just whichever row it looks aligned with at a glance.
        btnGetTrains.Location = new System.Drawing.Point(822, y1 - 4);
        btnGetTrains.Size     = new System.Drawing.Size(100, 66);
        btnGetTrains.Text     = "Get Trains";
        UiTheme.StylePrimary(btnGetTrains);
        btnGetTrains.Click   += new System.EventHandler(btnGetTrains_Click);

        // "Save Train" / "Booking Manager" — secondary actions, quiet outline.
        // x=930 clears btnGetTrains's true right edge (822+100=922) plus a gap.
        btnSaveTrain.Location = new System.Drawing.Point(930, y1 - 4);
        btnSaveTrain.Size     = new System.Drawing.Size(96, 30);
        btnSaveTrain.Text     = "Save Train";
        UiTheme.StyleSecondary(btnSaveTrain);
        btnSaveTrain.Click   += new System.EventHandler(btnSaveTrain_Click);

        btnBookingMgr.Location = new System.Drawing.Point(930, y1 + 30);
        btnBookingMgr.Size     = new System.Drawing.Size(96, 30);
        btnBookingMgr.Text     = "Booking Mgr";
        UiTheme.StyleSecondary(btnBookingMgr);
        btnBookingMgr.Click   += new System.EventHandler(btnBookingMgr_Click);

        // ── ROW 2 — the actual search fields ───────────────────────────────────
        int y2 = 44;

        txtFrom.Location        = new System.Drawing.Point(10, y2);
        txtFrom.Size            = new System.Drawing.Size(187, 26);
        txtFrom.PlaceholderText = "From station or code...";
        UiTheme.StyleTextBox(txtFrom);
        txtFrom.KeyDown += (s, e) =>
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Enter) txtTo.Focus();
        };

        btnSwap.Font      = new System.Drawing.Font("Segoe UI", 10F);
        btnSwap.Location  = new System.Drawing.Point(199, y2);
        btnSwap.Size      = new System.Drawing.Size(30, 26);
        btnSwap.Text      = "⇆";
        UiTheme.StyleSecondary(btnSwap);
        btnSwap.Click    += new System.EventHandler(btnSwap_Click);

        txtTo.Location        = new System.Drawing.Point(231, y2);
        txtTo.Size            = new System.Drawing.Size(187, 26);
        txtTo.PlaceholderText = "To station or code...";
        UiTheme.StyleTextBox(txtTo);
        txtTo.KeyDown += (s, e) =>
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Enter) btnGetTrains.PerformClick();
        };

        dtpDate.Font         = UiTheme.FontSmall;
        dtpDate.Format       = System.Windows.Forms.DateTimePickerFormat.Custom;
        dtpDate.CustomFormat = "dd-MMM-yy ddd";
        dtpDate.Location     = new System.Drawing.Point(424, y2);
        dtpDate.Size         = new System.Drawing.Size(145, 26);
        dtpDate.Value        = DateTime.Today;
        dtpDate.MinDate      = DateTime.Today;

        cmbQuota.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbQuota.Location = new System.Drawing.Point(576, y2);
        cmbQuota.Size     = new System.Drawing.Size(130, 26);
        UiTheme.StyleCombo(cmbQuota);
        cmbQuota.Items.AddRange(new object[]
        {
            "Multi Quota", "General Quota", "Tatkal", "Pre.Tatkal",
            "Foreign", "Defence", "Ladies", "Senior Citizens/Lower Berth",
            "Yuva", "Handicapped", "Duty Pass", "Parliament"
        });
        cmbQuota.SelectedIndex = 1;

        cmbClass.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbClass.Location = new System.Drawing.Point(712, y2);
        cmbClass.Size     = new System.Drawing.Size(96, 26);
        UiTheme.StyleCombo(cmbClass);
        cmbClass.Items.AddRange(new object[]
        {
            "All Classes", "1A - First AC", "2A - 2Tier AC", "3A - 3Tier AC",
            "CC - Chair Car", "FC - First Class", "SL - Sleeper",
            "2S - Second Sitting", "3E - 3Tier Economy",
            "EV - Vistadome AC", "GN - General"
        });
        cmbClass.SelectedIndex = 0;

        // ── pnlStatus ────────────────────────────────────────────────────────
        pnlStatus.BackColor = UiTheme.Primary;
        pnlStatus.Controls.Add(lblStatus);
        pnlStatus.Dock   = System.Windows.Forms.DockStyle.Bottom;
        pnlStatus.Height = 26;

        lblStatus.Dock      = System.Windows.Forms.DockStyle.Fill;
        lblStatus.Font      = UiTheme.FontSmall;
        lblStatus.ForeColor = UiTheme.TextOnPrimary;
        lblStatus.Padding   = new System.Windows.Forms.Padding(10, 0, 0, 0);
        lblStatus.Text      = "Enter station codes (e.g. NDLS → BCT) and click Get Trains.";
        lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        // ── dgvTrains ─────────────────────────────────────────────────────────
        dgvTrains.AllowUserToAddRows    = false;
        dgvTrains.AllowUserToDeleteRows = false;
        dgvTrains.BackgroundColor       = UiTheme.Background;
        dgvTrains.BorderStyle           = System.Windows.Forms.BorderStyle.None;
        dgvTrains.CellBorderStyle       = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
        dgvTrains.ColumnHeadersHeightSizeMode =
            System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvTrains.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
        dgvTrains.EnableHeadersVisualStyles = false;
        dgvTrains.Dock            = System.Windows.Forms.DockStyle.Fill;
        dgvTrains.ReadOnly        = true;
        dgvTrains.RowHeadersVisible = false;
        dgvTrains.SelectionMode   = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        dgvTrains.Font            = UiTheme.FontBody;
        dgvTrains.RowTemplate.Height = 30;
        dgvTrains.GridColor       = UiTheme.Border;
        dgvTrains.ColumnHeadersDefaultCellStyle.Font =
            new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
        dgvTrains.ColumnHeadersDefaultCellStyle.BackColor    = UiTheme.Primary;
        dgvTrains.ColumnHeadersDefaultCellStyle.ForeColor    = UiTheme.TextOnPrimary;
        dgvTrains.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.Primary;
        dgvTrains.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.TextOnPrimary;
        dgvTrains.ColumnHeadersHeight = 32;
        dgvTrains.ColumnHeadersDefaultCellStyle.Alignment =
            System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
        dgvTrains.DefaultCellStyle.BackColor          = UiTheme.Surface;
        dgvTrains.DefaultCellStyle.ForeColor          = UiTheme.TextPrimary;
        dgvTrains.DefaultCellStyle.SelectionBackColor = UiTheme.PrimaryLight;
        dgvTrains.DefaultCellStyle.SelectionForeColor = UiTheme.TextOnPrimary;
        dgvTrains.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.CardAlt;
        dgvTrains.DefaultCellStyle.Padding =
            new System.Windows.Forms.Padding(4, 0, 4, 0);

        // ── Form1 ─────────────────────────────────────────────────────────────
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
        BackColor           = UiTheme.Background;
        ClientSize          = new System.Drawing.Size(1200, 680);
        Controls.Add(dgvTrains);
        Controls.Add(pnlStatus);
        Controls.Add(pnlSearch);
        MinimumSize   = new System.Drawing.Size(900, 450);
        Name          = "Form1";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Text          = "Indian Railway Train Schedule — erail.in";

        pnlSearch.ResumeLayout(false);
        pnlSearch.PerformLayout();
        pnlStatus.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvTrains).EndInit();
        ResumeLayout(false);
    }

    // ── field declarations ────────────────────────────────────────────────────
    private System.Windows.Forms.Panel            pnlSearch;
    private System.Windows.Forms.Panel            pnlSearchBar;
    private System.Windows.Forms.Label            lblFromHdr;
    private System.Windows.Forms.CheckBox         chkFromOnly;
    private System.Windows.Forms.Label            lblToHdr;
    private System.Windows.Forms.CheckBox         chkToOnly;
    private System.Windows.Forms.CheckBox         chkSortDate;
    private System.Windows.Forms.CheckBox         chkFirstLast;
    private System.Windows.Forms.ComboBox         cmbTrainType;
    private System.Windows.Forms.Button           btnGetTrains;
    private System.Windows.Forms.TextBox          txtFrom;
    private System.Windows.Forms.Button           btnSwap;
    private System.Windows.Forms.TextBox          txtTo;
    private System.Windows.Forms.DateTimePicker   dtpDate;
    private System.Windows.Forms.ComboBox         cmbQuota;
    private System.Windows.Forms.ComboBox         cmbClass;
    private System.Windows.Forms.Button           btnSaveTrain;
    private System.Windows.Forms.Button           btnBookingMgr;
    private System.Windows.Forms.Panel            pnlStatus;
    private System.Windows.Forms.Label            lblStatus;
    private System.Windows.Forms.DataGridView     dgvTrains;
}
