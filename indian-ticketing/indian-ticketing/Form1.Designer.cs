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
        pnlSearch.BackColor = System.Drawing.Color.FromArgb(248, 248, 248);
        pnlSearch.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        pnlSearch.Dock   = System.Windows.Forms.DockStyle.Top;
        pnlSearch.Height = 76;
        pnlSearch.Controls.AddRange(new System.Windows.Forms.Control[]
        {
            lblFromHdr, chkFromOnly, lblToHdr, chkToOnly,
            chkSortDate, chkFirstLast, cmbTrainType, btnGetTrains,
            txtFrom, btnSwap, txtTo, dtpDate, cmbQuota, cmbClass,
            btnSaveTrain, btnBookingMgr
        });

        // ── ROW 1 ────────────────────────────────────────────────────────────
        int y1 = 6;

        // "From" label
        lblFromHdr.AutoSize  = true;
        lblFromHdr.Font      = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        lblFromHdr.Location  = new System.Drawing.Point(8, y1 + 2);
        lblFromHdr.Text      = "From";

        // "From Only" checkbox
        chkFromOnly.AutoSize  = true;
        chkFromOnly.Font      = new System.Drawing.Font("Segoe UI", 8F);
        chkFromOnly.Location  = new System.Drawing.Point(52, y1);
        chkFromOnly.Text      = "Only";

        // "To" label  — aligned with new txtTo x=229
        lblToHdr.AutoSize  = true;
        lblToHdr.Font      = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        lblToHdr.Location  = new System.Drawing.Point(229, y1 + 2);
        lblToHdr.Text      = "To";

        // "To Only" checkbox
        chkToOnly.AutoSize  = true;
        chkToOnly.Font      = new System.Drawing.Font("Segoe UI", 8F);
        chkToOnly.Location  = new System.Drawing.Point(253, y1);
        chkToOnly.Text      = "Only";

        // "Sort on Date" checkbox
        chkSortDate.AutoSize  = true;
        chkSortDate.Checked   = true;
        chkSortDate.Font      = new System.Drawing.Font("Segoe UI", 8F);
        chkSortDate.Location  = new System.Drawing.Point(360, y1);
        chkSortDate.Text      = "Sort on Date";

        // "First/Last Stn Seats" checkbox
        chkFirstLast.AutoSize  = true;
        chkFirstLast.Font      = new System.Drawing.Font("Segoe UI", 8F);
        chkFirstLast.Location  = new System.Drawing.Point(470, y1);
        chkFirstLast.Text      = "First / Last Stn Seats";

        // Train Type combobox
        cmbTrainType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbTrainType.Font     = new System.Drawing.Font("Segoe UI", 8F);
        cmbTrainType.Location = new System.Drawing.Point(625, y1 - 1);
        cmbTrainType.Size     = new System.Drawing.Size(120, 22);
        cmbTrainType.Items.AddRange(new object[]
        {
            "All Train Types", "Super Fast", "Rajdhani", "Sampark Kranti",
            "Garib Rath", "Mail & Express", "Special", "Duranto"
        });
        cmbTrainType.SelectedIndex = 0;

        // "Get Trains" button  — shifted right to x=756
        btnGetTrains.Font     = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        btnGetTrains.Location = new System.Drawing.Point(756, y1 - 2);
        btnGetTrains.Size     = new System.Drawing.Size(100, 52);
        btnGetTrains.Text     = "Get Trains";
        btnGetTrains.UseVisualStyleBackColor = true;
        btnGetTrains.Click   += new System.EventHandler(btnGetTrains_Click);

        // "Save Train" button
        btnSaveTrain.Font     = new System.Drawing.Font("Segoe UI", 8F);
        btnSaveTrain.Location = new System.Drawing.Point(864, y1 - 2);
        btnSaveTrain.Size     = new System.Drawing.Size(90, 24);
        btnSaveTrain.Text     = "Save Train";
        btnSaveTrain.UseVisualStyleBackColor = true;
        btnSaveTrain.Click   += new System.EventHandler(btnSaveTrain_Click);

        // "Booking Manager" button
        btnBookingMgr.Font     = new System.Drawing.Font("Segoe UI", 8F);
        btnBookingMgr.Location = new System.Drawing.Point(864, y1 + 26);
        btnBookingMgr.Size     = new System.Drawing.Size(90, 24);
        btnBookingMgr.Text     = "Booking Mgr";
        btnBookingMgr.UseVisualStyleBackColor = true;
        btnBookingMgr.Click   += new System.EventHandler(btnBookingMgr_Click);

        // ── ROW 2 ────────────────────────────────────────────────────────────
        int y2 = 38;

        // From TextBox  (wider — shows "Station Name (CODE)")
        txtFrom.Font            = new System.Drawing.Font("Segoe UI", 9.5F);
        txtFrom.Location        = new System.Drawing.Point(8, y2);
        txtFrom.Size            = new System.Drawing.Size(185, 24);
        txtFrom.PlaceholderText = "From station or code...";
        txtFrom.KeyDown        += (s, e) =>
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Enter) txtTo.Focus();
        };

        // Swap button
        btnSwap.Font     = new System.Drawing.Font("Segoe UI", 10F);
        btnSwap.Location = new System.Drawing.Point(197, y2);
        btnSwap.Size     = new System.Drawing.Size(28, 24);
        btnSwap.Text     = "⇆";
        btnSwap.UseVisualStyleBackColor = true;
        btnSwap.Click   += new System.EventHandler(btnSwap_Click);

        // To TextBox
        txtTo.Font            = new System.Drawing.Font("Segoe UI", 9.5F);
        txtTo.Location        = new System.Drawing.Point(229, y2);
        txtTo.Size            = new System.Drawing.Size(185, 24);
        txtTo.PlaceholderText = "To station or code...";
        txtTo.KeyDown        += (s, e) =>
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Enter) btnGetTrains.PerformClick();
        };

        // Date picker
        dtpDate.Font         = new System.Drawing.Font("Segoe UI", 8.5F);
        dtpDate.Format       = System.Windows.Forms.DateTimePickerFormat.Custom;
        dtpDate.CustomFormat = "dd-MMM-yy ddd";
        dtpDate.Location     = new System.Drawing.Point(420, y2);
        dtpDate.Size         = new System.Drawing.Size(145, 24);
        dtpDate.Value        = DateTime.Today;
        dtpDate.MinDate      = DateTime.Today;

        // Quota combobox
        cmbQuota.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbQuota.Font     = new System.Drawing.Font("Segoe UI", 8F);
        cmbQuota.Location = new System.Drawing.Point(570, y2);
        cmbQuota.Size     = new System.Drawing.Size(130, 22);
        cmbQuota.Items.AddRange(new object[]
        {
            "Multi Quota", "General Quota", "Tatkal", "Pre.Tatkal",
            "Foreign", "Defence", "Ladies", "Senior Citizens/Lower Berth",
            "Yuva", "Handicapped", "Duty Pass", "Parliament"
        });
        cmbQuota.SelectedIndex = 1;

        // Class filter combobox
        cmbClass.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        cmbClass.Font     = new System.Drawing.Font("Segoe UI", 8F);
        cmbClass.Location = new System.Drawing.Point(704, y2);
        cmbClass.Size     = new System.Drawing.Size(96, 22);
        cmbClass.Items.AddRange(new object[]
        {
            "All Classes", "1A - First AC", "2A - 2Tier AC", "3A - 3Tier AC",
            "CC - Chair Car", "FC - First Class", "SL - Sleeper",
            "2S - Second Sitting", "3E - 3Tier Economy",
            "EV - Vistadome AC", "GN - General"
        });
        cmbClass.SelectedIndex = 0;

        // ── pnlStatus ────────────────────────────────────────────────────────
        pnlStatus.BackColor = System.Drawing.Color.FromArgb(225, 225, 225);
        pnlStatus.Controls.Add(lblStatus);
        pnlStatus.Dock   = System.Windows.Forms.DockStyle.Bottom;
        pnlStatus.Height = 24;

        lblStatus.Dock      = System.Windows.Forms.DockStyle.Fill;
        lblStatus.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
        lblStatus.Padding   = new System.Windows.Forms.Padding(6, 0, 0, 0);
        lblStatus.Text      = "Enter station codes (e.g. NDLS → BCT) and click Get Trains.";
        lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        // ── dgvTrains ─────────────────────────────────────────────────────────
        dgvTrains.AllowUserToAddRows    = false;
        dgvTrains.AllowUserToDeleteRows = false;
        dgvTrains.BackgroundColor       = System.Drawing.Color.White;
        dgvTrains.BorderStyle           = System.Windows.Forms.BorderStyle.None;
        dgvTrains.ColumnHeadersHeightSizeMode =
            System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvTrains.Dock            = System.Windows.Forms.DockStyle.Fill;
        dgvTrains.ReadOnly        = true;
        dgvTrains.RowHeadersVisible = false;
        dgvTrains.SelectionMode   = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        dgvTrains.Font            = new System.Drawing.Font("Segoe UI", 8.5F);
        dgvTrains.GridColor       = System.Drawing.Color.FromArgb(210, 210, 210);
        dgvTrains.ColumnHeadersDefaultCellStyle.Font =
            new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
        dgvTrains.ColumnHeadersDefaultCellStyle.Alignment =
            System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
        dgvTrains.AlternatingRowsDefaultCellStyle.BackColor =
            System.Drawing.Color.FromArgb(245, 248, 255);
        dgvTrains.DefaultCellStyle.Padding =
            new System.Windows.Forms.Padding(2, 0, 2, 0);

        // ── Form1 ─────────────────────────────────────────────────────────────
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
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
