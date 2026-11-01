using ZaibPetroleumService.Model;
using ZaibPetroleumService.ProjectConnection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace ZaibPetroleumService.View
{
    public partial class frmDiselView : SampleView
    {
        private readonly Timer _searchDebounceTimer = new Timer();
        private int _loadToken;
        private static bool _dbIndexEnsured;
        private static bool _petrolBalanceRepaired;

        public frmDiselView()
        {
            InitializeComponent();
            this.KeyPreview = true;  // Enable form to capture key events

        }

     


        private void frmDiselView_Load(object sender, EventArgs e)
        {
            try { ApplyDailyVipGridUi(); } catch { }
            EnableGridDoubleBuffering();
            EnsureQueryIndex();

            // Average band — date filters rahenge (VIP)
            try { if (comboBox1 != null) comboBox1.Visible = false; } catch { }
            try { if (btnAverage != null) btnAverage.Visible = false; } catch { }
            dtpStart.Value = DateTime.Now;
            dtpEnd.Value = DateTime.Now;

            _searchDebounceTimer.Interval = 400;
            _searchDebounceTimer.Tick += (s, ev) =>
            {
                _searchDebounceTimer.Stop();
                LoadData1();
            };

            dtpStart.ValueChanged += DatePickers_ValueChanged;
            dtpEnd.ValueChanged += DatePickers_ValueChanged;

            if (!guna2DataGridView1.Columns.Contains("dgvTotalBalance"))
            {
                DataGridViewTextBoxColumn totalBalanceColumn = new DataGridViewTextBoxColumn();
                totalBalanceColumn.Name = "dgvTotalBalance";
                totalBalanceColumn.HeaderText = "Total Balance";
                guna2DataGridView1.Columns.Add(totalBalanceColumn);
            }

            guna2DataGridView1.KeyDown += new KeyEventHandler(guna2DataGridView1_KeyDown);

            // Form turant dikhe — data background mein aaye (aaj ki date filter)
            LoadData1();
        }

        private static void EnsureQueryIndex()
        {
            if (_dbIndexEnsured) return;
            try
            {
                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    try { Services.DatabaseSchemaManager.EnsureProfessionalSchema(con); } catch { }
                    using (var cmd1 = new SQLiteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_PetrolAdd_Date_Initial ON PetrolAdd(Date, IsInitialEntry)", con))
                        cmd1.ExecuteNonQuery();
                    using (var cmd2 = new SQLiteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_PetrolAdd_CustomerId ON PetrolAdd(CustomerId)", con))
                        cmd2.ExecuteNonQuery();
                }
                _dbIndexEnsured = true;
                // One-shot: repair stale credit Balance snapshots so Credit Adjust / DB match VIP.
                if (!_petrolBalanceRepaired)
                {
                    _petrolBalanceRepaired = true;
                    try { Services.BalanceConfirmationService.RecalculateAllCustomerPetrolBalances(); }
                    catch { }
                }
            }
            catch { /* index optional — load still works */ }
        }

        private void EnableGridDoubleBuffering()
        {
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null, guna2DataGridView1, new object[] { true });
        }

        private void btnAverage_Click(object sender, EventArgs e)
        {
            // Calculate Average based on the selected ComboBox option (Rate)
            if (comboBox1.Visible && comboBox1.SelectedItem != null)
            {
                string selectedOption = comboBox1.SelectedItem.ToString();
                if (selectedOption == "Rate")
                {
                    CalculateAverage("Rate");
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Please select a valid option!", "Info");
                    noDataMessage.ShowDialog();
                }
            }
        }

 
      
        private void frmCreditAdjust_KeyDown(object sender, KeyEventArgs e)
        {
            // Check if the Ctrl key is pressed along with R
            if (e.Control && e.KeyCode == Keys.R)
            {
                // Call the method to reload the form
                LoadData1();
                CustomeMessage noDataMessage = new CustomeMessage("Form reloaded successfully!", "Info");
                noDataMessage.ShowDialog();
            }
        }

        public override void btnAdd_Click(object sender, EventArgs e)
        {
            frmDiselAdd frm = new frmDiselAdd();
            frm.id = 0;
            frm.ShowDialog(this);
            // Save ke dauran pehle se refresh — yahan halka reload
            try { LoadData1(); } catch { }
        }

        /// <summary>Add/Edit/Delete ke baad — date filter mein entry dikhe (Roznamcha jaisa, race nahi).</summary>
        public void RefreshAfterChildSave(DateTime entryDate)
        {
            try
            {
                DateTime d = entryDate.Date;
                DateTime today = DateTime.Today;

                try
                {
                    dtpStart.ValueChanged -= DatePickers_ValueChanged;
                    dtpEnd.ValueChanged -= DatePickers_ValueChanged;
                }
                catch { }

                try
                {
                    if (dtpStart != null && dtpStart.Value.Date > d)
                        dtpStart.Value = d;
                    if (dtpStart != null && dtpStart.Value.Date > today)
                        dtpStart.Value = today;
                    if (dtpEnd != null && dtpEnd.Value.Date < d)
                        dtpEnd.Value = d;
                    if (dtpEnd != null && dtpEnd.Value.Date < today)
                        dtpEnd.Value = today;
                }
                catch { }

                try
                {
                    dtpStart.ValueChanged -= DatePickers_ValueChanged;
                    dtpEnd.ValueChanged -= DatePickers_ValueChanged;
                    dtpStart.ValueChanged += DatePickers_ValueChanged;
                    dtpEnd.ValueChanged += DatePickers_ValueChanged;
                }
                catch { }
            }
            catch { }
            LoadData1();
        }

        private void LoadData1()
        {
            int token = ++_loadToken;
            string searchText = txtSearch.Text ?? string.Empty;
            DateTime startDate = dtpStart.Value.Date;
            DateTime endDate = dtpEnd.Value.Date;
            if (endDate < startDate)
            {
                DateTime tmp = startDate;
                startDate = endDate;
                endDate = tmp;
            }

            string locF = "";
            string itemF = "";
            string modelF = "";
            string inOutF = "";
            try { locF = GetDailyVipLocFilter(); } catch { }
            try { itemF = GetDailyVipItemFilter(); } catch { }
            try { modelF = GetDailyVipModelFilter(); } catch { }
            try { inOutF = GetDailyVipInOutFilter(); } catch { }

            Task.Run(() => FetchPetrolData(searchText, startDate, endDate, locF, itemF, modelF, inOutF))
                .ContinueWith(t =>
                {
                    if (token != _loadToken || IsDisposed) return;
                    if (t.IsFaulted)
                    {
                        MessageBox.Show(t.Exception?.GetBaseException().Message ?? "Load failed.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    BindPetrolGrid(t.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static DataTable FetchPetrolData(
            string searchText, DateTime startDate, DateTime endDate,
            string locFilter = "", string itemFilter = "", string modelFilter = "", string inOutFilter = "")
        {
            searchText = (searchText ?? string.Empty).Trim();
            locFilter = (locFilter ?? "").Trim();
            itemFilter = (itemFilter ?? "").Trim();
            modelFilter = (modelFilter ?? "").Trim();
            inOutFilter = (inOutFilter ?? "").Trim();
            // Sale + Credit dono. Credit entry (IsInitialEntry=0): Credit column mein wasooli.
            // Running Balance = full-history cumulative — SAME formula as Amount + CreditVal columns
            // (Litter×Rate+Adv when both > 0, else Amount+Adv) − Credit. Date/search pe 0 se start nahi.
            const string vipRunningBalanceSql = @"
(SELECT IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN 0
             WHEN IFNULL(p2.Litter, 0) > 0 AND IFNULL(p2.Rate, 0) > 0
               THEN CAST(IFNULL(p2.Litter, 0) AS REAL) * CAST(IFNULL(p2.Rate, 0) AS REAL) + IFNULL(p2.Advance, 0)
             ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
           END
         ), 0)
       - IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN
               CASE
                 WHEN IFNULL(p2.Credit, 0) <> 0 THEN IFNULL(p2.Credit, 0)
                 ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
               END
             ELSE IFNULL(p2.Credit, 0)
           END
         ), 0)
 FROM PetrolAdd p2
 WHERE p2.CustomerId = PetrolAdd.CustomerId
   AND (
         date(p2.Date) < date(PetrolAdd.Date)
         OR (date(p2.Date) = date(PetrolAdd.Date) AND p2.pid <= PetrolAdd.pid)
       ))";

            // Sale Amount: Litter×Rate+Adv only when BOTH Litter & Rate > 0; else Amount+Adv.
            const string selectCore = @"
SELECT PetrolAdd.pid,
       PetrolAdd.CustomerId,
       PetrolAdd.Date,
       PetrolAdd.ReceiptNo,
       PetrolAdd.vehicle,
       IFNULL(PetrolAdd.SaleLocation,'') AS SaleLocation,
       IFNULL(PetrolAdd.SaleItem,'') AS SaleItem,
       IFNULL(PetrolAdd.SaleVehicleModel,'') AS SaleVehicleModel,
       IFNULL(NULLIF(TRIM(IFNULL(PetrolAdd.SaleInOut,'')),''),'In') AS SaleInOut,
       IFNULL(PetrolAdd.Litter, 0) AS Litter,
       IFNULL(PetrolAdd.Rate, 0) AS Rate,
       IFNULL(PetrolAdd.Advance, 0) AS Advance,
       CASE
           WHEN IFNULL(PetrolAdd.IsInitialEntry, 1) = 0 THEN 0
           WHEN IFNULL(PetrolAdd.Litter, 0) > 0 AND IFNULL(PetrolAdd.Rate, 0) > 0
               THEN CAST(IFNULL(PetrolAdd.Litter, 0) * IFNULL(PetrolAdd.Rate, 0) + IFNULL(PetrolAdd.Advance, 0) AS DECIMAL(18, 3))
           ELSE IFNULL(PetrolAdd.Amount, 0) + IFNULL(PetrolAdd.Advance, 0)
       END AS Amount,
       CASE
           WHEN IFNULL(PetrolAdd.IsInitialEntry, 1) = 0 THEN
               CASE
                   WHEN IFNULL(PetrolAdd.Credit, 0) <> 0 THEN IFNULL(PetrolAdd.Credit, 0)
                   ELSE IFNULL(PetrolAdd.Amount, 0) + IFNULL(PetrolAdd.Advance, 0)
               END
           ELSE IFNULL(PetrolAdd.Credit, 0)
       END AS CreditVal,
       CAST(" + vipRunningBalanceSql + @" AS DECIMAL(18, 3)) AS Balance,
       CAST(NULL AS REAL) AS TotalBalance,
       IFNULL(PetrolAdd.Note, '') AS Note,
       AddCustomer.Name,
       IFNULL(PetrolAdd.IsInitialEntry, 1) AS IsInitialEntry
FROM PetrolAdd
INNER JOIN AddCustomer ON PetrolAdd.CustomerId = AddCustomer.id
WHERE 1=1";

            // VIP filters — Location / Item / Vehicle Model / In-Out (All = empty)
            const string vipFilterSql = @"
  AND (@LocFilter = '' OR TRIM(IFNULL(PetrolAdd.SaleLocation,'')) = @LocFilter)
  AND (@ItemFilter = '' OR TRIM(IFNULL(PetrolAdd.SaleItem,'')) = @ItemFilter)
  AND (@ModelFilter = '' OR TRIM(IFNULL(PetrolAdd.SaleVehicleModel,'')) = @ModelFilter)
  AND (@InOutFilter = '' OR IFNULL(NULLIF(TRIM(IFNULL(PetrolAdd.SaleInOut,'')),''),'In') = @InOutFilter)";

            var dt = new DataTable();
            using (var con = new SQLiteConnection(projectconnection.conReturn()))
            {
                con.Open();
                string qry;
                var cmd = new SQLiteCommand { Connection = con };

                void BindVipFilters()
                {
                    cmd.Parameters.AddWithValue("@LocFilter", locFilter);
                    cmd.Parameters.AddWithValue("@ItemFilter", itemFilter);
                    cmd.Parameters.AddWithValue("@ModelFilter", modelFilter);
                    cmd.Parameters.AddWithValue("@InOutFilter", inOutFilter);
                }

                // Date range hamesha — search khali ya Name/Vehicle dono cases mein
                const string dateFilterSql = @"
  AND date(PetrolAdd.Date) >= date(@StartDate)
  AND date(PetrolAdd.Date) <= date(@EndDate)";

                if (string.IsNullOrEmpty(searchText))
                {
                    qry = selectCore + vipFilterSql + dateFilterSql + @"
ORDER BY AddCustomer.Name COLLATE NOCASE, PetrolAdd.Date ASC, PetrolAdd.pid ASC";
                    cmd.CommandText = qry;
                    cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));
                    BindVipFilters();
                }
                else
                {
                    // Name (exact) OR Vehicle No — + date pickers dono saath
                    string searchLike = "%" + searchText + "%";
                    int? exactCustomerId = TryGetExactCustomerId(con, searchText);

                    qry = selectCore + vipFilterSql + dateFilterSql + @"
  AND (
        (@HasCustomer = 1 AND PetrolAdd.CustomerId = @CustomerId)
        OR IFNULL(PetrolAdd.vehicle, '') LIKE @SearchLike COLLATE NOCASE
      )
ORDER BY AddCustomer.Name COLLATE NOCASE, PetrolAdd.Date ASC, PetrolAdd.pid ASC";
                    cmd.CommandText = qry;
                    cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@HasCustomer", exactCustomerId.HasValue ? 1 : 0);
                    cmd.Parameters.AddWithValue("@CustomerId", exactCustomerId ?? 0);
                    cmd.Parameters.AddWithValue("@SearchLike", searchLike);
                    BindVipFilters();
                }

                using (cmd)
                using (var da = new SQLiteDataAdapter(cmd))
                    da.Fill(dt);
            }

            ApplyCustomerGroupTotalBalance(dt);
            return dt;
        }

        /// <summary>
        /// Running Balance har row pe — Amount − Credit chain (jo grid pe dikhe) + full-history opening.
        /// SQL Balance se pehli row ka opening nikaal ke Amount/CreditVal se dubara chalao —
        /// taake Running Balance aur Amount/Credit hamesha match rahein.
        /// </summary>
        private static void ApplyCustomerGroupTotalBalance(DataTable dt)
        {
            if (dt == null || dt.Rows.Count == 0 || !dt.Columns.Contains("Balance")) return;
            if (!dt.Columns.Contains("TotalBalance")) return;
            if (!dt.Columns.Contains("CustomerId") || !dt.Columns.Contains("Amount")
                || !dt.Columns.Contains("CreditVal"))
            {
                foreach (DataRow row in dt.Rows)
                    row["TotalBalance"] = ToDec(row["Balance"]);
                return;
            }

            int i = 0;
            while (i < dt.Rows.Count)
            {
                if (dt.Rows[i]["CustomerId"] == DBNull.Value)
                {
                    dt.Rows[i]["TotalBalance"] = ToDec(dt.Rows[i]["Balance"]);
                    i++;
                    continue;
                }

                int cid = Convert.ToInt32(dt.Rows[i]["CustomerId"]);
                int start = i;
                while (i < dt.Rows.Count
                    && dt.Rows[i]["CustomerId"] != DBNull.Value
                    && Convert.ToInt32(dt.Rows[i]["CustomerId"]) == cid)
                    i++;
                int end = i - 1;

                decimal firstAmt = ToDec(dt.Rows[start]["Amount"]);
                decimal firstCr = ToDec(dt.Rows[start]["CreditVal"]);
                // SQL Balance = full history through first row; opening = that − first row delta
                decimal run = ToDec(dt.Rows[start]["Balance"]) - firstAmt + firstCr;

                for (int r = start; r <= end; r++)
                {
                    run += ToDec(dt.Rows[r]["Amount"]) - ToDec(dt.Rows[r]["CreditVal"]);
                    dt.Rows[r]["Balance"] = run;
                    dt.Rows[r]["TotalBalance"] = run;
                }
            }
        }

        private static decimal ToDec(object v)
        {
            if (v == null || v == DBNull.Value) return 0m;
            return Convert.ToDecimal(v);
        }

        private static int? TryGetExactCustomerId(SQLiteConnection con, string name)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT id FROM AddCustomer WHERE TRIM(Name) = @Name COLLATE NOCASE LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value) return null;
                return Convert.ToInt32(result);
            }
        }

        private static List<int> GetCustomerIdsByNameLike(SQLiteConnection con, string searchLike)
        {
            // Deprecated path — exact name match only (similar names mix nahi honge)
            var ids = new List<int>();
            string exactName = (searchLike ?? "").Trim().Trim('%');
            if (string.IsNullOrEmpty(exactName))
                return ids;

            int? id = TryGetExactCustomerId(con, exactName);
            if (id.HasValue)
                ids.Add(id.Value);
            return ids;
        }

        private void BindPetrolGrid(DataTable dt)
        {
            guna2DataGridView1.SuspendLayout();
            try
            {
                guna2DataGridView1.AutoGenerateColumns = false;
                guna2DataGridView1.DataSource = null;

                void Map(string gridCol, string dataCol)
                {
                    if (!guna2DataGridView1.Columns.Contains(gridCol)) return;
                    var col = guna2DataGridView1.Columns[gridCol];
                    col.DataPropertyName = dt.Columns.Contains(dataCol) ? dataCol : string.Empty;
                    col.Visible = true;
                }

                ApplyDailyVipGridUi();

                Map("dgvid", "pid");
                Map("dgvDate", "Date");
                Map("dgvreciptno", "ReceiptNo");
                Map("dgvVechleNo", "vehicle");
                Map("dgvSaleInOut", "SaleInOut");
                Map("dgvSaleLocation", "SaleLocation");
                Map("dgvSaleItem", "SaleItem");
                Map("dgvSaleVehicleModel", "SaleVehicleModel");
                Map("dgvLitter", "Litter");
                Map("dgvrate", "Rate");
                Map("dgvAdvance", "Advance");
                Map("dgvAmount", "Amount");
                Map("dgvCredit", "CreditVal");
                Map("dgvBalance", "Balance");
                Map("dgvTotalBalance", "TotalBalance");
                Map("dgvNote", "Note");
                Map("dgvName", "Name");

                if (guna2DataGridView1.Columns.Contains("dgvCredit"))
                {
                    // Binding alias — designer pe DataPropertyName na hone se pehle 0 dikhta tha
                    guna2DataGridView1.Columns["dgvCredit"].DataPropertyName =
                        dt.Columns.Contains("CreditVal") ? "CreditVal" : "Credit";
                    guna2DataGridView1.Columns["dgvCredit"].MinimumWidth = 90;
                    guna2DataGridView1.Columns["dgvCredit"].Width = 110;
                    guna2DataGridView1.Columns["dgvCredit"].DefaultCellStyle.ForeColor = Color.Red;
                    guna2DataGridView1.Columns["dgvCredit"].DefaultCellStyle.Format = "N0";
                    guna2DataGridView1.Columns["dgvCredit"].DefaultCellStyle.Font =
                        new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    guna2DataGridView1.Columns["dgvCredit"].DefaultCellStyle.Alignment =
                        DataGridViewContentAlignment.MiddleRight;
                    guna2DataGridView1.Columns["dgvCredit"].ValueType = typeof(decimal);
                }

                guna2DataGridView1.DataSource = dt;

                // Guna/grid kabhi alias miss karta hai — Credit/Balance seedha DataRow se cell pe likho
                ForceCreditCellsFromData();
                ForceBalanceCellsFromData();
                ForceRunningBalanceCellsFromData();

                foreach (DataGridViewColumn c in guna2DataGridView1.Columns)
                {
                    if (c.Name == "IsInitialEntry" || c.DataPropertyName == "IsInitialEntry")
                        c.Visible = false;
                }

                MainClass.SrNo(guna2DataGridView1);
                SetColumnOrder();
                ApplyDailyVipColumnVisibilityAndOrder();
                try { EnableQuickAmountCreditEdit(); } catch { }
            }
            finally
            {
                guna2DataGridView1.ResumeLayout(true);
            }

            CalculateTotalLitter();
            guna2DataGridView1.Invalidate();
            guna2DataGridView1.Refresh();
        }

        /// Credit column = DataTable CreditVal/Credit (binding miss pe bhi 0 na aaye).
        private void ForceCreditCellsFromData()
        {
            if (!guna2DataGridView1.Columns.Contains("dgvCredit")) return;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                if (!(row.DataBoundItem is DataRowView drv)) continue;

                decimal c = 0m;
                if (drv.Row.Table.Columns.Contains("CreditVal"))
                    c = ToDec(drv["CreditVal"]);
                else if (drv.Row.Table.Columns.Contains("Credit"))
                    c = ToDec(drv["Credit"]);

                row.Cells["dgvCredit"].Value = c;
            }
        }

        private void ForceBalanceCellsFromData()
        {
            if (!guna2DataGridView1.Columns.Contains("dgvBalance")) return;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                if (!(row.DataBoundItem is DataRowView drv)) continue;
                if (!drv.Row.Table.Columns.Contains("Balance")) continue;
                row.Cells["dgvBalance"].Value = ToDec(drv["Balance"]);
            }
        }

        /// <summary>Running Balance column = har row ka cumulative (sale + simple credit) — 0 bhi dikhe.</summary>
        private void ForceRunningBalanceCellsFromData()
        {
            if (!guna2DataGridView1.Columns.Contains("dgvTotalBalance")) return;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                if (!(row.DataBoundItem is DataRowView drv)) continue;

                decimal bal = 0m;
                if (drv.Row.Table.Columns.Contains("TotalBalance") && drv["TotalBalance"] != DBNull.Value)
                    bal = ToDec(drv["TotalBalance"]);
                else if (drv.Row.Table.Columns.Contains("Balance") && drv["Balance"] != DBNull.Value)
                    bal = ToDec(drv["Balance"]);

                // DataRow + cell dono — binding miss pe bhi VIP Running Balance dikhe
                if (drv.Row.Table.Columns.Contains("TotalBalance"))
                    drv["TotalBalance"] = bal;

                row.Cells["dgvTotalBalance"].Value = bal;
            }
        }

        private void ApplyCustomerTotalBalances()
        {
            decimal runningTotalBalance = 0;
            string previousCustomer = string.Empty;
            int firstCustomerRowIndex = -1;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;

                string currentCustomer = row.Cells["dgvName"].Value?.ToString() ?? string.Empty;

                if (currentCustomer != previousCustomer && firstCustomerRowIndex != -1)
                {
                    guna2DataGridView1.Rows[firstCustomerRowIndex].Cells["dgvTotalBalance"].Value =
                        runningTotalBalance.ToString("F2");
                    runningTotalBalance = 0;
                }

                if (row.Cells["dgvBalance"].Value != null && row.Cells["dgvBalance"].Value != DBNull.Value)
                    runningTotalBalance += Convert.ToDecimal(row.Cells["dgvBalance"].Value);

                if (currentCustomer != previousCustomer)
                    firstCustomerRowIndex = row.Index;
                else
                    row.Cells["dgvTotalBalance"].Value = DBNull.Value;

                previousCustomer = currentCustomer;
            }

            if (firstCustomerRowIndex != -1)
                guna2DataGridView1.Rows[firstCustomerRowIndex].Cells["dgvTotalBalance"].Value =
                    runningTotalBalance.ToString("F2");
        }


        private void SetColumnOrder()
        {
            guna2DataGridView1.Columns["dgvName"].DisplayIndex = 1;         // Name
            guna2DataGridView1.Columns["dgvDate"].DisplayIndex = 2;         // Date
            guna2DataGridView1.Columns["dgvreciptno"].DisplayIndex = 3;     // ReceiptNo
            guna2DataGridView1.Columns["dgvVechleNo"].DisplayIndex = 4;     // Vehicle
            guna2DataGridView1.Columns["dgvLitter"].DisplayIndex = 5;       // Litter
            guna2DataGridView1.Columns["dgvrate"].DisplayIndex = 6;         // Rate
            guna2DataGridView1.Columns["dgvAdvance"].DisplayIndex = 7;      // Advance
            guna2DataGridView1.Columns["dgvAmount"].DisplayIndex = 8;       // Amount
            guna2DataGridView1.Columns["dgvCredit"].DisplayIndex = 9;       // Credit
            guna2DataGridView1.Columns["dgvBalance"].DisplayIndex = 10;     // Balance
            guna2DataGridView1.Columns["dgvNote"].DisplayIndex = 11;        // Note
            guna2DataGridView1.Columns["dgvTotalBalance"].DisplayIndex = 12;// TotalBalance
        }


        // Method to update the TotalBalance in the database
        private void UpdateTotalBalanceInDatabase(int pid, decimal totalBalance)
        {
            string qry = "UPDATE PetrolAdd SET TotalBalance = @TotalBalance WHERE pid = @pid";

            Hashtable ht = new Hashtable();
            ht.Add("@TotalBalance", totalBalance);
            ht.Add("@pid", pid);

            MainClass.DataInsertUpdateDelete(qry, ht);  // Update the database
        }
        private void guna2DataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = guna2DataGridView1.Columns[e.ColumnIndex].Name;

            // In/Out: Out → red + bada bold font
            if (col == "dgvSaleInOut")
            {
                string io = (e.Value ?? "").ToString().Trim();
                if (string.IsNullOrEmpty(io)) io = "In";
                e.Value = io;
                e.FormattingApplied = true;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
                if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                    e.CellStyle.ForeColor = Color.Red;
                else
                    e.CellStyle.ForeColor = Color.LightGreen;
                return;
            }

            // Credit — Amount jaisa size/format (N0), red color VIP
            if (col == "dgvCredit")
            {
                decimal c = 0m;
                var row = guna2DataGridView1.Rows[e.RowIndex];
                if (row.DataBoundItem is DataRowView drv && drv.Row.Table.Columns.Contains("CreditVal"))
                    c = ToDec(drv["CreditVal"]);
                else if (row.DataBoundItem is DataRowView drvOld && drvOld.Row.Table.Columns.Contains("Credit"))
                    c = ToDec(drvOld["Credit"]);
                else if (e.Value != null && e.Value != DBNull.Value)
                    decimal.TryParse(e.Value.ToString().Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out c);

                e.Value = c.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
                e.FormattingApplied = true;
                e.CellStyle.ForeColor = Color.Red;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                return;
            }
            if (col == "dgvAmount")
            {
                decimal a = 0m;
                if (e.Value != null && e.Value != DBNull.Value)
                    decimal.TryParse(e.Value.ToString().Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out a);
                e.Value = a.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
                e.FormattingApplied = true;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                return;
            }
            if (col == "dgvNote")
            {
                if (e.Value != null && !string.IsNullOrWhiteSpace(e.Value.ToString()))
                {
                    e.CellStyle.ForeColor = Color.White;
                    e.CellStyle.Font = new Font(guna2DataGridView1.Font, FontStyle.Bold);
                }
                return;
            }

            // VIP: Running Balance har row (0 / credit / sale) — hamesha dikhe
            if (col == "dgvTotalBalance")
            {
                decimal balanceValue = 0m;
                if (e.Value != null && e.Value != DBNull.Value)
                    decimal.TryParse(e.Value.ToString().Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out balanceValue);
                else if (guna2DataGridView1.Rows[e.RowIndex].DataBoundItem is DataRowView drvRb)
                {
                    if (drvRb.Row.Table.Columns.Contains("TotalBalance"))
                        balanceValue = ToDec(drvRb["TotalBalance"]);
                    else if (drvRb.Row.Table.Columns.Contains("Balance"))
                        balanceValue = ToDec(drvRb["Balance"]);
                }

                e.Value = balanceValue.ToString("N0");
                e.FormattingApplied = true;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                e.CellStyle.ForeColor = Color.FromArgb(212, 175, 55);
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                e.CellStyle.BackColor = Color.FromArgb(28, 32, 58);
            }
        }

        private static void ApplyBalanceCellColor(DataGridViewCellFormattingEventArgs e, decimal balanceValue)
        {
            decimal abs = Math.Abs(balanceValue);
            if (abs > 10000m)
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 53, 69);
                e.CellStyle.ForeColor = Color.White;
            }
            else if (abs > 1000m)
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 255, 0);
                e.CellStyle.ForeColor = Color.Black;
            }
            else
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 140, 0);
                e.CellStyle.ForeColor = Color.White;
            }
        }

        // Method to set the order of the columns in DataGridView
        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || guna2DataGridView1.CurrentRow == null) return;

            // Amount / Credit → grid pe seedha edit (form mat kholo)
            if (e.ColumnIndex >= 0)
            {
                string col = guna2DataGridView1.Columns[e.ColumnIndex].Name;
                if (col == "dgvAmount" || col == "dgvCredit")
                {
                    try
                    {
                        guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[e.RowIndex].Cells[e.ColumnIndex];
                        guna2DataGridView1.BeginEdit(true);
                    }
                    catch { }
                    return;
                }
            }

            int id = Convert.ToInt32(guna2DataGridView1.CurrentRow.Cells["dgvid"].Value);

            // Daily Diesel — baaki columns pe form edit
            var frm = new frmDiselAdd { id = id };
            frm.ShowDialog(this);
            try { LoadData1(); } catch { }
        }



        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void guna2DataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (guna2DataGridView1.CurrentRow != null)
                {
                    // Confirm deletion
                    DialogResult confirmDelete = MessageBox.Show("Are you sure you want to delete this record?",
                                                                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (confirmDelete == DialogResult.Yes)
                    {
                        int id = Convert.ToInt32(guna2DataGridView1.CurrentRow.Cells["dgvid"].Value);
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
                        MainClass.DeleteWithTombstone("PetrolAdd", "pid", id, "zaib_petrol_entries");
                        if (customerIdForRepair > 0)
                            Services.BalanceConfirmationService.RecalculateCustomerPetrolBalances(customerIdForRepair);
                        MessageBox.Show("Record deleted successfully.");
                        RefreshAfterChildSave(DateTime.Now);
                    }
                }
            }
        }

        private void btnDel_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem != null)
            {
                string selectedOption = comboBox1.SelectedItem.ToString();

                if (selectedOption == "Rate")
                {
                    CalculateAverage("Rate");
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Please select a valid option!", "Info");
                    noDataMessage.ShowDialog();
                }
            }
        }

        private void CalculateAverage(string type)
        {
            DateTime startDate = dtpStart.Value.Date; // Start date
            DateTime endDate = dtpEnd.Value.Date; // End date
            decimal totalSum = 0;
            decimal totalLitterSum = 0;
            int rowCount = 0;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                DateTime rowDate = Convert.ToDateTime(row.Cells["dgvDate"].Value).Date;

                // Check if row date is equal to or between start and end dates (inclusive)
                if (rowDate >= startDate && rowDate <= endDate)
                {
                    if (row.Cells["dgvAmount"].Value != DBNull.Value && row.Cells["dgvLitter"].Value != DBNull.Value)
                    {
                        decimal amount = Convert.ToDecimal(row.Cells["dgvAmount"].Value);
                        decimal litter = Convert.ToDecimal(row.Cells["dgvLitter"].Value);

                        totalSum += amount;
                        totalLitterSum += litter;
                        rowCount++;
                    }
                }
            }

            // Displaying the results
            if (rowCount > 0)
            {
                if (totalLitterSum != 0) // Ensure no division by zero
                {
                    decimal ratio = totalSum / totalLitterSum;
                    lblResult.Text = $"Sum Amount: {totalSum:F2}, Sum Litter: {totalLitterSum:F2}, Average: {ratio:F2}";
                }
                else
                {
                    lblResult.Text = "Total Litter is zero, cannot calculate ratio.";
                }
            }
            else
            {
                lblResult.Text = "No data found for the selected date range.";
            }
        }

        private void btnAverage_Click_1(object sender, EventArgs e)
        {
            // Calculate Average based on the selected ComboBox option (Rate)
            if (comboBox1.Visible && comboBox1.SelectedItem != null)
            {
                string selectedOption = comboBox1.SelectedItem.ToString();
                if (selectedOption == "Rate")
                {
                    CalculateAverage("Rate");
                }
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Please select a valid option!", "Info");
                    noDataMessage.ShowDialog();
                }
            }
        }


        // Method to calculate TotalBalance for each customer
        private void CalculateTotalBalance()
        {
            decimal runningTotalBalance = 0;
            string previousCustomer = "";
            int firstCustomerRowIndex = -1;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                string currentCustomer = row.Cells["dgvName"].Value?.ToString() ?? string.Empty;

                // If a new customer starts, show TotalBalance in the first row of the previous customer
                if (currentCustomer != previousCustomer && firstCustomerRowIndex != -1)
                {
                    guna2DataGridView1.Rows[firstCustomerRowIndex].Cells["dgvTotalBalance"].Value = runningTotalBalance.ToString("F2");
                    runningTotalBalance = 0; // Reset for new customer
                }

                // Calculate current customer's running total balance
                if (row.Cells["dgvBalance"].Value != DBNull.Value)
                {
                    decimal currentBalance = Convert.ToDecimal(row.Cells["dgvBalance"].Value);
                    runningTotalBalance += currentBalance;
                }

                // Track first row index for current customer
                if (currentCustomer != previousCustomer)
                {
                    firstCustomerRowIndex = row.Index; // Store first index for the customer
                }

                previousCustomer = currentCustomer; // Update previous customer
            }

            // Update TotalBalance for the last customer
            if (firstCustomerRowIndex != -1)
            {
                guna2DataGridView1.Rows[firstCustomerRowIndex].Cells["dgvTotalBalance"].Value = runningTotalBalance.ToString("F2");
            }
        }

        private void DatePickers_ValueChanged(object sender, EventArgs e)
        {
            LoadData1();
        }

        private void CalculateTotalLitter()
        {
            decimal totalIn = 0m;
            decimal totalOut = 0m;
            decimal totalCredit = 0m;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;

                decimal rowAmt = 0m;
                if (row.Cells["dgvAmount"].Value != null && row.Cells["dgvAmount"].Value != DBNull.Value)
                    rowAmt = ToDec(row.Cells["dgvAmount"].Value);

                string io = "In";
                try
                {
                    if (guna2DataGridView1.Columns.Contains("dgvSaleInOut") &&
                        row.Cells["dgvSaleInOut"].Value != null &&
                        row.Cells["dgvSaleInOut"].Value != DBNull.Value)
                        io = row.Cells["dgvSaleInOut"].Value.ToString().Trim();
                    else if (row.DataBoundItem is DataRowView drvIo &&
                             drvIo.Row.Table.Columns.Contains("SaleInOut"))
                        io = (drvIo["SaleInOut"]?.ToString() ?? "").Trim();
                }
                catch { }
                if (string.IsNullOrEmpty(io)) io = "In";

                if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                    totalOut += rowAmt;
                else
                    totalIn += rowAmt;

                decimal rowCredit = 0m;
                if (row.Cells["dgvCredit"].Value != null && row.Cells["dgvCredit"].Value != DBNull.Value)
                    rowCredit = ToDec(row.Cells["dgvCredit"].Value);
                else if (row.DataBoundItem is DataRowView drvSum)
                {
                    if (drvSum.Row.Table.Columns.Contains("CreditVal"))
                        rowCredit = ToDec(drvSum["CreditVal"]);
                    else if (drvSum.Row.Table.Columns.Contains("Credit"))
                        rowCredit = ToDec(drvSum["Credit"]);
                }
                totalCredit += rowCredit;
            }

            decimal net = totalIn - totalOut - totalCredit;
            try
            {
                if (lblAmount != null)
                {
                    lblAmount.Text = $"In: {totalIn:N0}";
                    lblAmount.ForeColor = Color.LightGreen;
                }
                if (lblResult != null)
                {
                    lblResult.Text = $"Out −{totalOut:N0}  |  Credit −{totalCredit:N0}";
                    lblResult.ForeColor = Color.Red;
                }
                if (lblLitter != null)
                {
                    lblLitter.Text = $"Net: {net:N0}";
                    lblLitter.ForeColor = Color.FromArgb(212, 175, 55);
                }
            }
            catch { }
        }

    }
}