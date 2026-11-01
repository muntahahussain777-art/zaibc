using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Data;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public partial class frmStockAdd : SampleAdd1
    {
        public frmStockAdd()
        {
            InitializeComponent();
            SetupMinusLitterControls();
            txtvehicle.Leave += txtvehicle_Leave;
            txtvehicle.KeyDown += txtvehicle_KeyDown;
        }

        public int id = 0;

        // ===== NAYA CODE: purani values store karne ke liye =====
        private decimal oldAddDisel = 0m;
        private decimal oldRate = 0m;
        private int oldDealerId = 0;
        // Vehicle suggest ke baad user dealer change kar sake
        private bool _suggestingDealer;
        private bool _dealerPickedManually;
        private string _lastSuggestedVehicle = "";
        // ========================================================

        private void frmStockAdd_Load(object sender, EventArgs e)
        {
            SetupMinusLitterControls();
            ApplyDealerAmountVipMode();

            PopulateDealerNames();           // Existing code
            if (id == 0)
                txtdate.Value = DateTime.Now;    // Existing code

            if (id > 0)
            {
                LoadExistingRecord(id);
            }

            txtAddDisel.Leave += new EventHandler(txtAddDisel_Leave);
            txtAddDisel.TextChanged += new EventHandler(txtAddDisel_TextChanged);

            // NAYA: rate change pe bhi preview amount update
            txtRate.TextChanged += txtRate_TextChanged;

            this.KeyPreview = true;  // Taki Ctrl+1, Ctrl+2 capture ho sake
            this.KeyDown -= frmStockAdd_KeyDown;
            this.KeyDown += frmStockAdd_KeyDown; // KeyDown event attach

            txtDealerName.Visible = false; // By default hidden
            txtDealerName.TextChanged -= txtDealerName_TextChanged;
            txtDealerName.TextChanged += txtDealerName_TextChanged; // Apna event
            if (cbDealer != null)
            {
                cbDealer.SelectedIndexChanged -= CbDealer_UserChanged;
                cbDealer.SelectedIndexChanged += CbDealer_UserChanged;
                cbDealer.SelectionChangeCommitted -= CbDealer_UserChanged;
                cbDealer.SelectionChangeCommitted += CbDealer_UserChanged;
                cbDealer.Enabled = true;
            }

            BeginInvoke(new Action(() =>
            {
                ApplyDealerVipLayout();
                txtvehicle.Focus();
                txtvehicle.Select();
            }));
        }

        private void CbDealer_UserChanged(object sender, EventArgs e)
        {
            MarkDealerPickedManually();
        }

        private void txtvehicle_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    // Sirf suggest — pehle se selected / user-changed dealer overwrite NAHI
                    SuggestDealerByVehicle();
                    // Roznamcha (StockDiesel) se Location / Item / Vehicle Model
                    SuggestVipFromRoznamchaVehicle();
                    // Vehicle → seedha Amount (phir Tab se Credit)
                    try { FocusDealerVipAfterVehicle(); } catch { }
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtvehicle_KeyDown): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtvehicle_Leave(object sender, EventArgs e)
        {
            // Dealer overwrite nahi — sirf Roznamcha VIP fields
            try { SuggestVipFromRoznamchaVehicle(); } catch { }
        }

        /// <summary>
        /// Vehicle se dealer suggest. Agar dealer pehle se selected hai ya user ne change kiya
        /// to kabhi overwrite nahi — wahi dealer save hoga jo combo mein dikh raha hai.
        /// </summary>
        private void SuggestDealerByVehicle()
        {
            try
            {
                string vehicle = (txtvehicle.Text ?? "").Trim();
                if (string.IsNullOrEmpty(vehicle))
                    return;

                bool hasDealer = cbDealer != null && cbDealer.SelectedValue != null && cbDealer.SelectedIndex >= 0;
                bool sameVehicle = string.Equals(vehicle, _lastSuggestedVehicle, StringComparison.OrdinalIgnoreCase);

                // Same vehicle + dealer pehle se set → kabhi overwrite nahi (user change safe)
                if (hasDealer && sameVehicle)
                    return;

                // Nayi vehicle lekin user ne dealer khud choose kiya → uska dealer rehne do
                if (hasDealer && _dealerPickedManually)
                    return;

                DateTime entryDate = DateTime.Today;
                try
                {
                    if (txtdate != null)
                        entryDate = txtdate.Value.Date;
                }
                catch { entryDate = DateTime.Today; }

                // Same date latest → else on/before date → else overall latest (COUNT nahi)
                string qry = @"
            SELECT 
                s.DealerId,
                d.DealerName
            FROM AddStock s
            INNER JOIN AddDealer d ON d.Did = s.DealerId
            WHERE TRIM(IFNULL(s.Vehicle, '')) = @veh COLLATE NOCASE
            ORDER BY
              CASE WHEN date(s.Date) = date(@entryDate) THEN 0 ELSE 1 END,
              CASE WHEN date(s.Date) <= date(@entryDate) THEN 0 ELSE 1 END,
              date(s.Date) DESC,
              s.Sid DESC
            LIMIT 1";

                Hashtable ht = new Hashtable
                {
                    { "@veh", vehicle },
                    { "@entryDate", entryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                };
                DataTable dt = MainClass.ExecuteSelectQuery(qry, ht);

                if (dt != null && dt.Rows.Count > 0)
                {
                    int dealerId = Convert.ToInt32(dt.Rows[0]["DealerId"]);
                    string dealerName = dt.Rows[0]["DealerName"].ToString();

                    _suggestingDealer = true;
                    if (cbDealer.DataSource != null)
                        cbDealer.SelectedValue = dealerId;
                    if (txtDealerName != null)
                        txtDealerName.Text = dealerName;
                    _suggestingDealer = false;

                    _lastSuggestedVehicle = vehicle;
                    _dealerPickedManually = false;
                }
            }
            catch (Exception ex)
            {
                _suggestingDealer = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (SuggestDealerByVehicle): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        /// <summary>Save/Delete ke baad Dealer Name + Date fix — jab tak user change na kare.</summary>
        private void ResetAfterSaveKeepDealerAndDate()
        {
            int keepDid = 0;
            string keepName = "";
            DateTime keepDate = DateTime.Now;
            try
            {
                if (cbDealer != null && cbDealer.SelectedValue != null && cbDealer.SelectedValue != DBNull.Value
                    && !(cbDealer.SelectedValue is DataRowView))
                    keepDid = Convert.ToInt32(cbDealer.SelectedValue);
                else if (cbDealer != null && cbDealer.SelectedItem is DataRowView drv && drv["Did"] != DBNull.Value)
                    keepDid = Convert.ToInt32(drv["Did"]);
            }
            catch { }
            try { keepName = (txtDealerName != null ? txtDealerName.Text : "") ?? ""; } catch { }
            try { if (txtdate != null) keepDate = txtdate.Value; } catch { }

            MainClass.Enable_reset_keep_date(this, txtdate);
            ResetVipFieldsAfterSave();

            try { if (txtdate != null) txtdate.Value = keepDate; } catch { }
            try
            {
                if (keepDid > 0 && cbDealer != null && cbDealer.DataSource != null)
                {
                    _suggestingDealer = true;
                    cbDealer.SelectedValue = keepDid;
                    _suggestingDealer = false;
                    _dealerPickedManually = true;
                }
                if (txtDealerName != null && !string.IsNullOrWhiteSpace(keepName))
                    txtDealerName.Text = keepName.Trim();
            }
            catch { _suggestingDealer = false; }

            try { RefreshDealerVehicleAutoSuggest(); } catch { }
        }

        private void MarkDealerPickedManually()
        {
            if (_suggestingDealer) return;
            _dealerPickedManually = true;
        }

        /// <summary>Save se pehle jo dealer combo mein selected hai wahi ID.</summary>
        private int ResolveSelectedDealerId()
        {
            if (cbDealer?.SelectedValue != null)
            {
                try { return Convert.ToInt32(cbDealer.SelectedValue); }
                catch { }
            }
            // Ctrl+1 text mode: naam se resolve
            string name = (txtDealerName != null && txtDealerName.Visible)
                ? (txtDealerName.Text ?? "").Trim()
                : (cbDealer?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return 0;
            DataTable dt = cbDealer?.DataSource as DataTable;
            if (dt == null) return 0;
            foreach (DataRow row in dt.Rows)
            {
                if (string.Equals((row["DealerName"]?.ToString() ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt32(row["Did"]);
            }
            return 0;
        }

        private void frmStockAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.D1) // Ctrl + 1 — text mode
                {
                    e.SuppressKeyPress = true;
                    EnterDealerVipFastText();
                }
                else if (e.KeyCode == Keys.D2) // Ctrl + 2 — combo mode
                {
                    e.SuppressKeyPress = true;
                    ExitDealerVipFastText();
                }
            }
        }

        private void LoadExistingRecord(int id)
        {
            string query = "SELECT * FROM AddStock WHERE Sid = @id";
            Hashtable parameters = new Hashtable();
            parameters.Add("@id", id);

            try
            {
                DataTable dt = MainClass.ExecuteSelectQuery(query, parameters);

                if (dt != null && dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];
                    txtvehicle.Text = row["Vehicle"].ToString();
                    txtRate.Text = row["Rate"].ToString();
                    LoadLitterBoxesFromStored(Convert.ToDecimal(row["AddDisel"]));
                    txtdate.Value = Convert.ToDateTime(row["Date"]);
                    cbDealer.SelectedValue = row["DealerId"];
                    txtNote.Text = row["Note"].ToString();

                    // ===== purani values yahan save kar rahe =====
                    oldDealerId = Convert.ToInt32(row["DealerId"]);
                    oldRate = Convert.ToDecimal(row["Rate"]);
                    oldAddDisel = Convert.ToDecimal(row["AddDisel"]);
                    // ========================================================

                    LoadVipFieldsFromRow(row);
                    if (!_amountManual)
                        UpdateLitterRateAmount();
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
            string query = "SELECT Did, DealerName FROM AddDealer LIMIT 1000"; // Existing
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
            MarkDealerPickedManually();
            string enteredName = txtDealerName.Text.Trim();
            if (!string.IsNullOrEmpty(enteredName))
            {
                DataTable dt = cbDealer.DataSource as DataTable;
                if (dt != null)
                {
                    // Single quote handle
                    string filter = $"DealerName = '{enteredName.Replace("'", "''")}'";
                    DataRow[] rows = dt.Select(filter);

                    if (rows.Length > 0)
                    {
                        _suggestingDealer = true;
                        cbDealer.SelectedValue = rows[0]["Did"];
                        _suggestingDealer = false;
                    }
                    else
                    {
                        _suggestingDealer = true;
                        cbDealer.SelectedIndex = -1;
                        _suggestingDealer = false;
                    }
                }
            }
            else
            {
                _suggestingDealer = true;
                cbDealer.SelectedIndex = -1;
                _suggestingDealer = false;
            }
        }

        // ============= NAYA HELPER: Litter * Rate preview =============
        private void UpdateLitterRateAmount()
        {
            try
            {
                if (_amountManual) return;

                decimal addLitter = ParseLitterOrZero(txtAddDisel.Text);
                decimal minusLitter = ParseLitterOrZero(txtMinusDisel?.Text);
                decimal netLitter = addLitter - minusLitter;
                string rateText = txtRate.Text.Trim();

                if (netLitter == 0 || string.IsNullOrWhiteSpace(rateText))
                {
                    if (!_amountManual)
                        txtlitterRateAmount.Text = string.Empty;
                    return;
                }

                if (decimal.TryParse(rateText.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate))
                {
                    decimal amount = netLitter * rate;
                    txtlitterRateAmount.TextChanged -= TxtAmount_TextChanged;
                    txtlitterRateAmount.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount);
                    txtlitterRateAmount.TextChanged += TxtAmount_TextChanged;
                }
            }
            catch
            {
                if (!_amountManual)
                    txtlitterRateAmount.Text = string.Empty;
            }
        }
        // =============================================================

        // ================== SAVE BUTTON ==================
        protected override void btnSave_Click(object sender, EventArgs e)
        {
            // Jo dealer ab combo mein selected hai — wahi save (vehicle suggest overwrite nahi)
            int dealerId = ResolveSelectedDealerId();
            if (dealerId <= 0)
            {
                CustomeMessage validationMessage = new CustomeMessage("Valid dealer select karein!", "Warning");
                validationMessage.ShowDialog();
                return;
            }

            if (!DateTime.TryParse(txtdate.Text, out DateTime specifiedDate))
            {
                CustomeMessage validationMessage = new CustomeMessage("Date valid nahi hai!", "Warning");
                validationMessage.ShowDialog();
                return;
            }

            decimal.TryParse((txtRate.Text ?? "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate);

            decimal addDisel = 0m;
            try { addDisel = GetNetLitterForSave(out string litterError); if (!string.IsNullOrEmpty(litterError)) addDisel = ParseLitterOrZero(txtAddDisel.Text); }
            catch { addDisel = ParseLitterOrZero(txtAddDisel.Text); }

            decimal credit = GetVipCredit();
            decimal amount = GetVipAmountDirect();
            if (amount <= 0 && addDisel != 0 && rate > 0)
                amount = addDisel * rate;

            if (amount <= 0 && credit <= 0 && addDisel == 0)
            {
                CustomeMessage validationMessage = new CustomeMessage("Litter+Rate, Amount, ya Credit daalein!", "Warning");
                validationMessage.ShowDialog();
                return;
            }
            // Net effect on DDAmount = Amount − Credit (Dealer Payout credit yahan bhi)
            decimal newAmount = amount - credit;

            string saleLoc = GetVipSaleLocation();
            string saleItem = GetVipSaleItem();
            string saleModel = GetVipSaleVehicleModel();
            string saleInOut = GetVipSaleInOut();

            string qry;
            if (id == 0)
            {
                qry = "INSERT INTO AddStock (Vehicle, Rate, AddDisel, Date, DealerId, Note, Credit, Amount, SaleLocation, SaleItem, SaleVehicleModel, SaleInOut) " +
                      "VALUES (@vehicle, @rate, @adddisel, @date, @did, @note, @credit, @amount, @saleLoc, @saleItem, @saleModel, @saleInOut)";
            }
            else
            {
                qry = "UPDATE AddStock SET Vehicle = @vehicle, Rate = @rate, AddDisel = @adddisel, " +
                      "Date = @date, DealerId = @did, Note = @note, Credit = @credit, Amount = @amount, " +
                      "SaleLocation=@saleLoc, SaleItem=@saleItem, SaleVehicleModel=@saleModel, SaleInOut=@saleInOut WHERE Sid = @id";
            }

            Hashtable ht = new Hashtable
            {
                { "@id", id },
                { "@did", dealerId },
                { "@vehicle", string.IsNullOrWhiteSpace(txtvehicle.Text) ? (object)DBNull.Value : txtvehicle.Text },
                { "@rate", rate },
                { "@adddisel", addDisel },
                { "@date", specifiedDate.ToString("yyyy-MM-dd") },
                { "@note", string.IsNullOrWhiteSpace(txtNote.Text) ? (object)DBNull.Value : txtNote.Text },
                { "@credit", credit },
                { "@amount", amount },
                { "@saleLoc", string.IsNullOrWhiteSpace(saleLoc) ? (object)DBNull.Value : saleLoc },
                { "@saleItem", string.IsNullOrWhiteSpace(saleItem) ? (object)DBNull.Value : saleItem },
                { "@saleModel", string.IsNullOrWhiteSpace(saleModel) ? (object)DBNull.Value : saleModel },
                { "@saleInOut", saleInOut }
            };

            try
            {
                int saveId = id;
                bool ok = MainClass.RunInTransaction((conn, tx) =>
                {
                    if (saveId == 0)
                    {
                        if (MainClass.ExecInTx(qry, ht, conn, tx) <= 0) return false;
                        if (dealerId > 0 && newAmount != 0)
                        {
                            if (MainClass.ExecInTx(
                                    "UPDATE AddDealer SET DDAmount = DDAmount + @ddAmount WHERE Did = @Did",
                                    new Hashtable { { "@Did", dealerId }, { "@ddAmount", newAmount } },
                                    conn, tx) <= 0)
                                return false;
                        }
                        long sid = Convert.ToInt64(LocalPersistence.Scalar("SELECT last_insert_rowid()", null, conn, tx));
                        SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "AddStock", "Sid", sid);
                        return true;
                    }

                    DataRow old = LocalPersistence.ReadRow("AddStock", "Sid", saveId, conn, tx);
                    if (old == null) return false;
                    int authOldDealerId = Convert.ToInt32(old["DealerId"]);
                    decimal oldAmount = StockRowNetAmount(old);

                    if (MainClass.ExecInTx(qry, ht, conn, tx) <= 0) return false;

                    if (dealerId == authOldDealerId)
                    {
                        if (MainClass.ExecInTx(
                                "UPDATE AddDealer SET DDAmount = DDAmount - @oldAmount + @newAmount WHERE Did = @Did",
                                new Hashtable { { "@oldAmount", oldAmount }, { "@newAmount", newAmount }, { "@Did", dealerId } },
                                conn, tx) <= 0)
                            return false;
                    }
                    else
                    {
                        if (authOldDealerId > 0 &&
                            MainClass.ExecInTx(
                                "UPDATE AddDealer SET DDAmount = DDAmount - @oldAmount WHERE Did = @OldDid",
                                new Hashtable { { "@oldAmount", oldAmount }, { "@OldDid", authOldDealerId } },
                                conn, tx) <= 0)
                            return false;
                        if (dealerId > 0 &&
                            MainClass.ExecInTx(
                                "UPDATE AddDealer SET DDAmount = DDAmount + @newAmount WHERE Did = @NewDid",
                                new Hashtable { { "@newAmount", newAmount }, { "@NewDid", dealerId } },
                                conn, tx) <= 0)
                            return false;
                    }
                    SupabaseSyncService.RecordLocalChildBalanceEffect(conn, tx, "AddStock", "Sid", saveId);
                    return true;
                });

                if (ok)
                {
                    CustomeMessage successMessage = new CustomeMessage("Entry save ho gayi!", "Success");
                    successMessage.ShowDialog();
                    ResetAfterSaveKeepDealerAndDate();
                    id = 0;
                    try { txtvehicle.Focus(); txtvehicle.Select(); } catch { }
                }
                else
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("Data save nahi hua!", "Error");
                    errorMessage.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }
        // ================== btnSave_Click yahan tak ==================


        public override void btnDel_Click(object sender, EventArgs e)
        {
            if (id > 0)
            {
                string dealerIdQuery = "SELECT DealerId, Rate, AddDisel FROM AddStock WHERE Sid = @id";
                Hashtable htGetDealerId = new Hashtable();
                htGetDealerId.Add("@id", id);

                int dealerId = 0;
                decimal addDisel = 0;
                decimal rate = 0;

                try
                {
                    DataTable dealerIdTable = MainClass.ExecuteSelectQuery(dealerIdQuery, htGetDealerId);
                    if (dealerIdTable != null && dealerIdTable.Rows.Count > 0)
                    {
                        dealerId = Convert.ToInt32(dealerIdTable.Rows[0]["DealerId"]);
                        rate = Convert.ToDecimal(dealerIdTable.Rows[0]["Rate"]);
                        addDisel = Convert.ToDecimal(dealerIdTable.Rows[0]["AddDisel"]);
                    }
                    else
                    {
                        CustomeMessage noDataMessage = new CustomeMessage("Koi record nahi mila ID ke liye!", "Warning");
                        noDataMessage.ShowDialog();
                        return;
                    }

                    YesOrNoMessage confirmDelete = new YesOrNoMessage("Kya aap is record ko delete karna chahte hain?", "Confirm Delete");
                    if (confirmDelete.ShowDialog() == DialogResult.Yes)
                    {
                        bool ok = MainClass.RunInTransaction((conn, tx) =>
                        {
                            DataRow old = LocalPersistence.ReadRow("AddStock", "Sid", id, conn, tx);
                            if (old == null) return false;
                            int delDealerId = Convert.ToInt32(old["DealerId"]);
                            string syncId = old.Table.Columns.Contains("SyncId") ? old["SyncId"]?.ToString() : null;
                            long? expectedRev = null;
                            if (old.Table.Columns.Contains("ServerRev") && old["ServerRev"] != DBNull.Value)
                            {
                                try { long v = Convert.ToInt64(old["ServerRev"]); if (v > 0) expectedRev = v; } catch { }
                            }
                            decimal ddAmountToSubtract = StockRowNetAmount(old);

                            if (MainClass.ExecInTx(
                                    "DELETE FROM AddStock WHERE Sid = @id",
                                    new Hashtable { { "@id", id } },
                                    conn, tx) <= 0)
                                return false;

                            LocalPersistence.EnsureTombstone(syncId, "zaib_dealer_purchases", conn, tx, expectedRev);

                            if (delDealerId > 0 && ddAmountToSubtract != 0)
                            {
                                if (MainClass.ExecInTx(
                                        "UPDATE AddDealer SET DDAmount = DDAmount + @ddAmount WHERE Did = @Did",
                                        new Hashtable { { "@Did", delDealerId }, { "@ddAmount", -ddAmountToSubtract } },
                                        conn, tx) <= 0)
                                    return false;
                            }
                            SupabaseSyncService.DeleteBalanceMarkerOnly(conn, tx, syncId);
                            return true;
                        });

                        if (ok)
                        {
                            CustomeMessage successMessage = new CustomeMessage("Record delete ho gaya!", "Success");
                            successMessage.ShowDialog();
                            ResetAfterSaveKeepDealerAndDate();
                            id = 0;
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

        /// <summary>AddStock row net = Amount − Credit (Amount fallback Litter×Rate).</summary>
        private static decimal StockRowNetAmount(DataRow row)
        {
            if (row == null) return 0m;
            decimal amount = 0m;
            if (row.Table.Columns.Contains("Amount") && row["Amount"] != DBNull.Value)
                amount = Convert.ToDecimal(row["Amount"]);
            if (amount == 0m)
            {
                decimal r = row.Table.Columns.Contains("Rate") && row["Rate"] != DBNull.Value ? Convert.ToDecimal(row["Rate"]) : 0m;
                decimal l = row.Table.Columns.Contains("AddDisel") && row["AddDisel"] != DBNull.Value ? Convert.ToDecimal(row["AddDisel"]) : 0m;
                amount = r * l;
            }
            decimal credit = 0m;
            if (row.Table.Columns.Contains("Credit") && row["Credit"] != DBNull.Value)
                credit = Convert.ToDecimal(row["Credit"]);
            return amount - credit;
        }

        private void UpdateDealerDAmount(int Did, decimal ddAmount)
        {
            string qry = "UPDATE AddDealer SET DDAmount = DDAmount + @ddAmount WHERE Did = @Did";
            Hashtable ht = new Hashtable();
            ht.Add("@Did", Did);
            ht.Add("@ddAmount", ddAmount);

            try
            {
                int result = MainClass.DataInsertUpdateDelete(qry, ht);
                if (result > 0)
                {
                    CustomeMessage successMessage = new CustomeMessage("AddDealer amount update ho gaya!", "Success");
                    successMessage.ShowDialog();
                }
                else
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("AddDealer amount update nahi hua!", "Error");
                    errorMessage.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtAddDisel_Leave(object sender, EventArgs e)
        {
            FormatPositiveLitterBox(txtAddDisel);
            UpdateLitterRateAmount();
        }

        private void txtAddDisel_TextChanged(object sender, EventArgs e)
        {
            FormatPositiveLitterBox(txtAddDisel);
            _amountManual = false;
            UpdateLitterRateAmount();
        }

        private void txtAddDisel_KeyPress(object sender, KeyPressEventArgs e)
        {
            PositiveLitter_KeyPress(sender, e);
        }

        private void txtRate_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Allow control characters (like backspace), digits, and one decimal point
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true; // Block invalid input
                CustomeMessage customMessageBox = new CustomeMessage("یہاں صرف نمبر اور دہائی کا نشان (.) لکھ سکتے ہو!", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            // Block multiple decimal points
            TextBox textBox = sender as TextBox;
            if (e.KeyChar == '.' && textBox != null && textBox.Text.Contains("."))
            {
                e.Handled = true; // Block if a decimal point already exists
            }
        }

        // NAYA: jab rate change ho, preview update
        private void txtRate_TextChanged(object sender, EventArgs e)
        {
            _amountManual = false;
            UpdateLitterRateAmount();
        }
    }
}