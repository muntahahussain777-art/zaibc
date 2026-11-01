using System;
using System.Collections;
using System.Data;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public partial class frmDieselLedgerDealerAdd : SampleAdd
    {
        // ==============
        // 1) Two private fields to store old (dealer & amount)
        //    for "undo" logic when editing
        // ==============
        private int _oldDealerId = 0;
        private decimal _oldAmountPaid = 0;
        private bool _savedInSession;

        public int LedgerID { get; set; }  // Track if editing or adding a new record

        public frmDieselLedgerDealerAdd(int ledgerID = 0)
        {
            InitializeComponent();
            LedgerID = ledgerID;

            // NOTE: We do NOT call LoadLedgerDetails here.
            // We'll do that in frmDieselLedgerDealerAdd_Load, after PopulateDealerNames().

            cbDealer.SelectedIndexChanged += cbDealer_SelectedIndexChanged;
        }

        private void frmDieselLedgerDealerAdd_Load(object sender, EventArgs e)
        {
            // 1) Pehle dealers ComboBox fill karo
            PopulateDealerNames();

            // Credit amount commas — Load se pehle wire taake open pe 1,000,000 dikhe
            txtAmountPaid.TextChanged -= txtAmountPaid_TextChanged;
            txtAmountPaid.TextChanged += txtAmountPaid_TextChanged;

            // 2) Agar edit mode hai, to record load karo
            if (LedgerID > 0)
            {
                LoadLedgerDetails(LedgerID);
            }
            else
            {
                dtpPaymentDate.Value = DateTime.Now;
            }

            // NAYA CODE (shortcut keys, etc.)
            this.KeyPreview = true;
            this.KeyDown += frmDieselLedgerDealerAdd_KeyDown;

            txtDealerName.TextChanged += txtDealerName_TextChanged;
            txtDealerName.Visible = false; // by default hidden

            this.FormClosing -= frmDieselLedgerDealerAdd_FormClosing;
            this.FormClosing += frmDieselLedgerDealerAdd_FormClosing;
        }

        private void frmDieselLedgerDealerAdd_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_savedInSession && DialogResult == DialogResult.None)
                DialogResult = DialogResult.OK;
        }

        private void ResetAfterSaveKeepDate()
        {
            // Keep dealer + date; clear only amount/note for fast next entry.
            int? keepDid = null;
            if (cbDealer.SelectedValue != null && cbDealer.SelectedValue != DBNull.Value)
            {
                try { keepDid = Convert.ToInt32(cbDealer.SelectedValue); } catch { }
            }
            string keepDealerName = txtDealerName.Text;
            DateTime keepDate = dtpPaymentDate.Value;

            txtAmountPaid.Text = "";
            txtNote.Text = "";
            LedgerID = 0;
            _oldDealerId = 0;
            _oldAmountPaid = 0;
            dtpPaymentDate.Value = keepDate;

            if (keepDid.HasValue && cbDealer.DataSource != null)
            {
                try { cbDealer.SelectedValue = keepDid.Value; } catch { }
            }
            if (!string.IsNullOrEmpty(keepDealerName))
                txtDealerName.Text = keepDealerName;

            BeginInvoke(new Action(() =>
            {
                txtAmountPaid.Focus();
                txtAmountPaid.SelectAll();
            }));
        }
        private void PopulateDealerNames()
        {
            string query = "SELECT Did, DealerName FROM AddDealer";
            DataTable dt = MainClass.ExecuteSelectQuery(query, null);

            if (dt != null && dt.Rows.Count > 0)
            {
                cbDealer.DataSource = dt;
                cbDealer.DisplayMember = "DealerName";
                cbDealer.ValueMember = "Did";
                cbDealer.SelectedIndex = -1;
                cbDealer.DropDownHeight = 200;
                cbDealer.IntegralHeight = false;

                // [NAYA CODE for txtDealerName AutoComplete ↓↓]
                AutoCompleteStringCollection autoDealer = new AutoCompleteStringCollection();
                foreach (DataRow row in dt.Rows)
                {
                    autoDealer.Add(row["DealerName"].ToString());
                }
                txtDealerName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                txtDealerName.AutoCompleteSource = AutoCompleteSource.CustomSource;
                txtDealerName.AutoCompleteCustomSource = autoDealer;
                // [NAYA CODE ↑↑]
            }
        }

        // =========================
        // HOTKEYS FOR TEXTBOX/COMBO
        // =========================
        private void frmDieselLedgerDealerAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.D1) // Ctrl + 1
                {
                    txtDealerName.Visible = true;
                    cbDealer.Visible = false;
                    txtDealerName.Focus();
                }
                else if (e.KeyCode == Keys.D2) // Ctrl + 2
                {
                    txtDealerName.Visible = false;
                    cbDealer.Visible = true;
                    cbDealer.Focus();
                }
            }
        }

        // =========================
        // LOADLedgerDetails for Edit
        // =========================
        private void LoadLedgerDetails(int ledgerID)
        {
            string query = "SELECT * FROM DieselLedgerCredit WHERE LedgerID = @LedgerID";
            Hashtable ht = new Hashtable { { "@LedgerID", ledgerID } };

            DataTable dt = MainClass.ExecuteSelectQuery(query, ht);
            if (dt != null && dt.Rows.Count > 0)
            {
                // Combobox set karne ke liye
                cbDealer.SelectedValue = Convert.ToInt32(dt.Rows[0]["Did"]);

                _oldAmountPaid = Convert.ToDecimal(dt.Rows[0]["AmounGiven"]);
                txtAmountPaid.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", _oldAmountPaid);
                dtpPaymentDate.Value = Convert.ToDateTime(dt.Rows[0]["Date"]);
                txtNote.Text = dt.Rows[0]["Note"].ToString();

                // Store old scenario for "undo" if user changes dealer or amount
                _oldDealerId = Convert.ToInt32(dt.Rows[0]["Did"]);
            }
            else
            {
                CustomeMessage customMessageBox = new CustomeMessage("منتخب ریکارڈ کے لیے کوئی تفصیل نہیں ملی۔", "INFORMATION");
                customMessageBox.ShowDialog();
            }
        }

        // =========================
        // SAVE (INSERT OR UPDATE)
        // =========================
        public override void btnSave_Click(object sender, EventArgs e)
        {
            if (cbDealer.SelectedIndex == -1)
            {
                ErrorFormMessage err = new ErrorFormMessage("براہ کرم ڈیلر کا انتخاب کریں۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
                return;
            }

            if (!decimal.TryParse(txtAmountPaid.Text.Replace(",", ""), out decimal newAmountPaid))
            {
                ErrorFormMessage err = new ErrorFormMessage("براہ کرم ایک درست رقم درج کریں۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
                return;
            }

            DateTime paymentDate = dtpPaymentDate.Value;
            int newDealerId = Convert.ToInt32(cbDealer.SelectedValue);

            decimal netDeduction = BalanceConfirmationService.NetDeduction(
                newAmountPaid, _oldAmountPaid, LedgerID > 0 && newDealerId == _oldDealerId);
            if (!BalanceConfirmationService.ConfirmDealerCreditDeduction(newDealerId, netDeduction, cbDealer.Text))
                return;

            try
            {
                if (LedgerID == 0)
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        string sql = @"
                INSERT INTO DieselLedgerCredit (Did, AmounGiven, Date, Note)
                VALUES (@did, @amt, @dt, @note)";
                        var ht = new Hashtable
                        {
                            { "@did", newDealerId },
                            { "@amt", newAmountPaid },
                            { "@dt", paymentDate.ToString("yyyy-MM-dd") },
                            { "@note", txtNote.Text ?? "" }
                        };
                        if (MainClass.ExecInTx(sql, ht, conn, tx) <= 0) return false;
                        if (newDealerId > 0 && newAmountPaid != 0)
                        {
                            if (MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DAmount = DAmount + @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", newDealerId }, { "@amount", newAmountPaid } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        long lid = Convert.ToInt64(LocalPersistence.Scalar("SELECT last_insert_rowid()", null, conn, tx));
                        SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "DieselLedgerCredit", "LedgerID", lid);
                        return true;
                    });
                    if (!ok)
                    {
                        ErrorFormMessage err = new ErrorFormMessage("ڈیٹا محفوظ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        err.ShowDialog();
                        return;
                    }

                    CustomeMessage success = new CustomeMessage("ڈیٹا کامیابی کے ساتھ محفوظ کر لیا گیا۔", "ZAIB PETROLEUM SERVICE");
                    success.ShowDialog();
                    ResetAfterSaveKeepDate();
                    _savedInSession = true;
                }
                else
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        DataRow old = LocalPersistence.ReadRow("DieselLedgerCredit", "LedgerID", LedgerID, conn, tx);
                        if (old == null) return false;
                        int authOldDealer = Convert.ToInt32(old["Did"]);
                        decimal authOldAmt = Convert.ToDecimal(old["AmounGiven"]);

                        string sql = @"
                UPDATE DieselLedgerCredit
                SET Did        = @did,
                    AmounGiven = @amt,
                    Date       = @dt,
                    Note       = @note
                WHERE LedgerID = @lid";
                        var ht = new Hashtable
                        {
                            { "@lid", LedgerID },
                            { "@did", newDealerId },
                            { "@amt", newAmountPaid },
                            { "@dt", paymentDate.ToString("yyyy-MM-dd") },
                            { "@note", txtNote.Text ?? "" }
                        };
                        if (MainClass.ExecInTx(sql, ht, conn, tx) <= 0) return false;

                        if (authOldDealer != newDealerId)
                        {
                            if (authOldDealer > 0 && authOldAmt != 0 &&
                                MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DAmount = DAmount - @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", authOldDealer }, { "@amount", authOldAmt } },
                                    conn, tx) <= 0)
                                return false;
                            if (newDealerId > 0 && newAmountPaid != 0 &&
                                MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DAmount = DAmount + @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", newDealerId }, { "@amount", newAmountPaid } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        else
                        {
                            decimal difference = newAmountPaid - authOldAmt;
                            if (difference != 0 && newDealerId > 0)
                            {
                                string q = difference > 0
                                    ? "UPDATE AddDealer SET DAmount = DAmount + @amount WHERE Did = @dealerId"
                                    : "UPDATE AddDealer SET DAmount = DAmount - @amount WHERE Did = @dealerId";
                                if (MainClass.ExecInTx(q,
                                        new Hashtable { { "@dealerId", newDealerId }, { "@amount", Math.Abs(difference) } },
                                        conn, tx) <= 0)
                                    return false;
                            }
                        }
                        SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "DieselLedgerCredit", "LedgerID", LedgerID);
                        return true;
                    });
                    if (!ok)
                    {
                        ErrorFormMessage err = new ErrorFormMessage("ڈیٹا اپڈیٹ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        err.ShowDialog();
                        return;
                    }

                    CustomeMessage success = new CustomeMessage("ڈیٹا کامیابی کے ساتھ اپڈیٹ ہوگیا۔", "ZAIB PETROLEUM SERVICE");
                    success.ShowDialog();
                    _savedInSession = true;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("خرابی: " + ex.Message, "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
            }
        }

        // =========================
        // INSERT & UPDATE Queries
        // =========================
        private void InsertLedgerRecord(int dealerId, decimal amountPaid, string note, DateTime paymentDate)
        {
            string sql = @"
                INSERT INTO DieselLedgerCredit (Did, AmounGiven, Date, Note)
                VALUES (@did, @amt, @dt, @note)";

            Hashtable ht = new Hashtable
            {
                { "@did", dealerId },
                { "@amt", amountPaid },
                { "@dt", paymentDate.ToString("yyyy-MM-dd") },
                { "@note", note }
            };

            int result = MainClass.DataInsertUpdateDelete(sql, ht);
            if (result <= 0)
            {
                ErrorFormMessage err = new ErrorFormMessage("ڈیٹا محفوظ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
            }
        }

        private bool UpdateLedgerRecord(int ledgerId, int dealerId, decimal amountPaid, string note, DateTime paymentDate)
        {
            string sql = @"
                UPDATE DieselLedgerCredit
                SET Did        = @did,
                    AmounGiven = @amt,
                    Date       = @dt,
                    Note       = @note
                WHERE LedgerID = @lid";

            Hashtable ht = new Hashtable
            {
                { "@lid", ledgerId },
                { "@did", dealerId },
                { "@amt", amountPaid },
                { "@dt", paymentDate.ToString("yyyy-MM-dd") },
                { "@note", note }
            };

            int result = MainClass.DataInsertUpdateDelete(sql, ht);
            if (result <= 0)
            {
                ErrorFormMessage err = new ErrorFormMessage("ڈیٹا اپڈیٹ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
                return false;
            }
            return true;
        }

        // =========================
        // ADD & SUBTRACT Dealer's DAmount
        // =========================
        private void AddToDealerAmount(int dealerId, decimal amountPaid)
        {
            if (dealerId == 0 || amountPaid == 0) return;

            string updateQuery = "UPDATE AddDealer SET DAmount = DAmount + @amount WHERE Did = @dealerId";
            Hashtable htUpdate = new Hashtable
            {
                { "@dealerId", dealerId },
                { "@amount", amountPaid }
            };

            int updateResult = MainClass.DataInsertUpdateDelete(updateQuery, htUpdate);
            if (updateResult <= 0)
            {
                ErrorFormMessage err = new ErrorFormMessage("ڈیلر کے DAmount کو اپڈیٹ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
            }
        }

        private void SubtractFromDealerAmount(int dealerId, decimal oldAmount)
        {
            if (dealerId == 0 || oldAmount == 0) return;

            string updateQuery = "UPDATE AddDealer SET DAmount = DAmount - @amount WHERE Did = @dealerId";
            Hashtable htUpdate = new Hashtable
            {
                { "@dealerId", dealerId },
                { "@amount", oldAmount }
            };

            int updateResult = MainClass.DataInsertUpdateDelete(updateQuery, htUpdate);
            if (updateResult <= 0)
            {
                ErrorFormMessage err = new ErrorFormMessage("پرانے ڈیلر کے DAmount کو واپس کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                err.ShowDialog();
            }
        }

        // =========================
        // DELETE BUTTON
        // =========================
        public override void btnDel_Click(object sender, EventArgs e)
        {
            if (LedgerID > 0)
            {
                YesOrNoMessage confirmDelete = new YesOrNoMessage("کیا آپ واقعی اس ریکارڈ کو حذف کرنا چاہتے ہیں؟", "حذف کی تصدیق کریں");
                if (confirmDelete.ShowDialog() == DialogResult.Yes)
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        DataRow old = LocalPersistence.ReadRow("DieselLedgerCredit", "LedgerID", LedgerID, conn, tx);
                        if (old == null) return false;
                        int dealerId = Convert.ToInt32(old["Did"]);
                        decimal amountToDelete = Convert.ToDecimal(old["AmounGiven"]);
                        string syncId = old.Table.Columns.Contains("SyncId") ? old["SyncId"]?.ToString() : null;
                        long? expectedRev = null;
                        if (old.Table.Columns.Contains("ServerRev") && old["ServerRev"] != DBNull.Value)
                        {
                            try { long v = Convert.ToInt64(old["ServerRev"]); if (v > 0) expectedRev = v; } catch { }
                        }

                        if (MainClass.ExecInTx(
                                "DELETE FROM DieselLedgerCredit WHERE LedgerID = @LedgerID",
                                new Hashtable { { "@LedgerID", LedgerID } },
                                conn, tx) <= 0)
                            return false;

                        LocalPersistence.EnsureTombstone(syncId, "zaib_dealer_payouts", conn, tx, expectedRev);

                        if (dealerId > 0 && amountToDelete != 0)
                        {
                            if (MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DAmount = DAmount - @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", dealerId }, { "@amount", amountToDelete } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        SupabaseSyncService.DeleteBalanceMarkerOnly(conn, tx, syncId);
                        return true;
                    });

                    if (ok)
                    {
                        CustomeMessage successMessage = new CustomeMessage("ریکارڈ کامیابی کے ساتھ حذف کر دیا گیا اور ڈیلر کا DAmount اپ ڈیٹ ہو گیا۔", "ZAIB PETROLEUM SERVICE");
                        successMessage.ShowDialog();
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        ErrorFormMessage errorMessage = new ErrorFormMessage("ڈیزل لیجر سے ریکارڈ ختم کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        errorMessage.ShowDialog();
                    }
                }
            }
            else
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("براہ کرم حذف کرنے کے لیے ایک درست ریکارڈ منتخب کریں۔", "ZAIB PETROLEUM SERVICE");
                errorMessage.ShowDialog();
            }
        }

        // =========================
        // COMBOBOX, TEXTBOX EVENTS
        // =========================
        private void cbDealer_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbDealer.SelectedItem != null)
            {
                int dealerId = Convert.ToInt32((cbDealer.SelectedItem as DataRowView)["Did"]);
                ShowDealerBalance(dealerId);
            }
        }

        private void ShowDealerBalance(int dealerId)
        {
            string query = "SELECT DAmount, DDAmount FROM AddDealer WHERE Did = @dealerId";
            Hashtable ht = new Hashtable { { "@dealerId", dealerId } };
            DataTable dt = MainClass.ExecuteSelectQuery(query, ht);

            if (dt != null && dt.Rows.Count > 0)
            {
                decimal dAmount = Convert.ToDecimal(dt.Rows[0]["DAmount"]);
                decimal ddAmount = Convert.ToDecimal(dt.Rows[0]["DDAmount"]);

                lblDealerBalance.Text = $"Balance: {ddAmount:F2}";
                lblcreditbalance.Text = $"Balance: {dAmount:F2}";
                decimal creditBalance = ddAmount - dAmount;
                lblbalance.Text = $"Credit Balance: {creditBalance:F2}";
            }
            else
            {
                lblDealerBalance.Text = "Balance: 0.00";
                lblcreditbalance.Text = "Balance: 0.00";
                lblbalance.Text = "Balance: 0.00";
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

        private void txtAmountPaid_TextChanged(object sender, EventArgs e)
        {
            string currentText = (txtAmountPaid.Text ?? "").Replace(",", "").Trim();
            if (string.IsNullOrEmpty(currentText)) return;

            if (decimal.TryParse(currentText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            {
                string formatted = value.ToString("N0", CultureInfo.InvariantCulture);
                if (txtAmountPaid.Text != formatted)
                {
                    txtAmountPaid.Text = formatted;
                    txtAmountPaid.SelectionStart = txtAmountPaid.Text.Length;
                }
            }
        }
    }
}
