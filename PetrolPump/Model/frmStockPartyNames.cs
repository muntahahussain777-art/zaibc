using Guna.UI2.WinForms;
using System;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    /// <summary>
    /// Stock Add2 — VIP name list (StockPartyName only). AddDealer touch nahi.
    /// </summary>
    public class frmStockPartyNames : Form
    {
        private Guna2DataGridView dgv;
        private Guna2TextBox txtName;
        private Guna2Button btnSave;
        private Guna2Button btnUpdate;
        private Guna2Button btnDelete;
        private Guna2Button btnClear;
        private Guna2Button btnClose;
        private Label lblTitle;
        private Label lblName;
        private int _editId;

        public frmStockPartyNames()
        {
            BuildUi();
            Load += (s, e) =>
            {
                if (txtName != null)
                {
                    txtName.Size = new Size(480, 36);
                    txtName.Height = 36;
                }
                ReloadGrid();
            };
        }

        private void BuildUi()
        {
            Text = "Stock Names (Add2)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 500);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(32, 36, 66);

            lblTitle = new Label
            {
                Text = "Stock Names",
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(18, 14)
            };

            lblName = new Label
            {
                Text = "Name",
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(18, 54)
            };

            // Same pattern as Stock Add dealer textbox (fixed height, no overlap)
            txtName = new Guna2TextBox();
            txtName.AutoRoundedCorners = true;
            txtName.BorderColor = Color.FromArgb(112, 51, 255);
            txtName.BorderRadius = 17;
            txtName.Cursor = Cursors.IBeam;
            txtName.DefaultText = "";
            txtName.FillColor = Color.FromArgb(37, 41, 74);
            txtName.FocusedState.BorderColor = Color.FromArgb(94, 148, 255);
            txtName.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
            txtName.ForeColor = Color.White;
            txtName.HoverState.BorderColor = Color.FromArgb(94, 148, 255);
            txtName.Location = new Point(18, 78);
            txtName.Name = "txtName";
            txtName.PasswordChar = '\0';
            txtName.PlaceholderText = "";
            txtName.SelectedText = "";
            txtName.Size = new Size(480, 36);

            // Clear gap under Name textbox
            btnSave = MakeBtn("Save", Color.FromArgb(0, 150, 136), 18, 140);
            btnUpdate = MakeBtn("Update", Color.FromArgb(100, 88, 255), 128, 140);
            btnDelete = MakeBtn("Delete", Color.FromArgb(220, 53, 69), 238, 140);
            btnClear = MakeBtn("Clear", Color.FromArgb(52, 73, 94), 348, 140);
            btnClose = MakeBtn("Close", Color.FromArgb(39, 49, 70), 408, 454);
            btnClose.Size = new Size(90, 32);

            dgv = new Guna2DataGridView
            {
                Location = new Point(18, 188),
                Size = new Size(480, 252),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.FromArgb(32, 36, 66),
                BorderStyle = BorderStyle.None,
                ColumnHeadersHeight = 36,
                RowTemplate = { Height = 30 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ScrollBars = ScrollBars.Both,
                Theme = Guna.UI2.WinForms.Enums.DataGridViewPresetThemes.Default
            };
            dgv.ThemeStyle.HeaderStyle.BackColor = Color.FromArgb(100, 88, 255);
            dgv.ThemeStyle.HeaderStyle.ForeColor = Color.White;
            dgv.ThemeStyle.RowsStyle.BackColor = Color.FromArgb(32, 36, 66);
            dgv.ThemeStyle.RowsStyle.ForeColor = Color.White;
            dgv.ThemeStyle.AlternatingRowsStyle.BackColor = Color.FromArgb(37, 41, 74);
            dgv.DefaultCellStyle.BackColor = Color.FromArgb(32, 36, 66);
            dgv.DefaultCellStyle.ForeColor = Color.White;
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(100, 88, 255);
            dgv.DefaultCellStyle.SelectionForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(100, 88, 255);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.EnableHeadersVisualStyles = false;
            dgv.RowHeadersVisible = false;
            dgv.CellClick += Dgv_CellClick;
            dgv.CellDoubleClick += Dgv_CellClick;

            btnSave.Click += (s, e) => SaveNew();
            btnUpdate.Click += (s, e) => UpdateSelected();
            btnDelete.Click += (s, e) => DeleteSelected();
            btnClear.Click += (s, e) => ClearEdit();
            btnClose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            Controls.Add(lblTitle);
            Controls.Add(lblName);
            Controls.Add(txtName);
            Controls.Add(btnSave);
            Controls.Add(btnUpdate);
            Controls.Add(btnDelete);
            Controls.Add(btnClear);
            Controls.Add(dgv);
            Controls.Add(btnClose);

            // Force textbox height after parent attach (Guna sometimes expands)
            txtName.Size = new Size(480, 36);
            txtName.Height = 36;

            btnSave.BringToFront();
            btnUpdate.BringToFront();
            btnDelete.BringToFront();
            btnClear.BringToFront();
            btnClose.BringToFront();
        }

        private static Guna2Button MakeBtn(string text, Color fill, int x, int y)
        {
            return new Guna2Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(100, 34),
                BorderRadius = 12,
                FillColor = fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
            };
        }

        private void ReloadGrid()
        {
            try
            {
                StockPartyNameStore.EnsureDefault();
                var dt = StockPartyNameStore.ListForGrid();
                dgv.DataSource = dt;
                if (dgv.Columns.Contains("Id"))
                    dgv.Columns["Id"].Visible = false;
                if (dgv.Columns.Contains("Name"))
                    dgv.Columns["Name"].HeaderText = "Name";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Load error: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Dgv_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || dgv.CurrentRow == null) return;
            try
            {
                if (dgv.CurrentRow.Cells["Id"].Value == null) return;
                _editId = Convert.ToInt32(dgv.CurrentRow.Cells["Id"].Value);
                txtName.Text = Convert.ToString(dgv.CurrentRow.Cells["Name"].Value) ?? "";
            }
            catch { }
        }

        private void ClearEdit()
        {
            _editId = 0;
            txtName.Text = "";
            txtName.Focus();
            if (dgv.Rows.Count > 0)
                dgv.ClearSelection();
        }

        private void SaveNew()
        {
            string name = (txtName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Name likhein!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (StockPartyNameStore.FindId(name) > 0)
            {
                MessageBox.Show(this, "Ye name pehle se maujood hai!", "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (StockPartyNameStore.Insert(name) > 0)
            {
                MessageBox.Show(this, "Name save ho gaya!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearEdit();
                ReloadGrid();
            }
            else
                MessageBox.Show(this, "Save nahi hua!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void UpdateSelected()
        {
            if (_editId <= 0)
            {
                MessageBox.Show(this, "Pehle list se name select karein!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = (txtName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Name likhein!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int dup = StockPartyNameStore.FindId(name);
            if (dup > 0 && dup != _editId)
            {
                MessageBox.Show(this, "Ye name pehle se maujood hai!", "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (StockPartyNameStore.Update(_editId, name))
            {
                MessageBox.Show(this, "Name update ho gaya!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearEdit();
                ReloadGrid();
            }
            else
                MessageBox.Show(this, "Update nahi hua!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void DeleteSelected()
        {
            if (_editId <= 0)
            {
                MessageBox.Show(this, "Pehle list se name select karein!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int useCount = StockPartyNameStore.CountStockUsage(_editId);
            if (useCount > 0)
            {
                MessageBox.Show(this,
                    "Is name ki stock entries hain (" + useCount + "). Pehle entries delete/change karein!",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(this, "Delete karein? " + txtName.Text, "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            if (StockPartyNameStore.Delete(_editId))
            {
                MessageBox.Show(this, "Name delete ho gaya!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearEdit();
                ReloadGrid();
            }
            else
                MessageBox.Show(this, "Delete nahi hua!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
