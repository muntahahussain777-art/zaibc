using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public partial class frmDiselAdd : SampleAdd
    {
        private List<string> existingCustomerNames = new List<string>();
        public int id = 0;
        private decimal TotalBalance = 0;
        private bool _loadingData; // LoadData ke dauran recalc Amount mat mitao
        private bool _syncingFields; // Amount/Balance sync loop rokne ke liye
        private string _lastAppliedVehicle = "";
        private bool _namePickedByUser; // user ne Name khud select kiya
        private bool _suppressNameChange; // programmatic Name clear/set

        public frmDiselAdd()
        {
            try
            {
                InitializeComponent();
                txtlitter.TextChanged += TriggerRecalculation;
                txtrate.TextChanged += TriggerRecalculation;
                txtlitter.Leave += txtlitter_Leave;
                txtrate.Leave += txtrate_Leave;
                txtadvance.TextChanged += TriggerRecalculation;
                txtcredit.TextChanged += TriggerRecalculation;
                this.KeyPreview = true;
                this.KeyDown += new KeyEventHandler(frmDiselAdd_KeyDown);
                PopulateCustomerNames();

                // ⭐ yeh do nayi lines:
                txtVehicle.Leave += txtVehicle_Leave;
                txtVehicle.KeyDown += txtVehicle_KeyDown;
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Constructor): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }


        private void frmDiselAdd_Load(object sender, EventArgs e)
        {
            try
            {
                ApplyDailyVipMode();
                LoadCustomers();

                if (id > 0)
                {
                    LoadData();
                }
                else
                {
                    txtdate.Value = DateTime.Now;
                    ClearCustomerNameSelection();
                    _namePickedByUser = false;
                    _lastAppliedVehicle = "";
                }
                txtbalance.TextChanged += new EventHandler(txtbalance_TextChanged);
                txtamount.TextChanged += new EventHandler(txtamount_TextChanged);
                txtadvance.TextChanged += new EventHandler(txtadvance_TextChanged);
                txtcredit.TextChanged += new EventHandler(txtcredit_TextChanged);
                txtamount.Leave += (s, ev) => SyncBalanceFromAmount();
                txtbalance.Leave += (s, ev) => SyncBalanceFromAmount(); // alag type kiya ho to Amount se wapas sync

                // Amount click/focus → In; Credit type → Out (vehicle pe In/Out free)
                txtamount.Click += (s, ev) => { try { AutoInWhenAmountFocus(); } catch { } };
                txtamount.Enter += (s, ev) => { try { AutoInWhenAmountFocus(); } catch { } };
                txtcredit.Click += (s, ev) =>
                {
                    try
                    {
                        decimal.TryParse((txtcredit.Text ?? "").Replace(",", "").Trim(), out decimal c);
                        if (c > 0) AutoOutWhenCreditEntered(c);
                    }
                    catch { }
                };
                txtcredit.Enter += (s, ev) =>
                {
                    try
                    {
                        decimal.TryParse((txtcredit.Text ?? "").Replace(",", "").Trim(), out decimal c);
                        if (c > 0) AutoOutWhenCreditEntered(c);
                    }
                    catch { }
                };

                SetupCustomerCreditDisplay();
                RefreshCustomerCreditDisplay();

                BeginInvoke(new Action(() =>
                {
                    txtVehicle.Focus();
                    txtVehicle.Select();
                }));
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Form Load): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }
        // Enter / Tab: vehicle apply → seedha Amount (phir Tab se Credit)
        private void txtVehicle_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    ApplyVehicleEntry(force: true);
                    FocusDailyVipAfterVehicle();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtVehicle_KeyDown): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        // Leave: empty/changed vehicle pe apply; same vehicle + Name already picked → Name mat chhoo
        private void txtVehicle_Leave(object sender, EventArgs e)
        {
            try
            {
                ApplyVehicleEntry(force: false);
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtVehicle_Leave): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        /// <summary>
        /// Vehicle diya → sirf Location/Item/Model Roznamcha se.
        /// Name bilkul change nahi — jo customer select hai wahi rehta hai (kisi bhi vehicle pe).
        /// </summary>
        private void ApplyVehicleEntry(bool force)
        {
            if (_loadingData || id > 0) return;

            // Name snapshot — VIP suggest / clear se Name touch na ho
            object keepNameId = null;
            string keepNameText = "";
            bool keepName = false;
            try
            {
                if (cbName != null && cbName.SelectedValue != null && cbName.SelectedValue != DBNull.Value
                    && !(cbName.SelectedValue is DataRowView)
                    && int.TryParse(cbName.SelectedValue.ToString(), out int kid) && kid > 0)
                {
                    keepNameId = cbName.SelectedValue;
                    keepNameText = (cbName.Text ?? "").Trim();
                    keepName = true;
                }
                else if (txtName != null && !string.IsNullOrWhiteSpace(txtName.Text))
                {
                    keepNameText = txtName.Text.Trim();
                    keepName = true;
                }
            }
            catch { }

            string vehicle = (txtVehicle.Text ?? "").Trim();

            if (string.IsNullOrEmpty(vehicle))
            {
                try { ClearVipLocationItemModel(); } catch { }
                _lastAppliedVehicle = "";
                RestoreCustomerNameIfNeeded(keepName, keepNameId, keepNameText);
                return;
            }

            bool vehicleChanged = !string.Equals(vehicle, _lastAppliedVehicle, StringComparison.OrdinalIgnoreCase);

            // Same vehicle pe Leave/focus → kuch mat chhoo
            if (!force && !vehicleChanged)
                return;

            // Name mat chhoo — sirf Location / Item / Vehicle Model
            try { ClearVipLocationItemModel(); } catch { }
            try { SuggestVipFromRoznamchaVehicle(); } catch { }
            _lastAppliedVehicle = vehicle;
            RestoreCustomerNameIfNeeded(keepName, keepNameId, keepNameText);
        }

        /// <summary>Vehicle apply ke baad Name wapas — select kiya hua customer stick rahe.</summary>
        private void RestoreCustomerNameIfNeeded(bool keepName, object keepNameId, string keepNameText)
        {
            if (!keepName) return;
            try
            {
                _suppressNameChange = true;
                if (txtName != null && !string.IsNullOrEmpty(keepNameText))
                    txtName.Text = keepNameText;
                if (cbName != null && keepNameId != null)
                {
                    try { cbName.SelectedValue = keepNameId; }
                    catch
                    {
                        try
                        {
                            int idx = cbName.FindStringExact(keepNameText ?? "");
                            if (idx >= 0) cbName.SelectedIndex = idx;
                        }
                        catch { }
                    }
                }
                _namePickedByUser = true;
            }
            catch { }
            finally
            {
                _suppressNameChange = false;
            }
        }

        private object BlankCustomerIdValue()
        {
            try
            {
                DataTable dt = cbName != null ? cbName.DataSource as DataTable : null;
                if (dt != null && dt.Columns.Contains("Id"))
                    return Convert.ChangeType(0, dt.Columns["Id"].DataType);
            }
            catch { }
            return 0;
        }

        private void ClearCustomerNameSelection()
        {
            try
            {
                _suppressNameChange = true;
                if (txtName != null) txtName.Text = "";
                if (cbName != null && cbName.DataSource != null)
                {
                    // DropDownList: blank row (Id=0) — type SQLite Int64 match
                    try { cbName.SelectedValue = BlankCustomerIdValue(); }
                    catch
                    {
                        try { cbName.SelectedIndex = 0; } catch { }
                    }
                }
                try { RefreshCustomerCreditDisplay(); } catch { }
            }
            catch { }
            finally
            {
                _suppressNameChange = false;
            }
        }

        private static DataTable WithBlankCustomerRow(DataTable source)
        {
            DataTable dt = source != null ? source.Copy() : new DataTable();
            if (!dt.Columns.Contains("Id"))
                dt.Columns.Add("Id", typeof(long));
            if (!dt.Columns.Contains("Name"))
                dt.Columns.Add("Name", typeof(string));

            DataRow blank = dt.NewRow();
            blank["Id"] = Convert.ChangeType(0, dt.Columns["Id"].DataType);
            blank["Name"] = "";
            dt.Rows.InsertAt(blank, 0);
            return dt;
        }

        private void LoadCustomers()
        {
            try
            {
                string customerQry = "SELECT Id, Name FROM AddCustomer ORDER BY Name COLLATE NOCASE";
                DataTable customerData = MainClass.ExecuteSelectQuery(customerQry, null);
                customerData = WithBlankCustomerRow(customerData);

                _suppressNameChange = true;
                try
                {
                    cbName.DisplayMember = "Name";
                    cbName.ValueMember = "Id";
                    cbName.DataSource = customerData;
                    cbName.SelectedValue = BlankCustomerIdValue();
                }
                finally { _suppressNameChange = false; }
            }
            catch (Exception ex)
            {
                _suppressNameChange = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (LoadCustomers): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void frmDiselAdd_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Control)
                {
                    if (e.KeyCode == Keys.D1)
                    {
                        e.SuppressKeyPress = true;
                        EnterDailyVipFastTextMode();
                    }
                    else if (e.KeyCode == Keys.D2)
                    {
                        e.SuppressKeyPress = true;
                        ExitDailyVipFastTextMode();
                    }
                    else if (e.KeyCode == Keys.S)
                    {
                        btnSave_Click(sender, e);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (KeyDown): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void PopulateCustomerNames()
        {
            try
            {
                string query = "SELECT Id, Name FROM AddCustomer ORDER BY Name COLLATE NOCASE";
                DataTable dt = MainClass.ExecuteSelectQuery(query, null);
                dt = WithBlankCustomerRow(dt);

                if (dt.Rows.Count > 1)
                {
                    _suppressNameChange = true;
                    try
                    {
                        cbName.DataSource = dt;
                        cbName.DisplayMember = "Name";
                        cbName.ValueMember = "Id";
                        cbName.DropDownHeight = 200;
                        cbName.IntegralHeight = false;
                        cbName.SelectedValue = BlankCustomerIdValue();
                    }
                    finally { _suppressNameChange = false; }

                    AutoCompleteStringCollection customerNames = new AutoCompleteStringCollection();
                    foreach (DataRow row in dt.Rows)
                    {
                        string n = Convert.ToString(row["Name"]) ?? "";
                        if (!string.IsNullOrWhiteSpace(n))
                            customerNames.Add(n);
                    }

                    txtName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    txtName.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    txtName.AutoCompleteCustomSource = customerNames;

                    cbName.SelectedIndexChanged -= cbName_SelectedIndexChanged;
                    cbName.SelectedIndexChanged += cbName_SelectedIndexChanged;
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Koi customer data nahi mila!", "Warning");
                    noDataMessage.ShowDialog();
                    cbName.DataSource = null;
                    txtName.AutoCompleteCustomSource = null;
                }
            }
            catch (Exception ex)
            {
                _suppressNameChange = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (PopulateCustomerNames): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void cbName_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressNameChange || _loadingData) return;

                int selectedId = 0;
                try
                {
                    if (cbName.SelectedValue != null && cbName.SelectedValue != DBNull.Value
                        && !(cbName.SelectedValue is DataRowView))
                        int.TryParse(cbName.SelectedValue.ToString(), out selectedId);
                }
                catch { selectedId = 0; }

                // Blank row — Name empty state
                if (selectedId <= 0)
                {
                    _namePickedByUser = false;
                    try { RefreshCustomerCreditDisplay(); } catch { }
                    return;
                }

                // Agar txtName visible hai aur uska text cbName ke selected item se match karta hai
                if (txtName.Visible && !string.IsNullOrWhiteSpace(txtName.Text) && cbName.Text == txtName.Text.Trim())
                {
                    OnUserPickedCustomerName();
                    return;
                }

                OnUserPickedCustomerName();
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (cbName_SelectedIndexChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        /// <summary>
        /// Name user ne select kiya — vehicle ho ya na ho (Credit/Amount-only bhi).
        /// Name stick rahe; Amount/Credit pe change nahi.
        /// </summary>
        private void OnUserPickedCustomerName()
        {
            if (_suppressNameChange || _loadingData || id > 0) return;

            _namePickedByUser = true;
            try { RefreshCustomerCreditDisplay(); } catch { }
        }

        private void TriggerRecalculation(object sender, EventArgs e)
        {
            if (_loadingData || _syncingFields) return;
            try
            {
                _syncingFields = true;

                decimal litter = 0, rate = 0, credit = 0, amount = 0;

                bool hasLitter = decimal.TryParse((txtlitter.Text ?? "").Replace(",", ""), out litter) && litter > 0;
                bool hasRate = decimal.TryParse((txtrate.Text ?? "").Replace(",", ""), out rate) && rate > 0;
                // VIP: Advance remove — balance mein advance 0
                if (txtadvance != null) txtadvance.Text = "0";
                decimal.TryParse((txtcredit.Text ?? "").Replace(",", ""), out credit);
                bool isAmountValid = decimal.TryParse((txtamount.Text ?? "").Replace(",", ""), out amount);

                // Litter+Rate mode: Amount = Litter×Rate, Balance hamesha usi se
                if (hasLitter && hasRate)
                {
                    amount = litter * rate;
                    decimal finalBalance = amount - credit;
                    txtamount.Text = FmtMoney(amount);
                    txtbalance.Text = FmtMoney(finalBalance);
                }
                // Direct Amount mode: Balance = Amount − Credit
                else if (isAmountValid)
                {
                    decimal finalBalance = amount - credit;
                    txtbalance.Text = FmtMoney(finalBalance);
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (TriggerRecalculation): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
            finally
            {
                _syncingFields = false;
            }
        }

        /// Balance hamesha Amount se — manual alag Balance ignore / wapas sync.
        private void SyncBalanceFromAmount()
        {
            TriggerRecalculation(null, EventArgs.Empty);
        }

        private static string FmtMoney(decimal v)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0}", v);
        }

        private void LoadData()
        {
            try
            {
                _loadingData = true;
                string qry = "SELECT PetrolAdd.Date, PetrolAdd.ReceiptNo, PetrolAdd.vehicle, PetrolAdd.Litter, PetrolAdd.Rate, " +
                       "PetrolAdd.Advance, " +
                       "IFNULL(PetrolAdd.Amount, 0) AS Amount, " +
                       "IFNULL(PetrolAdd.Balance, 0) AS Balance, " +
                       "IFNULL(PetrolAdd.Credit, 0) AS Credit, PetrolAdd.Note, PetrolAdd.CustomerId, " +
                       "IFNULL(PetrolAdd.IsInitialEntry, 1) AS IsInitialEntry, " +
                       "IFNULL(PetrolAdd.SaleLocation,'') AS SaleLocation, " +
                       "IFNULL(PetrolAdd.SaleItem,'') AS SaleItem, " +
                       "IFNULL(PetrolAdd.SaleVehicleModel,'') AS SaleVehicleModel, " +
                       "IFNULL(PetrolAdd.SaleInOut,'In') AS SaleInOut " +
                       "FROM PetrolAdd " +
                       "INNER JOIN AddCustomer ON PetrolAdd.CustomerId = AddCustomer.Id " +
                       "WHERE PetrolAdd.pid = @id";

                Hashtable ht = new Hashtable();
                ht.Add("@id", id);

                DataTable dt = MainClass.ExecuteSelectQuery(qry, ht);
                if (dt != null && dt.Rows.Count > 0)
                {
                    DataRow row = dt.Rows[0];

                    if (DateTime.TryParse(Convert.ToString(row["Date"]), out DateTime loadedDate))
                        txtdate.Value = loadedDate.Date;
                    else
                        txtdate.Value = DateTime.Now;
                    txtrecipt.Text = "";
                    txtVehicle.Text = row["vehicle"].ToString();

                    // 0 litter/rate ko khali dikhao taake direct-amount mode rahe
                    decimal lit = row["Litter"] != DBNull.Value ? Convert.ToDecimal(row["Litter"]) : 0;
                    decimal rt = row["Rate"] != DBNull.Value ? Convert.ToDecimal(row["Rate"]) : 0;
                    txtlitter.Text = lit > 0 ? lit.ToString("G") : "";
                    txtrate.Text = rt > 0 ? FormatRate(rt) : "";
                    txtadvance.Text = "0";
                    LoadVipFieldsFromRow(row);

                    decimal amount = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0;
                    decimal credit = row["Credit"] != DBNull.Value ? Convert.ToDecimal(row["Credit"]) : 0;
                    decimal balance = row["Balance"] != DBNull.Value ? Convert.ToDecimal(row["Balance"]) : 0;

                    // Comma format — hazar / lac / crore clear (LoadData mein TextChanged skip hota hai)
                    txtamount.Text = FmtMoney(amount);
                    txtcredit.Text = FmtMoney(credit);
                    txtbalance.Text = FmtMoney(balance);

                    txtnote.Text = row["Note"].ToString();

                    if (cbName.Items.Count > 0)
                    {
                        cbName.SelectedValue = row["CustomerId"];
                    }

                    _lastAppliedVehicle = (txtVehicle.Text ?? "").Trim();
                    _namePickedByUser = true;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (LoadData): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
            finally
            {
                _loadingData = false;
                // Open pe Balance = Amount − Credit + Advance (mismatch na rahe)
                SyncBalanceFromAmount();
            }
        }

        public override void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                string customerName = string.Empty;

                if (txtName.Visible && !string.IsNullOrWhiteSpace(txtName.Text))
                {
                    customerName = txtName.Text;
                }
                else if (cbName.SelectedValue != null && cbName.SelectedValue != DBNull.Value
                    && int.TryParse(cbName.SelectedValue.ToString(), out int selId) && selId > 0)
                {
                    customerName = cbName.Text;
                }
                else
                {
                    CustomeMessage validationMessage = new CustomeMessage("Customer select karein ya naam daalein!", "Warning");
                    validationMessage.ShowDialog();
                    try
                    {
                        if (txtName != null && txtName.Visible) txtName.Focus();
                        else if (cbName != null) cbName.Focus();
                    }
                    catch { }
                    return;
                }
                // ... (baaki tumhara code same ka same)


                // VIP: Advance / Receipt band
                if (txtadvance != null) txtadvance.Text = "0";
                if (txtrecipt != null) txtrecipt.Text = "";

                // Litter / Rate / Amount / Credit optional — empty pe 0 (validation nahi)
                decimal credit = 0;
                decimal.TryParse((txtcredit.Text ?? "").Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out credit);

                decimal litter = 0;
                decimal.TryParse((txtlitter.Text ?? "").Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out litter);

                decimal rate = 0;
                decimal.TryParse((txtrate.Text ?? "").Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out rate);

                decimal amount = 0;
                decimal.TryParse((txtamount.Text ?? "").Replace(",", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);

                DateTime dateValue;
                try
                {
                    dateValue = txtdate != null ? txtdate.Value.Date : DateTime.Today;
                }
                catch
                {
                    if (!DateTime.TryParse(txtdate != null ? txtdate.Text : null, out dateValue))
                    {
                        CustomeMessage validationMessage = new CustomeMessage("Valid date daalein!", "Warning");
                        validationMessage.ShowDialog();
                        return;
                    }
                }

                int customerId = ResolveCustomerIdForSave(customerName);
                if (customerId <= 0)
                {
                    CustomeMessage validationMessage = new CustomeMessage(
                        "Customer select karein ya sahi naam likhein!\n(Milta-julta naam mix nahi hoga — exact naam chahiye.)",
                        "Warning");
                    validationMessage.ShowDialog();
                    return;
                }

                // ——— Normal SALE (IsInitialEntry=1) ———
                if (litter > 0 && rate > 0 && amount <= 0)
                    amount = litter * rate;

                decimal balance = amount - credit;
                decimal finalBalance = balance; // advance = 0 (VIP)
                TotalBalance += finalBalance;

                // Credit > 0 pe save se pehle bhi Out force (galti se In na save ho)
                try { AutoOutWhenCreditEntered(credit); } catch { }

                string saleLoc = GetVipSaleLocation();
                string saleItem = GetVipSaleItem();
                string saleModel = GetVipSaleVehicleModel();
                string saleInOut = GetVipSaleInOut();
                if (credit > 0 && !string.Equals(saleInOut, "Out", StringComparison.OrdinalIgnoreCase))
                    saleInOut = "Out";

                string qry;
                Hashtable htSave = new Hashtable();
                int editId = id;

                // Delete ke baad stale id → UPDATE 0 rows: pehle check
                if (editId > 0)
                {
                    try
                    {
                        DataTable exists = MainClass.ExecuteSelectQuery(
                            "SELECT pid FROM PetrolAdd WHERE pid=@pid LIMIT 1",
                            new Hashtable { { "@pid", editId } });
                        if (exists == null || exists.Rows.Count == 0)
                            editId = 0;
                    }
                    catch { editId = 0; }
                }

                bool isEdit = editId > 0;

                if (!isEdit)
                {
                    qry = "INSERT INTO PetrolAdd (CustomerId, Date, ReceiptNo, vehicle, Litter, Rate, Advance, Amount, Credit, Balance, Note, IsInitialEntry, SaleLocation, SaleItem, SaleVehicleModel, SaleInOut) " +
                          "VALUES (@customerId, @date, @recipt, @veh, @litter, @rate, @advance, @amount, @credit, @balance, @note, 1, @saleLoc, @saleItem, @saleModel, @saleInOut)";
                }
                else
                {
                    qry = "UPDATE PetrolAdd SET CustomerId = @customerId, Date = @date, ReceiptNo = @recipt, vehicle = @veh, " +
                          "Litter = @litter, Rate = @rate, Advance = @advance, Amount = @amount, Credit = @credit, " +
                          "Balance = @balance, Note = @note, IsInitialEntry = 1, " +
                          "SaleLocation=@saleLoc, SaleItem=@saleItem, SaleVehicleModel=@saleModel, SaleInOut=@saleInOut " +
                          "WHERE pid = @pid";
                    htSave.Add("@pid", editId);
                }

                htSave.Add("@customerId", customerId);
                htSave.Add("@date", dateValue.ToString("yyyy-MM-dd"));
                htSave.Add("@recipt", "");
                htSave.Add("@veh", txtVehicle.Text);
                htSave.Add("@litter", litter.ToString("F2"));
                htSave.Add("@rate", FormatRate(rate));
                htSave.Add("@advance", "0");
                htSave.Add("@amount", amount.ToString("F2"));
                htSave.Add("@credit", credit.ToString("F2"));
                htSave.Add("@balance", finalBalance.ToString("F2"));
                htSave.Add("@note", txtnote.Text);
                htSave.Add("@saleLoc", string.IsNullOrWhiteSpace(saleLoc) ? (object)DBNull.Value : saleLoc);
                htSave.Add("@saleItem", string.IsNullOrWhiteSpace(saleItem) ? (object)DBNull.Value : saleItem);
                htSave.Add("@saleModel", string.IsNullOrWhiteSpace(saleModel) ? (object)DBNull.Value : saleModel);
                htSave.Add("@saleInOut", saleInOut);

                int r = MainClass.DataInsertUpdateDelete(qry, htSave);

                // UPDATE fail (delete-then-save) → naya INSERT
                if (r <= 0 && isEdit)
                {
                    editId = 0;
                    isEdit = false;
                    id = 0;
                    htSave.Remove("@pid");
                    qry = "INSERT INTO PetrolAdd (CustomerId, Date, ReceiptNo, vehicle, Litter, Rate, Advance, Amount, Credit, Balance, Note, IsInitialEntry, SaleLocation, SaleItem, SaleVehicleModel, SaleInOut) " +
                          "VALUES (@customerId, @date, @recipt, @veh, @litter, @rate, @advance, @amount, @credit, @balance, @note, 1, @saleLoc, @saleItem, @saleModel, @saleInOut)";
                    r = MainClass.DataInsertUpdateDelete(qry, htSave);
                }

                if (r > 0)
                {
                    BalanceConfirmationService.RecalculateCustomerPetrolBalances(customerId);
                    CustomeMessage successMessage = new CustomeMessage(
                        isEdit ? "Entry update ho gayi!" : "Entry save ho gayi!", "Success");
                    successMessage.ShowDialog();

                    // Grid async refresh — save pe lag nahi
                    NotifyDiselViewRefresh(dateValue);

                    if (isEdit)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    // Name + Date fix (Dealer Amount jaisa)
                    ResetAfterSaveKeepNameAndDate();
                    id = 0;
                    TotalBalance = 0;
                    try { txtVehicle.Focus(); txtVehicle.Select(); } catch { }
                }
                else if (isEdit)
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage(
                        "Update nahi hua.", "Error");
                    errorMessage.ShowDialog();
                }
                else
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage(
                        "Entry save nahi hui!", "Error");
                    errorMessage.ShowDialog();
                }


            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Save): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        public override void btnDel_Click(object sender, EventArgs e)
        {
            try
            {
                if (id > 0)
                {
                    YesOrNoMessage confirmDelete = new YesOrNoMessage("Kya aap is record ko delete karna chahte hain?", "Confirm Delete");
                    if (confirmDelete.ShowDialog() == DialogResult.Yes)
                    {
                        try
                        {
                            int customerIdForRepair = 0;
                            try
                            {
                                var htC = new Hashtable { { "@pid", id } };
                                DataTable dtC = MainClass.ExecuteSelectQuery(
                                    "SELECT CustomerId FROM PetrolAdd WHERE pid=@pid LIMIT 1", htC);
                                if (dtC != null && dtC.Rows.Count > 0 && dtC.Rows[0][0] != DBNull.Value)
                                    customerIdForRepair = Convert.ToInt32(dtC.Rows[0][0]);
                            }
                            catch { }

                            int r = MainClass.DeleteWithTombstone("PetrolAdd", "pid", id, "zaib_petrol_entries");
                            if (r > 0)
                            {
                                if (customerIdForRepair > 0)
                                    BalanceConfirmationService.RecalculateCustomerPetrolBalances(customerIdForRepair);
                                CustomeMessage successMessage = new CustomeMessage("Record delete ho gaya!", "Success");
                                successMessage.ShowDialog();
                                DateTime keepDate = DateTime.Now;
                                try { if (txtdate != null) keepDate = txtdate.Value; } catch { }
                                ResetAfterSaveKeepNameAndDate();
                                id = 0;
                                NotifyDiselViewRefresh(keepDate);
                            }
                            else
                            {
                                ErrorFormMessage errorMessage = new ErrorFormMessage("Record delete nahi hua!", "Error");
                                errorMessage.ShowDialog();
                            }
                        }
                        catch (Exception exInner)
                        {
                            ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Delete Inner): " + exInner.Message, "Error");
                            errorMessage.ShowDialog();
                        }
                    }
                }
                else
                {
                    CustomeMessage noSelectionMessage = new CustomeMessage("Pehle ek record select karein!", "Warning");
                    noSelectionMessage.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (Delete): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        /// <summary>Save/Delete ke baad Daily Diesel grid refresh (Owner pehle — halka).</summary>
        private void NotifyDiselViewRefresh(DateTime entryDate)
        {
            try
            {
                ZaibPetroleumService.View.frmDiselView view =
                    Owner as ZaibPetroleumService.View.frmDiselView;

                if (view == null || view.IsDisposed)
                {
                    foreach (Form f in Application.OpenForms)
                    {
                        view = f as ZaibPetroleumService.View.frmDiselView;
                        if (view != null && !view.IsDisposed) break;
                        try
                        {
                            foreach (Control c in f.Controls)
                            {
                                if (c is ZaibPetroleumService.View.frmDiselView v && !v.IsDisposed)
                                {
                                    view = v;
                                    break;
                                }
                            }
                        }
                        catch { }
                        if (view != null) break;
                    }
                }

                if (view == null || view.IsDisposed) return;

                Action refresh = () =>
                {
                    try { view.RefreshAfterChildSave(entryDate); } catch { }
                };
                // Sync refresh — delete/save ke baad grid turant dikhe (Roznamcha jaisa)
                if (view.IsHandleCreated)
                {
                    if (view.InvokeRequired)
                        view.Invoke(refresh);
                    else
                        refresh();
                }
                else
                    refresh();
            }
            catch { }
        }

        private void txtbalance_TextChanged(object sender, EventArgs e)
        {
            if (_loadingData || _syncingFields) return;
            try
            {
                string currentText = txtbalance.Text.Replace(",", "");
                if (decimal.TryParse(currentText, out decimal value))
                {
                    _syncingFields = true;
                    txtbalance.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                    txtbalance.SelectionStart = txtbalance.Text.Length;
                    _syncingFields = false;
                }
            }
            catch (Exception ex)
            {
                _syncingFields = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtbalance_TextChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtcredit_TextChanged(object sender, EventArgs e)
        {
            if (_loadingData || _syncingFields) return;
            try
            {
                string currentText = txtcredit.Text.Replace(",", "");
                if (decimal.TryParse(currentText, out decimal value))
                {
                    _syncingFields = true;
                    txtcredit.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                    txtcredit.SelectionStart = txtcredit.Text.Length;
                    _syncingFields = false;
                    SyncBalanceFromAmount();
                    // Credit amount → galti se In ho to automatic Out
                    try { AutoOutWhenCreditEntered(value); } catch { }
                }
                else if (string.IsNullOrWhiteSpace(currentText))
                {
                    try { AutoOutWhenCreditEntered(0); } catch { }
                }
            }
            catch (Exception ex)
            {
                _syncingFields = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtcredit_TextChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtamount_TextChanged(object sender, EventArgs e)
        {
            if (_loadingData || _syncingFields) return;
            try
            {
                string currentText = txtamount.Text.Replace(",", "");
                if (decimal.TryParse(currentText, out decimal value))
                {
                    _syncingFields = true;
                    txtamount.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                    txtamount.SelectionStart = txtamount.Text.Length;
                    _syncingFields = false;
                    // Amount badla → Balance turant sync (alag Balance issue nahi)
                    SyncBalanceFromAmount();
                    // Amount → In (agar Credit empty); baad mein user In/Out change kar sakta hai
                    if (value > 0)
                        try { AutoInWhenAmountFocus(); } catch { }
                }
            }
            catch (Exception ex)
            {
                _syncingFields = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtamount_TextChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtadvance_TextChanged(object sender, EventArgs e)
        {
            if (_loadingData || _syncingFields) return;
            try
            {
                string currentText = txtadvance.Text.Replace(",", "");
                if (decimal.TryParse(currentText, out decimal value))
                {
                    _syncingFields = true;
                    txtadvance.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                    txtadvance.SelectionStart = txtadvance.Text.Length;
                    _syncingFields = false;
                    SyncBalanceFromAmount();
                }
            }
            catch (Exception ex)
            {
                _syncingFields = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtadvance_TextChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtlitter_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (char.IsControl(e.KeyChar))
                    return;

                var tb = sender as Control;
                string text = tb is Guna.UI2.WinForms.Guna2TextBox gtb ? gtb.Text : (tb as TextBox)?.Text ?? "";

                // Starting mein point (.) allow nahi
                if (e.KeyChar == '.' && (text.Length == 0 || GetSelectionStart(tb) == 0))
                {
                    e.Handled = true;
                    return;
                }

                if (!char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                    e.Handled = true;
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtlitter_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private const int RateDecimalPlaces = 4;

        private static string FormatRate(decimal rate)
        {
            return rate == decimal.Truncate(rate)
                ? rate.ToString("0", CultureInfo.InvariantCulture)
                : rate.ToString("0.0####", CultureInfo.InvariantCulture);
        }

        private void txtrate_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (char.IsControl(e.KeyChar))
                    return;

                var tb = sender as Control;
                string text = tb is Guna.UI2.WinForms.Guna2TextBox gtb ? gtb.Text : (tb as TextBox)?.Text ?? "";
                int selStart = GetSelectionStart(tb);
                int selLen = GetSelectionLength(tb);

                if (e.KeyChar == '.')
                {
                    if (text.Contains(".") || selStart == 0)
                    {
                        e.Handled = true;
                        return;
                    }
                    return;
                }

                if (!char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                    return;
                }

                string before = text.Substring(0, selStart);
                string after = text.Substring(selStart + selLen);
                string newText = before + e.KeyChar + after;

                int dotIdx = newText.IndexOf('.');
                if (dotIdx >= 0 && newText.Length - dotIdx - 1 > RateDecimalPlaces)
                    e.Handled = true;
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtrate_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtlitter_Leave(object sender, EventArgs e)
        {
            try
            {
                string t = txtlitter.Text ?? "";
                if (t.StartsWith("."))
                    txtlitter.Text = t.TrimStart('.');
            }
            catch { }
        }

        private void txtrate_Leave(object sender, EventArgs e)
        {
            try
            {
                SanitizeRateText();
            }
            catch { }
        }

        private void SanitizeRateText()
        {
            string t = (txtrate.Text ?? "").Trim().Replace(",", "");
            if (string.IsNullOrEmpty(t))
                return;

            if (t.StartsWith("."))
                t = "0" + t;

            if (t.EndsWith("."))
            {
                txtrate.Text = t.TrimEnd('.');
                return;
            }

            if (decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal rate))
            {
                string formatted = FormatRate(rate);

                if (formatted != txtrate.Text)
                    txtrate.Text = formatted;
            }
        }

        private static int GetSelectionStart(Control ctrl)
        {
            if (ctrl is Guna.UI2.WinForms.Guna2TextBox g) return g.SelectionStart;
            if (ctrl is TextBox t) return t.SelectionStart;
            return 0;
        }

        private static int GetSelectionLength(Control ctrl)
        {
            if (ctrl is Guna.UI2.WinForms.Guna2TextBox g) return g.SelectionLength;
            if (ctrl is TextBox t) return t.SelectionLength;
            return 0;
        }

        private void txtamount_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                {
                    e.Handled = true;
                    CustomeMessage customMessageBox = new CustomeMessage("آپ یہاں صرف نمبر درج کر سکتے ہیں۔", "غلطی");
                    customMessageBox.ShowDialog();
                }
                if (e.KeyChar == '.' && (sender as TextBox).Text.Contains("."))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtamount_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtcredit_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                {
                    e.Handled = true;
                    CustomeMessage customMessageBox = new CustomeMessage("آپ یہاں صرف نمبر درج کر سکتے ہیں۔", "غلطی");
                    customMessageBox.ShowDialog();
                }
                if (e.KeyChar == '.' && (sender as TextBox).Text.Contains("."))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtcredit_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtbalance_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                {
                    e.Handled = true;
                    CustomeMessage customMessageBox = new CustomeMessage("آپ یہاں صرف نمبر درج کر سکتے ہیں۔", "غلطی");
                    customMessageBox.ShowDialog();
                }
                if (e.KeyChar == '.' && (sender as TextBox).Text.Contains("."))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtbalance_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void txtadvance_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                {
                    e.Handled = true;
                    CustomeMessage customMessageBox = new CustomeMessage("آپ یہاں صرف نمبر درج کر سکتے ہیں۔", "غلطی");
                    customMessageBox.ShowDialog();
                }
                if (e.KeyChar == '.' && (sender as TextBox).Text.Contains("."))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtadvance_KeyPress): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private int ResolveCustomerIdForSave(string customerName)
        {
            if (cbName.SelectedValue != null && cbName.SelectedValue != DBNull.Value &&
                int.TryParse(cbName.SelectedValue.ToString(), out int selectedId) && selectedId > 0)
                return selectedId;

            if (string.IsNullOrWhiteSpace(customerName))
                return 0;

            var ht = new Hashtable { { "@name", customerName.Trim() } };
            DataTable dt = MainClass.ExecuteSelectQuery(
                "SELECT id FROM AddCustomer WHERE TRIM(Name) = @name COLLATE NOCASE LIMIT 1", ht);
            if (dt != null && dt.Rows.Count > 0)
                return Convert.ToInt32(dt.Rows[0]["id"]);
            return 0;
        }

        private void txtName_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressNameChange || _loadingData) return;

                string enteredName = txtName.Text.Trim();
                if (!string.IsNullOrEmpty(enteredName))
                {
                    DataTable dt = cbName.DataSource as DataTable;
                    if (dt != null)
                    {
                        DataRow match = null;
                        foreach (DataRow row in dt.Rows)
                        {
                            if (string.Equals(Convert.ToString(row["Name"]), enteredName,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                match = row;
                                break;
                            }
                        }

                        _suppressNameChange = true;
                        try
                        {
                            if (match != null)
                                cbName.SelectedValue = match["Id"];
                            else
                                cbName.SelectedValue = BlankCustomerIdValue();
                        }
                        finally { _suppressNameChange = false; }
                    }

                    OnUserPickedCustomerName();
                }
                else
                {
                    _suppressNameChange = true;
                    try { cbName.SelectedValue = BlankCustomerIdValue(); }
                    finally { _suppressNameChange = false; }
                    _namePickedByUser = false;
                }
            }
            catch (Exception ex)
            {
                _suppressNameChange = false;
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error (txtName_TextChanged): " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }
    }
}
