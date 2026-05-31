using System.Windows.Forms;

namespace indian_ticketing;

public partial class PassengersDialog : Form
{
    public List<Passenger> Passengers { get; private set; } = new();

    public PassengersDialog(List<Passenger>? existing = null)
    {
        InitializeComponent();
        if (existing != null)
            foreach (var p in existing)
                AddRow(p.Name, p.Age, p.Gender, p.Berth);
        else
            AddRow("", 25, "M", "NP");
    }

    private void AddRow(string name = "", int age = 25, string gender = "M", string berth = "NP")
    {
        dgv.Rows.Add(name, age, gender, berth);
    }

    private void btnAdd_Click(object? sender, EventArgs e) => AddRow();

    private void btnRemove_Click(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count > 0)
            dgv.Rows.Remove(dgv.SelectedRows[0]);
    }

    private void btnOk_Click(object? sender, EventArgs e)
    {
        Passengers.Clear();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (row.IsNewRow) continue;
            var name = row.Cells[0].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) continue;
            Passengers.Add(new Passenger
            {
                Name   = name,
                Age    = int.TryParse(row.Cells[1].Value?.ToString(), out var a) ? a : 25,
                Gender = row.Cells[2].Value?.ToString() ?? "M",
                Berth  = row.Cells[3].Value?.ToString() ?? "NP",
            });
        }
        if (Passengers.Count == 0)
        {
            MessageBox.Show("Add at least one passenger.", "Required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
