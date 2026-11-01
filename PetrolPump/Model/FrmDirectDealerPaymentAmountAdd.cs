using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public partial class FrmDirectDealerPaymentAmountAdd : SampleAdd
    {
        public int LedgerID { get; set; }

        // Yeh do private variables rakhe hain purana dealer/amount store karne ke liye
        private int _oldDid = 0;
        private decimal _oldAmountPaid = 0;
        private bool _savedInSession;

        public FrmDirectDealerPaymentAmountAdd(int ledgerID = 0)
        {
            InitializeComponent();
            LedgerID = ledgerID;
        }

        private void FrmDirectDealerPaymentAmountAdd_Load(object sender, EventArgs e)
        {
            // Pehle comboBox fill karo
            PopulateDealerNames();

            // Agar editing mode hai (LedgerID > 0), to LoadLedgerDetails se data bharo
            if (LedgerID > 0)
            {
                LoadLedgerDetails(LedgerID);
            }
            else
            {
                dtpPaymentDate.Value = DateTime.Now;
            }

            this.KeyPreview = true;

            txtDealerName.Visible = false;
            txtDealerName.TextChanged += txtDealerName_TextChanged;

            this.FormClosing -= FrmDirectDealerPaymentAmountAdd_FormClosing;
            this.FormClosing += FrmDirectDealerPaymentAmountAdd_FormClosing;
        }

        private void FrmDirectDealerPaymentAmountAdd_FormClosing(object sender, FormClosingEventArgs e)
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
            _oldDid = 0;
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

        private void FrmDirectDealerPaymentAmountAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.D1)
                {
                    txtDealerName.Visible = true;
                    cbDealer.Visible = false;
                    txtDealerName.Focus();
                }
                else if (e.KeyCode == Keys.D2)
                {
                    txtDealerName.Visible = false;
                    cbDealer.Visible = true;
                    cbDealer.Focus();
                }
                else if (e.KeyCode == Keys.S)
                {
                    e.SuppressKeyPress = true;
                }
            }
        }


        // Edit mode ke liye purana record load karein
        private void LoadLedgerDetails(int ledgerID)
        {
            string query = "SELECT * FROM DieselLedgerDebit WHERE LedgerID = @LedgerID";
            Hashtable ht = new Hashtable();
            ht.Add("@LedgerID", ledgerID);

            DataTable dt = MainClass.ExecuteSelectQuery(query, ht);
            if (dt != null && dt.Rows.Count > 0)
            {
                int did = Convert.ToInt32(dt.Rows[0]["Did"]);
                _oldDid = did;
                _oldAmountPaid = Convert.ToDecimal(dt.Rows[0]["AmounGiven"]);

                try { cbDealer.SelectedValue = did; }
                catch { /* ignore */ }
                if (cbDealer.SelectedValue == null || Convert.ToInt32(cbDealer.SelectedValue) != did)
                {
                    for (int i = 0; i < cbDealer.Items.Count; i++)
                    {
                        if (cbDealer.Items[i] is DataRowView drv && Convert.ToInt32(drv["Did"]) == did)
                        {
                            cbDealer.SelectedIndex = i;
                            break;
                        }
                    }
                }

                txtAmountPaid.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", _oldAmountPaid);
                dtpPaymentDate.Value = Convert.ToDateTime(dt.Rows[0]["Date"]);
                txtNote.Text = dt.Rows[0]["Note"].ToString();
            }
            else
            {
                CustomeMessage msg = new CustomeMessage("منتخب ریکارڈ کے لیے کوئی تفصیل نہیں ملی۔", "ZAIB PETROLEUM SERVICE");
                msg.ShowDialog();
            }
        }

        private void ShowDealerBalance(int dealerId)
        {
            string query = "SELECT DAmount, DDAmount FROM AddDealer WHERE Did = @dealerId";
            Hashtable ht = new Hashtable();
            ht.Add("@dealerId", dealerId);

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

        public override void btnSave_Click(object sender, EventArgs e)
        {
            if (cbDealer.SelectedValue == null || cbDealer.SelectedValue == DBNull.Value ||
                !int.TryParse(cbDealer.SelectedValue.ToString(), out int newDealerId) || newDealerId <= 0)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("براہ کرم ڈیلر کا انتخاب کریں۔", "ZAIB PETROLEUM SERVICE");
                errorMessage.ShowDialog();
                return;
            }

            if (!decimal.TryParse(txtAmountPaid.Text.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newAmountPaid))
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("براہ کرم ایک درست رقم درج کریں۔", "ZAIB PETROLEUM SERVICE");
                errorMessage.ShowDialog();
                return;
            }

            DateTime paymentDate = dtpPaymentDate.Value;
            bool isEdit = LedgerID > 0;
            string note = txtNote.Text ?? "";

            try
            {
                if (!isEdit)
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        string insertQuery = @"
                    INSERT INTO DieselLedgerDebit (Did, AmounGiven, Date, Note) 
                    VALUES (@dealerId, @amountPaid, @paymentDate, @note)";
                        var ht = new Hashtable
                        {
                            { "@dealerId", newDealerId },
                            { "@amountPaid", newAmountPaid },
                            { "@paymentDate", paymentDate.ToString("yyyy-MM-dd") },
                            { "@note", note }
                        };
                        if (MainClass.ExecInTx(insertQuery, ht, conn, tx) <= 0) return false;
                        if (newDealerId > 0 && newAmountPaid != 0)
                        {
                            if (MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DDAmount = DDAmount + @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", newDealerId }, { "@amount", newAmountPaid } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        long lid = Convert.ToInt64(LocalPersistence.Scalar("SELECT last_insert_rowid()", null, conn, tx));
                        SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "DieselLedgerDebit", "LedgerID", lid);
                        return true;
                    });
                    if (!ok)
                    {
                        ErrorFormMessage err = new ErrorFormMessage("ڈیٹا محفوظ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        err.ShowDialog();
                        return;
                    }

                    CustomeMessage successMsg = new CustomeMessage("نیا ریکارڈ محفوظ ہوگیا اور ڈیلر کا بیلنس اپڈیٹ ہوگیا۔", "ZAIB PETROLEUM SERVICE");
                    successMsg.ShowDialog();
                    _savedInSession = true;
                    ResetAfterSaveKeepDate();
                }
                else
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        DataRow old = LocalPersistence.ReadRow("DieselLedgerDebit", "LedgerID", LedgerID, conn, tx);
                        if (old == null) return false;
                        int authOldDid = Convert.ToInt32(old["Did"]);
                        decimal authOldAmt = Convert.ToDecimal(old["AmounGiven"]);

                        string updateQuery = @"
                    UPDATE DieselLedgerDebit
                    SET Did = @newDealerId, AmounGiven = @newAmountPaid, Date = @paymentDate, Note = @note
                    WHERE LedgerID = @LedgerID";
                        var ht = new Hashtable
                        {
                            { "@newDealerId", newDealerId },
                            { "@newAmountPaid", newAmountPaid },
                            { "@paymentDate", paymentDate.ToString("yyyy-MM-dd") },
                            { "@note", note },
                            { "@LedgerID", LedgerID }
                        };
                        if (MainClass.ExecInTx(updateQuery, ht, conn, tx) <= 0) return false;

                        if (authOldDid != newDealerId)
                        {
                            if (authOldDid > 0 && authOldAmt != 0 &&
                                MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DDAmount = DDAmount - @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", authOldDid }, { "@amount", authOldAmt } },
                                    conn, tx) <= 0)
                                return false;
                            if (newDealerId > 0 && newAmountPaid != 0 &&
                                MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DDAmount = DDAmount + @amount WHERE Did = @dealerId",
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
                                    ? "UPDATE AddDealer SET DDAmount = DDAmount + @amount WHERE Did = @dealerId"
                                    : "UPDATE AddDealer SET DDAmount = DDAmount - @amount WHERE Did = @dealerId";
                                if (MainClass.ExecInTx(q,
                                        new Hashtable { { "@dealerId", newDealerId }, { "@amount", Math.Abs(difference) } },
                                        conn, tx) <= 0)
                                    return false;
                            }
                        }
                        SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "DieselLedgerDebit", "LedgerID", LedgerID);
                        return true;
                    });
                    if (!ok)
                    {
                        ErrorFormMessage err = new ErrorFormMessage("ڈیٹا اپڈیٹ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        err.ShowDialog();
                        return;
                    }

                    CustomeMessage successMsg = new CustomeMessage("ریکارڈ کامیابی سے اپڈیٹ ہوگیا۔", "ZAIB PETROLEUM SERVICE");
                    successMsg.ShowDialog();
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

        private bool InsertLedgerRecord(int dealerId, decimal amountPaid, string note, DateTime paymentDate)
        {
            string insertQuery = @"
                    INSERT INTO DieselLedgerDebit (Did, AmounGiven, Date, Note) 
                    VALUES (@dealerId, @amountPaid, @paymentDate, @note)";

            Hashtable ht = new Hashtable();
            ht.Add("@dealerId", dealerId);
            ht.Add("@amountPaid", amountPaid);
            ht.Add("@paymentDate", paymentDate.ToString("yyyy-MM-dd"));
            ht.Add("@note", note);

            int insertResult = MainClass.DataInsertUpdateDelete(insertQuery, ht);
            if (insertResult > 0) return true;

            ErrorFormMessage err = new ErrorFormMessage("ڈیٹا محفوظ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
            err.ShowDialog();
            return false;
        }

        private bool UpdateLedgerRecord(int ledgerId, int dealerId, decimal amountPaid, string note, DateTime paymentDate)
        {
            string updateQuery = @"
                    UPDATE DieselLedgerDebit
                    SET Did = @newDealerId, AmounGiven = @newAmountPaid, Date = @paymentDate, Note = @note
                    WHERE LedgerID = @LedgerID";

            Hashtable ht = new Hashtable();
            ht.Add("@newDealerId", dealerId);
            ht.Add("@newAmountPaid", amountPaid);
            ht.Add("@paymentDate", paymentDate.ToString("yyyy-MM-dd"));
            ht.Add("@note", note);
            ht.Add("@LedgerID", ledgerId);

            int updateResult = MainClass.DataInsertUpdateDelete(updateQuery, ht);
            if (updateResult > 0) return true;

            ErrorFormMessage err = new ErrorFormMessage("ڈیٹا اپڈیٹ کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
            err.ShowDialog();
            return false;
        }

        // Yeh helper method new amount add karne ke liye
        private void AddDDAmount(int dealerId, decimal amount)
        {
            if (dealerId <= 0 || amount == 0) return;

            Hashtable ht = new Hashtable();
            ht.Add("@dealerId", dealerId);
            ht.Add("@amount", amount);

            string updateQuery = "UPDATE AddDealer SET DDAmount = DDAmount + @amount WHERE Did = @dealerId";
            MainClass.DataInsertUpdateDelete(updateQuery, ht);
        }

        // Yeh helper method amount minus karne ke liye
        private void SubtractDDAmount(int dealerId, decimal amount)
        {
            if (dealerId <= 0 || amount == 0) return;

            Hashtable ht = new Hashtable();
            ht.Add("@dealerId", dealerId);
            ht.Add("@amount", amount);

            string updateQuery = "UPDATE AddDealer SET DDAmount = DDAmount - @amount WHERE Did = @dealerId";
            MainClass.DataInsertUpdateDelete(updateQuery, ht);
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

                AutoCompleteStringCollection autoSource = new AutoCompleteStringCollection();
                foreach (DataRow row in dt.Rows)
                {
                    autoSource.Add(row["DealerName"].ToString());
                }
                txtDealerName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                txtDealerName.AutoCompleteSource = AutoCompleteSource.CustomSource;
                txtDealerName.AutoCompleteCustomSource = autoSource;
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

        private void cbDealer_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbDealer.SelectedItem != null)
            {
                int dealerId = Convert.ToInt32((cbDealer.SelectedItem as DataRowView)["Did"]);
                ShowDealerBalance(dealerId);
            }
        }

        private void txtAmountPaid_TextChanged(object sender, EventArgs e)
        {
            string currentText = txtAmountPaid.Text.Replace(",", "");

            if (decimal.TryParse(currentText, out decimal value))
            {
                txtAmountPaid.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                txtAmountPaid.SelectionStart = txtAmountPaid.Text.Length;
            }
        }

        public override void btnDel_Click(object sender, EventArgs e)
        {
            if (LedgerID > 0)
            {
                YesOrNoMessage confirmDelete = new YesOrNoMessage(
                    "کیا آپ واقعی اس ریکارڈ کو حذف کرنا چاہتے ہیں؟", "حذف کی تصدیق کریں");
                if (confirmDelete.ShowDialog() == DialogResult.Yes)
                {
                    bool ok = MainClass.RunInTransaction((conn, tx) =>
                    {
                        DataRow old = LocalPersistence.ReadRow("DieselLedgerDebit", "LedgerID", LedgerID, conn, tx);
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
                                "DELETE FROM DieselLedgerDebit WHERE LedgerID = @LedgerID",
                                new Hashtable { { "@LedgerID", LedgerID } },
                                conn, tx) <= 0)
                            return false;

                        LocalPersistence.EnsureTombstone(syncId, "zaib_dealer_direct", conn, tx, expectedRev);

                        if (dealerId > 0 && amountToDelete != 0)
                        {
                            if (MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DDAmount = DDAmount - @amount WHERE Did = @dealerId",
                                    new Hashtable { { "@dealerId", dealerId }, { "@amount", amountToDelete } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        SupabaseSyncService.DeleteBalanceMarkerOnly(conn, tx, syncId);
                        return true;
                    });

                    if (ok)
                    {
                        CustomeMessage successMessage = new CustomeMessage(
                            "ریکارڈ کامیابی کے ساتھ حذف کر دیا گیا اور ڈیلر کا DDAmount اپ ڈیٹ ہو گیا۔",
                            "ZAIB PETROLEUM SERVICE");
                        successMessage.ShowDialog();
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        ErrorFormMessage errorMessage = new ErrorFormMessage(
                            "ڈیزل لیجر سے ریکارڈ ختم کرنے میں خرابی آئی۔", "ZAIB PETROLEUM SERVICE");
                        errorMessage.ShowDialog();
                    }
                }
            }
            else
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage(
                    "براہ کرم حذف کرنے کے لیے ایک درست ریکارڈ منتخب کریں۔", "ZAIB PETROLEUM SERVICE");
                errorMessage.ShowDialog();
            }
        }
    }
}
