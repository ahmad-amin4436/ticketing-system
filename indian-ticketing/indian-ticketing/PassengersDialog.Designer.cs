namespace indian_ticketing;

partial class PassengersDialog
{
    private System.ComponentModel.IContainer? components = null;
    protected override void Dispose(bool disposing) { if (disposing) components?.Dispose(); base.Dispose(disposing); }

    private void InitializeComponent()
    {
        dgv       = new System.Windows.Forms.DataGridView();
        btnAdd    = new System.Windows.Forms.Button();
        btnRemove = new System.Windows.Forms.Button();
        btnOk     = new System.Windows.Forms.Button();
        btnCancel = new System.Windows.Forms.Button();

        ((System.ComponentModel.ISupportInitialize)dgv).BeginInit();
        SuspendLayout();

        // DataGridView
        dgv.Location   = new System.Drawing.Point(12, 12);
        dgv.Size       = new System.Drawing.Size(556, 220);
        dgv.AllowUserToAddRows    = true;
        dgv.AllowUserToDeleteRows = false;
        dgv.RowHeadersVisible     = false;
        dgv.BorderStyle           = System.Windows.Forms.BorderStyle.None;
        dgv.CellBorderStyle       = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
        dgv.EnableHeadersVisualStyles = false;
        dgv.BackgroundColor      = UiTheme.Background;
        dgv.GridColor             = UiTheme.Border;
        dgv.RowTemplate.Height    = 28;
        dgv.SelectionMode         = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        dgv.Font = UiTheme.FontBody;
        dgv.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        dgv.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Primary;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextOnPrimary;
        dgv.ColumnHeadersHeight   = 30;
        dgv.DefaultCellStyle.BackColor          = UiTheme.Surface;
        dgv.DefaultCellStyle.SelectionBackColor = UiTheme.PrimaryLight;
        dgv.DefaultCellStyle.SelectionForeColor = UiTheme.TextOnPrimary;
        dgv.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.CardAlt;

        var colName   = new System.Windows.Forms.DataGridViewTextBoxColumn { HeaderText = "Name",   Width = 180, Name = "Name" };
        var colAge    = new System.Windows.Forms.DataGridViewTextBoxColumn { HeaderText = "Age",    Width = 55,  Name = "Age" };

        var colGender = new System.Windows.Forms.DataGridViewComboBoxColumn
        {
            HeaderText = "Gender", Width = 80, Name = "Gender",
        };
        colGender.Items.AddRange("M", "F", "T");

        var colBerth  = new System.Windows.Forms.DataGridViewComboBoxColumn
        {
            HeaderText = "Berth Pref", Width = 100, Name = "Berth",
        };
        colBerth.Items.AddRange("NP", "LB", "MB", "UB", "SL", "SU");

        dgv.Columns.AddRange(colName, colAge, colGender, colBerth);

        // Buttons
        int by = 242;
        btnAdd.Location = new System.Drawing.Point(12,  by); btnAdd.Size = new System.Drawing.Size(94, 30);
        btnAdd.Text = "+ Add Row";
        UiTheme.StyleSecondary(btnAdd);
        btnAdd.Click += new System.EventHandler(btnAdd_Click);

        btnRemove.Location = new System.Drawing.Point(110, by); btnRemove.Size = new System.Drawing.Size(94, 30);
        btnRemove.Text = "- Remove";
        UiTheme.StyleSecondary(btnRemove);
        btnRemove.Click += new System.EventHandler(btnRemove_Click);

        btnOk.Location = new System.Drawing.Point(394, by); btnOk.Size = new System.Drawing.Size(84, 30);
        btnOk.Text = "OK";
        UiTheme.StylePrimary(btnOk);
        btnOk.Click += new System.EventHandler(btnOk_Click);

        btnCancel.Location = new System.Drawing.Point(484, by); btnCancel.Size = new System.Drawing.Size(84, 30);
        btnCancel.Text = "Cancel"; btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
        UiTheme.StyleSecondary(btnCancel);

        // Form
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
        BackColor           = UiTheme.Surface;
        ClientSize          = new System.Drawing.Size(580, 284);
        Controls.AddRange(new System.Windows.Forms.Control[] { dgv, btnAdd, btnRemove, btnOk, btnCancel });
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = System.Windows.Forms.FormStartPosition.CenterParent;
        Text            = "Passengers";
        AcceptButton    = btnOk;
        CancelButton    = btnCancel;

        ((System.ComponentModel.ISupportInitialize)dgv).EndInit();
        ResumeLayout(false);
    }

    private System.Windows.Forms.DataGridView dgv = null!;
    private System.Windows.Forms.Button btnAdd    = null!;
    private System.Windows.Forms.Button btnRemove = null!;
    private System.Windows.Forms.Button btnOk     = null!;
    private System.Windows.Forms.Button btnCancel = null!;
}
