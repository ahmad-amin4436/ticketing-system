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
        dgv.Location   = new System.Drawing.Point(8, 8);
        dgv.Size       = new System.Drawing.Size(560, 220);
        dgv.AllowUserToAddRows    = true;
        dgv.AllowUserToDeleteRows = false;
        dgv.RowHeadersVisible     = false;
        dgv.SelectionMode         = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        dgv.Font = new System.Drawing.Font("Segoe UI", 9F);
        dgv.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);

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
        int by = 238;
        btnAdd.Location = new System.Drawing.Point(8,   by); btnAdd.Size = new System.Drawing.Size(90, 28);
        btnAdd.Text = "+ Add Row"; btnAdd.UseVisualStyleBackColor = true;
        btnAdd.Click += new System.EventHandler(btnAdd_Click);

        btnRemove.Location = new System.Drawing.Point(104, by); btnRemove.Size = new System.Drawing.Size(90, 28);
        btnRemove.Text = "- Remove"; btnRemove.UseVisualStyleBackColor = true;
        btnRemove.Click += new System.EventHandler(btnRemove_Click);

        btnOk.Location = new System.Drawing.Point(396, by); btnOk.Size = new System.Drawing.Size(80, 28);
        btnOk.Text = "OK"; btnOk.UseVisualStyleBackColor = true;
        btnOk.Click += new System.EventHandler(btnOk_Click);

        btnCancel.Location = new System.Drawing.Point(482, by); btnCancel.Size = new System.Drawing.Size(80, 28);
        btnCancel.Text = "Cancel"; btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
        btnCancel.UseVisualStyleBackColor = true;

        // Form
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize          = new System.Drawing.Size(576, 276);
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
