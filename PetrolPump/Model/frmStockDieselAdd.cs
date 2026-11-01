using System;
using System.Collections;
using System.Data;
using System.Globalization;
using System.Windows.Forms;

namespace ZaibPetroleumService.Model
{
    public partial class frmStockDieselAdd : SampleAdd1, IMessageFilter
    {
        public frmStockDieselAdd()
        {
            InitializeComponent();
            SetupMinusLitterControls();
            Application.AddMessageFilter(this);
            FormClosed += (s, e) =>
            {
                try { Application.RemoveMessageFilter(this); } catch { }
            };
        }

        public int id = 0;
        private const string StockDealerName = "Stock";

        private void frmStockDieselAdd_Load(object sender, EventArgs e)
        {
            SetupMinusLitterControls();
            ApplyPartyStockMode();

            Shown += (s, ev) =>
            {
                try
                {
                    ApplyPartyStockMode();
                    if (id == 0)
                    {
                        PopulatePartyDealerNames();
                        SelectRozNamchaDefault();
                    }
                    else
                    {
                        // Update: Name wahi rahe jo record mein hai (RozNamcha pe reset na ho)
                        int keepDid = 0;
                        try
                        {
                            if (cbDealer.SelectedValue != null && cbDealer.SelectedValue != DBNull.Value)
                                keepDid = Convert.ToInt32(cbDealer.SelectedValue);
                        }
                        catch { }

                        PopulatePartyDealerNames();
                        if (keepDid > 0 && cbDealer.DataSource != null)
                        {
                            try { cbDealer.SelectedValue = keepDid; } catch { }
                            if (cbDealer.SelectedIndex < 0)
                                LoadExistingRecord(id);
                        }
                        else
                            LoadExistingRecord(id);
                    }
                    FocusPartyVehicle();
                }
                catch { }
            };

            PopulatePartyDealerNames();
            if (id == 0)
            {
                txtdate.Value = DateTime.Now;
                SelectRozNamchaDefault();
            }

            if (id > 0)
                LoadExistingRecord(id);

            txtlitter.Leave += new EventHandler(txtlitter_Leave);
            txtlitter.TextChanged += new EventHandler(txtlitter_TextChanged);

            this.KeyPreview = true;
            this.KeyDown += frmStockDieselAdd_KeyDown;

            txtDealerName.Visible = false;
            txtDealerName.TextChanged += txtDealerName_TextChanged;

            BeginInvoke(new Action(FocusPartyVehicle));
        }

        private int FindStockDealerId()
        {
            var ht = new Hashtable { { "@n", StockDealerName } };
            DataTable dt = MainClass.ExecuteSelectQuery(
                "SELECT Did FROM AddDealer WHERE TRIM(IFNULL(DealerName,'')) = @n COLLATE NOCASE LIMIT 1", ht);
            if (dt == null || dt.Rows.Count == 0 || dt.Rows[0]["Did"] == DBNull.Value)
                return 0;
            return Convert.ToInt32(dt.Rows[0]["Did"]);
        }

        private void EnsureStockDealerExists()
        {
            try
            {
                if (FindStockDealerId() > 0)
                    return;

                var ht = new Hashtable
                {
                    { "@name", StockDealerName },
                    { "@Amount", 0 },
                    { "@DAmount", 0 },
                    { "@date", DateTime.Now.ToString("yyyy-MM-dd") }
                };
                MainClass.DataInsertUpdateDelete(
                    "INSERT INTO AddDealer (DealerName, DDAmount, DAmount, Date) VALUES (@name, @Amount, @DAmount, @date)",
                    ht);
            }
            catch { }
        }

        private void SelectStockDealer()
        {
            try
            {
                int did = FindStockDealerId();
                if (did <= 0) return;
                if (cbDealer.DataSource != null)
                    cbDealer.SelectedValue = did;
                txtDealerName.Text = StockDealerName;
                txtDealerName.Visible = false;
                cbDealer.Visible = true;
            }
            catch { }
        }

        private void KeepDateAndStockDealer()
        {
            MainClass.Enable_reset_keep_date(this, txtdate);
            id = 0;
            SelectStockDealer();
            FocusAddLitter();
        }

        private void ApplyLitterTabCycle()
        {
            txtlitter.TabStop = true;
            txtlitter.TabIndex = 1;
            if (txtMinusLitter != null)
            {
                txtMinusLitter.TabStop = true;
                txtMinusLitter.TabIndex = 2;
            }
            txtRate.TabStop = true;
            txtRate.TabIndex = 3;
            txtNote.TabStop = true;
            txtNote.TabIndex = 4;

            txtvehicle.TabStop = false;
            txtdate.TabStop = false;
            cbDealer.TabStop = false;
            txtDealerName.TabStop = false;
            txtcredit.TabStop = false;
            txtdebit.TabStop = false;
            btnSave.TabStop = false;
            btnDel.TabStop = false;
            btnClose.TabStop = false;
            if (lblMinusLitter != null)
                lblMinusLitter.TabStop = false;

            StopInnerTab(txtvehicle);
            StopInnerTab(txtdate);
            StopInnerTab(cbDealer);
            StopInnerTab(txtDealerName);
            StopInnerTab(txtcredit);
            StopInnerTab(txtdebit);
            StopInnerTab(txtlitter);
            StopInnerTab(txtMinusLitter);
            StopInnerTab(txtRate);
            StopInnerTab(txtNote);
        }

        private static void StopInnerTab(Control box)
        {
            if (box == null) return;
            foreach (Control c in box.Controls)
            {
                c.TabStop = false;
                StopInnerTab(c);
            }
        }

        private void FocusAddLitter()
        {
            try
            {
                if (txtlitter == null || txtlitter.IsDisposed) return;
                ActiveControl = txtlitter;
                txtlitter.Focus();
                txtlitter.SelectionStart = (txtlitter.Text ?? "").Length;
                txtlitter.SelectionLength = 0;
            }
            catch { }
        }

        private Guna.UI2.WinForms.Guna2TextBox[] LitterTabCycle()
        {
            return new[] { txtlitter, txtMinusLitter, txtRate, txtNote };
        }

        private bool TryCycleLitterFields(bool reverse)
        {
            if (_partyMode)
            {
                // Fast text (Ctrl+1): Name + Type textboxes; else combos
                Control[] cycle = GetPartyTabCycle();
                int idx = -1;
                for (int i = 0; i < cycle.Length; i++)
                {
                    if (cycle[i] != null && !cycle[i].IsDisposed && cycle[i].ContainsFocus)
                    {
                        idx = i;
                        break;
                    }
                }
                int next = idx < 0
                    ? (reverse ? cycle.Length - 1 : 0)
                    : (reverse ? (idx - 1 + cycle.Length) % cycle.Length : (idx + 1) % cycle.Length);
                for (int n = 0; n < cycle.Length; n++)
                {
                    int i = reverse
                        ? (next - n + cycle.Length) % cycle.Length
                        : (next + n) % cycle.Length;
                    var box = cycle[i];
                    if (box == null || box.IsDisposed || !box.Visible || !box.Enabled) continue;
                    try
                    {
                        box.Focus();
                        return true;
                    }
                    catch { }
                }
                return false;
            }

            var cycleOld = LitterTabCycle();
            int idxOld = -1;
            for (int i = 0; i < cycleOld.Length; i++)
            {
                if (cycleOld[i] != null && !cycleOld[i].IsDisposed && cycleOld[i].ContainsFocus)
                {
                    idxOld = i;
                    break;
                }
            }

            int nextOld;
            if (idxOld < 0)
                nextOld = reverse ? cycleOld.Length - 1 : 0;
            else
                nextOld = reverse
                    ? (idxOld - 1 + cycleOld.Length) % cycleOld.Length
                    : (idxOld + 1) % cycleOld.Length;

            for (int n = 0; n < cycleOld.Length; n++)
            {
                int i = reverse
                    ? (nextOld - n + cycleOld.Length) % cycleOld.Length
                    : (nextOld + n) % cycleOld.Length;
                var box = cycleOld[i];
                if (box == null || box.IsDisposed || !box.Visible || !box.Enabled)
                    continue;
                try
                {
                    box.Focus();
                    box.SelectionStart = (box.Text ?? "").Length;
                    box.SelectionLength = 0;
                    return true;
                }
                catch { }
            }
            return false;
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x0100;
            if (m.Msg != WM_KEYDOWN) return false;
            if (!Visible || IsDisposed) return false;
            if (Form.ActiveForm != this) return false;
            if ((Keys)m.WParam.ToInt32() != Keys.Tab) return false;
            bool reverse = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            return TryCycleLitterFields(reverse);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyPartyStockMode();
            BeginInvoke(new Action(FocusPartyVehicle));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                if (TryCycleLitterFields(keyData == (Keys.Shift | Keys.Tab)))
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                if (TryCycleLitterFields(keyData == (Keys.Shift | Keys.Tab)))
                    return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void frmStockDieselAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control) return;

            if (e.KeyCode == Keys.D1)
            {
                e.SuppressKeyPress = true;
                if (_partyMode)
                    EnterPartyFastTextMode();
                else
                {
                    txtDealerName.Visible = true;
                    cbDealer.Visible = false;
                    txtDealerName.Focus();
                }
            }
            else if (e.KeyCode == Keys.D2)
            {
                e.SuppressKeyPress = true;
                if (_partyMode)
                    ExitPartyFastTextMode();
                else
                {
                    txtDealerName.Visible = false;
                    cbDealer.Visible = true;
                    cbDealer.Focus();
                }
            }
        }

        private void LoadExistingRecord(int id)
        {
            string query = "SELECT * FROM StockDiesel WHERE SID = @id";
            Hashtable parameters = new Hashtable();
            parameters.Add("@id", id);

            try
            {
                DataTable dt = MainClass.ExecuteSelectQuery(query, parameters);

                if (dt != null && dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    txtdate.Value = Convert.ToDateTime(row["Date"]);
                    txtvehicle.Text = row["Vehicle"].ToString();

                    // Stock party Name (StockPartyName.Pid = SDid) — update pe wahi name
                    if (_partyMode)
                        PopulatePartyDealerNames();

                    if (row["SDid"] != DBNull.Value)
                    {
                        int dealerId;
                        if (int.TryParse(row["SDid"].ToString(), out dealerId))
                        {
                            try { cbDealer.SelectedValue = dealerId; } catch { }

                            if (cbDealer.SelectedIndex == -1)
                            {
                                string partyName = "";
                                try
                                {
                                    DataTable pn = MainClass.ExecuteSelectQuery(
                                        "SELECT PartyName FROM StockPartyName WHERE Pid = @id LIMIT 1",
                                        new Hashtable { { "@id", dealerId } });
                                    if (pn != null && pn.Rows.Count > 0)
                                        partyName = Convert.ToString(pn.Rows[0]["PartyName"]) ?? "";
                                }
                                catch { }

                                cbDealer.SelectedIndex = -1;
                                txtDealerName.Visible = true;
                                cbDealer.Visible = false;
                                txtDealerName.Text = string.IsNullOrWhiteSpace(partyName)
                                    ? "(Missing Name ID: " + dealerId + ")"
                                    : partyName;
                            }
                            else
                            {
                                txtDealerName.Visible = false;
                                cbDealer.Visible = true;
                            }
                        }
                    }

                    LoadPartyFieldsFromRow(row);
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Koi record nahi mila!", "Warning");
                    noDataMessage.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void PopulateDealerNames()
        {
            string query = "SELECT Did, DealerName FROM AddDealer LIMIT 1000";
            try
            {
                DataTable dt = MainClass.ExecuteSelectQuery(query, null);

                if (dt != null && dt.Rows.Count > 0)
                {
                    cbDealer.DataSource = dt;
                    cbDealer.DisplayMember = "DealerName";
                    cbDealer.ValueMember = "Did";
                    cbDealer.DropDownHeight = 200;
                    cbDealer.IntegralHeight = false;

                    // AutoComplete for txtDealerName
                    AutoCompleteStringCollection autoSource = new AutoCompleteStringCollection();
                    foreach (DataRow row in dt.Rows)
                    {
                        autoSource.Add(row["DealerName"].ToString());
                    }
                    txtDealerName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    txtDealerName.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    txtDealerName.AutoCompleteCustomSource = autoSource;
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Koi dealer nahi mila!", "Warning");
                    noDataMessage.ShowDialog();
                    cbDealer.DataSource = null;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtDealerName_TextChanged(object sender, EventArgs e)
        {
            string enteredName = txtDealerName.Text.Trim();

            if (!string.IsNullOrEmpty(enteredName))
            {
                DataTable dt = cbDealer.DataSource as DataTable;
                if (dt != null)
                {
                    string filter = $"DealerName = '{enteredName.Replace("'", "''")}'";
                    DataRow[] rows = dt.Select(filter);

                    if (rows.Length > 0)
                    {
                        cbDealer.SelectedValue = rows[0]["Did"];
                    }
                    else
                    {
                        cbDealer.SelectedIndex = -1;
                    }
                }
            }
            else
            {
                cbDealer.SelectedIndex = -1;
            }
        }

        protected override void btnSave_Click(object sender, EventArgs e)
        {
            // Stock menu se hamesha party (Roznamcha) save — purana AddDealer path nahi
            try
            {
                ApplyPartyStockMode();
                TrySavePartyStockEntry();
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Save): " + ex.Message, "Error");
                    errorMessage.ShowDialog();
                }
                catch
                {
                    MessageBox.Show("Error (Save): " + ex.Message);
                }
            }
        }

        public override void btnDel_Click(object sender, EventArgs e)
        {
            if (id > 0)
            {
                try
                {
                    YesOrNoMessage confirmDelete = new YesOrNoMessage("Kya aap is record ko delete karna chahte hain?", "Confirm Delete");
                    if (confirmDelete.ShowDialog() == DialogResult.Yes)
                    {
                        int deleteResult = MainClass.DeleteWithTombstone("StockDiesel", "SID", id, "zaib_stock_diesel");
                        if (deleteResult > 0)
                        {
                            CustomeMessage successMessage = new CustomeMessage("Record delete ho gaya!", "Success");
                            successMessage.ShowDialog();
                            id = 0;
                            DateTime keepDate = DateTime.Now;
                            int keepDid = 0;
                            try { if (txtdate != null) keepDate = txtdate.Value; } catch { }
                            try
                            {
                                if (cbDealer != null && cbDealer.SelectedValue != null
                                    && cbDealer.SelectedValue != DBNull.Value
                                    && !(cbDealer.SelectedValue is DataRowView))
                                    keepDid = Convert.ToInt32(cbDealer.SelectedValue);
                            }
                            catch { }
                            try
                            {
                                if (_partyMode)
                                    ResetPartyAfterSaveKeepNameDate(keepDid, keepDate);
                                else
                                {
                                    KeepDateAndStockDealer();
                                    try { txtdate.Value = keepDate; } catch { }
                                }
                            }
                            catch
                            {
                                KeepDateAndStockDealer();
                                try { txtdate.Value = keepDate; } catch { }
                            }
                            try { NotifyStockViewRefresh(keepDate); } catch { }
                        }
                        else
                        {
                            ErrorFormMessage errorMessage = new ErrorFormMessage("Stock record delete nahi hua!", "Error");
                            errorMessage.ShowDialog();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                    errorMessage.ShowDialog();
                }
            }
            else
            {
                CustomeMessage noSelectionMessage = new CustomeMessage("Pehle ek record select karein!", "Warning");
                noSelectionMessage.ShowDialog();
            }
        }

        private void txtlitter_Leave(object sender, EventArgs e)
        {
            FormatPositiveLitterBox(txtlitter);
        }

        private void txtlitter_TextChanged(object sender, EventArgs e)
        {
            KeepLitterCaretAtTypePosition(txtlitter);
        }

        private void txtlitter_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true;
                CustomeMessage customMessageBox = new CustomeMessage("یہاں صرف نمبر اور دہائی کا نشان (.) لکھ سکتے ہو!", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            TextBox textBox = sender as TextBox;
            if (e.KeyChar == '.' && textBox != null && textBox.Text.Contains("."))
            {
                e.Handled = true;
            }
        }

        private void txtRate_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true;
                CustomeMessage customMessageBox = new CustomeMessage("یہاں صرف نمبر اور دہائی کا نشان (.) لکھ سکتے ہو!", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            TextBox textBox = sender as TextBox;
            if (e.KeyChar == '.' && textBox != null && textBox.Text.Contains("."))
            {
                e.Handled = true;
            }
        }

        private void txtcredit_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true;
                CustomeMessage customMessageBox = new CustomeMessage("یہاں صرف نمبر اور دہائی کا نشان (.) لکھ سکتے ہو!", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            TextBox textBox = sender as TextBox;
            if (e.KeyChar == '.' && textBox != null && textBox.Text.Contains("."))
            {
                e.Handled = true;
            }
        }

        private void txtdebit_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true;
                CustomeMessage customMessageBox = new CustomeMessage("یہاں صرف نمبر اور دہائی کا نشان (.) لکھ سکتے ہو!", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            TextBox textBox = sender as TextBox;
            if (e.KeyChar == '.' && textBox != null && textBox.Text.Contains("."))
            {
                e.Handled = true;
            }
        }
    }
}
