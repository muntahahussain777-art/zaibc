using ZaibPetroleumService.Model;
using ZaibPetroleumService.ProjectConnection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class frmStockView : SampleView
    {
        private readonly Timer _searchDebounceTimer = new Timer();
        private int _loadToken;
        private static bool _dbIndexEnsured;

        public frmStockView()
        {
            InitializeComponent();
        }

        private void frmStockView_Load(object sender, EventArgs e)
        {
            EnableGridDoubleBuffering();
            EnsureQueryIndex();
            try
            {
                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    Services.DatabaseSchemaManager.EnsureProfessionalSchema(con);
                }
            }
            catch { }

            // Average band — date filters lazmi (VIP)
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
            guna2DataGridView1.CellFormatting += guna2DataGridView1_CellFormatting;

            try { EnsureDealerVipFilters(); } catch { }
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
                    using (var cmd1 = new SQLiteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_AddStock_Date ON AddStock(Date)", con))
                        cmd1.ExecuteNonQuery();
                    using (var cmd2 = new SQLiteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_AddStock_DealerId ON AddStock(DealerId)", con))
                        cmd2.ExecuteNonQuery();
                }
                _dbIndexEnsured = true;
            }
            catch { }
        }

        private void EnableGridDoubleBuffering()
        {
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null, guna2DataGridView1, new object[] { true });
        }

        public override void btnAdd_Click(object sender, EventArgs e)
        {
            frmStockAdd frm = new frmStockAdd();
            frm.ShowDialog();
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
            try { locF = GetDealerVipLocFilter(); } catch { }
            try { itemF = GetDealerVipItemFilter(); } catch { }
            try { modelF = GetDealerVipModelFilter(); } catch { }
            try { inOutF = GetDealerVipInOutFilter(); } catch { }

            Task.Run(() => FetchStockData(searchText, startDate, endDate, locF, itemF, modelF, inOutF))
                .ContinueWith(t =>
                {
                    if (token != _loadToken || IsDisposed) return;
                    if (t.IsFaulted)
                    {
                        MessageBox.Show(t.Exception?.GetBaseException().Message ?? "Load failed.", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    BindStockGrid(t.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static DataTable FetchStockData(
            string searchText, DateTime startDate, DateTime endDate,
            string locFilter = "", string itemFilter = "", string modelFilter = "", string inOutFilter = "")
        {
            searchText = (searchText ?? string.Empty).Trim();
            locFilter = (locFilter ?? "").Trim();
            itemFilter = (itemFilter ?? "").Trim();
            modelFilter = (modelFilter ?? "").Trim();
            inOutFilter = (inOutFilter ?? "").Trim();

            // Amount = Litter×Rate when both > 0, else Amount column; Credit = AddStock.Credit
            // UNION Dealer Payout (DieselLedger) credits so same credit DealerAmount mein dikhe
            const string amountExpr = @"
CASE
  WHEN IFNULL(AddStock.AddDisel, 0) <> 0 AND IFNULL(AddStock.Rate, 0) <> 0
    THEN CAST(IFNULL(AddStock.AddDisel, 0) AS REAL) * CAST(IFNULL(AddStock.Rate, 0) AS REAL)
  ELSE IFNULL(AddStock.Amount, 0)
END";

            const string stockSelect = @"
SELECT AddStock.Sid AS Sid,
       AddStock.DealerId AS DealerId,
       AddStock.Date AS Date,
       IFNULL(AddDealer.DealerName,'') AS DealerName,
       IFNULL(AddStock.Vehicle,'') AS Vehicle,
       IFNULL(NULLIF(TRIM(IFNULL(AddStock.SaleInOut,'')),''),'In') AS SaleInOut,
       IFNULL(AddStock.SaleLocation,'') AS SaleLocation,
       IFNULL(AddStock.SaleItem,'') AS SaleItem,
       IFNULL(AddStock.SaleVehicleModel,'') AS SaleVehicleModel,
       IFNULL(AddStock.AddDisel, 0) AS AddDisel,
       IFNULL(AddStock.Rate, 0) AS Rate,
       CAST(" + amountExpr + @" AS DECIMAL(18,3)) AS Amount,
       IFNULL(AddStock.Credit, 0) AS Credit,
       IFNULL(AddStock.Note,'') AS Note,
       CAST(0 AS REAL) AS RunningBalance,
       'stock' AS Src
FROM AddStock
LEFT JOIN AddDealer ON AddStock.DealerId = AddDealer.Did";

            const string payoutSelect = @"
SELECT DieselLedger.LedgerID AS Sid,
       DieselLedger.Did AS DealerId,
       DieselLedger.Date AS Date,
       IFNULL(AddDealer.DealerName,'') AS DealerName,
       '' AS Vehicle,
       'In' AS SaleInOut,
       '' AS SaleLocation,
       '' AS SaleItem,
       '' AS SaleVehicleModel,
       0 AS AddDisel,
       0 AS Rate,
       0 AS Amount,
       IFNULL(DieselLedger.AmounGiven, 0) AS Credit,
       IFNULL(DieselLedger.Note,'') AS Note,
       CAST(NULL AS REAL) AS RunningBalance,
       'payout' AS Src
FROM DieselLedger
LEFT JOIN AddDealer ON DieselLedger.Did = AddDealer.Did";

            // VIP filters — stock rows; payout (empty VIP) only when filters All
            const string stockVipFilter = @"
  AND (@LocFilter = '' OR TRIM(IFNULL(AddStock.SaleLocation,'')) = @LocFilter)
  AND (@ItemFilter = '' OR TRIM(IFNULL(AddStock.SaleItem,'')) = @ItemFilter)
  AND (@ModelFilter = '' OR TRIM(IFNULL(AddStock.SaleVehicleModel,'')) = @ModelFilter)
  AND (@InOutFilter = '' OR IFNULL(NULLIF(TRIM(IFNULL(AddStock.SaleInOut,'')),''),'In') = @InOutFilter)";

            const string payoutVipFilter = @"
  AND (@LocFilter = '' OR '' = @LocFilter)
  AND (@ItemFilter = '' OR '' = @ItemFilter)
  AND (@ModelFilter = '' OR '' = @ModelFilter)
  AND (@InOutFilter = '' OR 'In' = @InOutFilter)";

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

                if (string.IsNullOrEmpty(searchText))
                {
                    qry = stockSelect + @"
WHERE date(AddStock.Date) >= date(@StartDate)
  AND date(AddStock.Date) <= date(@EndDate)
" + stockVipFilter + @"
UNION ALL
" + payoutSelect + @"
WHERE date(DieselLedger.Date) >= date(@StartDate)
  AND date(DieselLedger.Date) <= date(@EndDate)
" + payoutVipFilter + @"
ORDER BY DealerName COLLATE NOCASE, Date ASC, Sid ASC";
                    cmd.CommandText = qry;
                    cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));
                    BindVipFilters();
                }
                else
                {
                    string searchLike = "%" + searchText + "%";
                    int? exactDealerId = TryGetExactDealerId(con, searchText);

                    if (exactDealerId.HasValue)
                    {
                        qry = stockSelect + @"
WHERE AddStock.DealerId = @DealerId
" + stockVipFilter + @"
UNION ALL
" + payoutSelect + @"
WHERE DieselLedger.Did = @DealerId
" + payoutVipFilter + @"
ORDER BY Date ASC, Sid ASC";
                        cmd.CommandText = qry;
                        cmd.Parameters.AddWithValue("@DealerId", exactDealerId.Value);
                        BindVipFilters();
                    }
                    else
                    {
                        qry = stockSelect + @"
WHERE (
        IFNULL(AddStock.Vehicle, '') LIKE @SearchLike
        OR IFNULL(AddStock.Note, '') LIKE @SearchLike
        OR IFNULL(AddDealer.DealerName, '') LIKE @SearchLike
      )
" + stockVipFilter + @"
UNION ALL
" + payoutSelect + @"
WHERE (
        IFNULL(DieselLedger.Note, '') LIKE @SearchLike
        OR IFNULL(AddDealer.DealerName, '') LIKE @SearchLike
      )
" + payoutVipFilter + @"
ORDER BY DealerName COLLATE NOCASE, Date ASC, Sid ASC";
                        cmd.CommandText = qry;
                        cmd.Parameters.AddWithValue("@SearchLike", searchLike);
                        BindVipFilters();
                    }
                }

                using (cmd)
                using (var da = new SQLiteDataAdapter(cmd))
                    da.Fill(dt);
            }

            ApplyDealerRunningBalance(dt);
            return dt;
        }

        /// <summary>
        /// Running Balance har row — Amount + / Credit − (stock + payout) dealer-wise chronological.
        /// </summary>
        private static void ApplyDealerRunningBalance(DataTable dt)
        {
            if (dt == null || dt.Rows.Count == 0) return;
            if (!dt.Columns.Contains("RunningBalance"))
                dt.Columns.Add("RunningBalance", typeof(decimal));

            // Sort already by dealer/date; recompute sequentially
            int prevDid = int.MinValue;
            decimal run = 0m;
            foreach (DataRow row in dt.Rows)
            {
                int did = row["DealerId"] != DBNull.Value ? Convert.ToInt32(row["DealerId"]) : 0;
                if (did != prevDid)
                {
                    run = 0m;
                    prevDid = did;
                }
                decimal amt = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0m;
                decimal cred = row["Credit"] != DBNull.Value ? Convert.ToDecimal(row["Credit"]) : 0m;
                run += amt - cred;
                row["RunningBalance"] = run;
            }
        }

        private static int? TryGetExactDealerId(SQLiteConnection con, string name)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT Did FROM AddDealer WHERE TRIM(DealerName) = @Name COLLATE NOCASE LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@Name", name.Trim());
                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value) return null;
                return Convert.ToInt32(result);
            }
        }

        private static List<int> GetDealerIdsByNameLike(SQLiteConnection con, string searchLike)
        {
            var ids = new List<int>();
            string exactName = (searchLike ?? "").Trim().Trim('%');
            if (string.IsNullOrEmpty(exactName))
                return ids;

            int? id = TryGetExactDealerId(con, exactName);
            if (id.HasValue)
                ids.Add(id.Value);
            return ids;
        }

        private void BindStockGrid(DataTable dt)
        {
            guna2DataGridView1.SuspendLayout();
            try
            {
                guna2DataGridView1.AutoGenerateColumns = true;
                if (dt != null && dt.Rows.Count > 0)
                {
                    if (!dt.Columns.Contains("Sr"))
                        dt.Columns.Add("Sr", typeof(int));
                    for (int i = 0; i < dt.Rows.Count; i++)
                        dt.Rows[i]["Sr"] = i + 1;

                    guna2DataGridView1.DataSource = dt;
                    ApplyDealerAmountVipGrid();
                    try { EnableQuickDealerAmountCreditEdit(); } catch { }
                }
                else
                {
                    guna2DataGridView1.DataSource = null;
                }

                foreach (DataGridViewColumn column in guna2DataGridView1.Columns)
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            finally
            {
                guna2DataGridView1.ResumeLayout(true);
            }

            CalculateTotals();
            guna2DataGridView1.Invalidate();
        }

        private void CalculateTotals()
        {
            decimal totalIn = 0m;
            decimal totalOut = 0m;
            decimal totalCredit = 0m;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;

                decimal rowAmt = 0m;
                if (guna2DataGridView1.Columns.Contains("Amount") &&
                    row.Cells["Amount"].Value != null && row.Cells["Amount"].Value != DBNull.Value)
                {
                    rowAmt = Convert.ToDecimal(row.Cells["Amount"].Value);
                }
                else if (guna2DataGridView1.Columns.Contains("Rate") &&
                         guna2DataGridView1.Columns.Contains("AddDisel") &&
                         row.Cells["Rate"].Value != null && row.Cells["Rate"].Value != DBNull.Value &&
                         row.Cells["AddDisel"].Value != null && row.Cells["AddDisel"].Value != DBNull.Value)
                {
                    rowAmt = Convert.ToDecimal(row.Cells["Rate"].Value) * Convert.ToDecimal(row.Cells["AddDisel"].Value);
                }

                string io = "In";
                try
                {
                    if (guna2DataGridView1.Columns.Contains("SaleInOut") &&
                        row.Cells["SaleInOut"].Value != null &&
                        row.Cells["SaleInOut"].Value != DBNull.Value)
                        io = row.Cells["SaleInOut"].Value.ToString().Trim();
                }
                catch { }
                if (string.IsNullOrEmpty(io)) io = "In";

                if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                    totalOut += rowAmt;
                else
                    totalIn += rowAmt;

                if (guna2DataGridView1.Columns.Contains("Credit") &&
                    row.Cells["Credit"].Value != null && row.Cells["Credit"].Value != DBNull.Value)
                    totalCredit += Convert.ToDecimal(row.Cells["Credit"].Value);
            }

            decimal net = totalIn - totalOut - totalCredit;
            try
            {
                if (lblTotalAmount != null)
                {
                    lblTotalAmount.Text = $"In: {totalIn:N0}";
                    lblTotalAmount.ForeColor = Color.LightGreen;
                }
                if (lblResult != null)
                {
                    lblResult.Text = $"Out −{totalOut:N0}  |  Credit −{totalCredit:N0}";
                    lblResult.ForeColor = Color.Red;
                    lblResult.Visible = true;
                }
                if (lblTotalDisel != null)
                {
                    lblTotalDisel.Text = $"Net: {net:N0}";
                    lblTotalDisel.ForeColor = Color.FromArgb(212, 175, 55);
                }
            }
            catch { }
        }

        private void guna2DataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = guna2DataGridView1.Columns[e.ColumnIndex].Name;

            // In/Out — Daily Diesel jaisa: Out red, In green (balance math touch nahi)
            if (col == "SaleInOut")
            {
                string io = (e.Value ?? "").ToString().Trim();
                if (string.IsNullOrEmpty(io)) io = "In";
                e.Value = io;
                e.FormattingApplied = true;
                e.CellStyle.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                    e.CellStyle.ForeColor = Color.Red;
                else
                    e.CellStyle.ForeColor = Color.LightGreen;
                return;
            }

            if (col == "Credit" && e.Value != null && e.Value != DBNull.Value)
            {
                if (decimal.TryParse(e.Value.ToString(), out decimal c))
                {
                    e.Value = c.ToString("N2");
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = Color.Red;
                    e.CellStyle.Font = new Font(guna2DataGridView1.Font, FontStyle.Bold);
                }
                return;
            }

            if (col == "RunningBalance" && e.Value != null && e.Value != DBNull.Value)
            {
                if (decimal.TryParse(e.Value.ToString(), out decimal bal))
                {
                    e.Value = bal.ToString("N0");
                    e.FormattingApplied = true;
                    e.CellStyle.ForeColor = Color.FromArgb(212, 175, 55);
                    e.CellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                    e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                return;
            }

            if (col == "Amount" && e.Value != null && e.Value != DBNull.Value)
            {
                if (decimal.TryParse(e.Value.ToString(), out decimal amount))
                {
                    e.Value = amount.ToString("N2");
                    e.FormattingApplied = true;
                    ApplyAmountCellColor(e, amount);
                }
            }
        }

        private static void ApplyAmountCellColor(DataGridViewCellFormattingEventArgs e, decimal amount)
        {
            decimal abs = Math.Abs(amount);
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

        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || guna2DataGridView1.CurrentRow == null) return;

            // Amount / Credit → grid pe seedha edit (form mat kholo) — Daily Diesel jaisa
            if (e.ColumnIndex >= 0)
            {
                string col = guna2DataGridView1.Columns[e.ColumnIndex].Name;
                if (col == "Amount" || col == "Credit")
                {
                    try
                    {
                        if (IsDealerStockEditableRow(e.RowIndex))
                        {
                            guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[e.RowIndex].Cells[e.ColumnIndex];
                            guna2DataGridView1.BeginEdit(true);
                        }
                    }
                    catch { }
                    return;
                }
            }

            string src = "stock";
            if (guna2DataGridView1.Columns.Contains("Src") &&
                guna2DataGridView1.CurrentRow.Cells["Src"].Value != null)
                src = guna2DataGridView1.CurrentRow.Cells["Src"].Value.ToString();

            int id = Convert.ToInt32(guna2DataGridView1.CurrentRow.Cells["Sid"].Value);
            if (string.Equals(src, "payout", StringComparison.OrdinalIgnoreCase))
            {
                var frm = new frmDieselLedgerAdd(id);
                frm.ShowDialog();
            }
            else
            {
                var frm = new frmStockAdd { id = id };
                frm.ShowDialog();
            }
            LoadData1();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void btnAverage_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem != null)
            {
                string selectedOption = comboBox1.SelectedItem.ToString();
                if (selectedOption == "TotalAmount/Divide")
                    CalculateAverageDiselRate();
                else
                {
                    CustomeMessage noDataMessage = new CustomeMessage("Please select a valid option!", "Info");
                    noDataMessage.ShowDialog();
                }
            }
            else
            {
                CustomeMessage noDataMessage = new CustomeMessage("Please select an option from the ComboBox.", "Info");
                noDataMessage.ShowDialog();
            }
        }

        private void CalculateAverageDiselRate()
        {
            DateTime startDate = dtpStart.Value.Date;
            DateTime endDate = dtpEnd.Value.Date;
            decimal totalAmountSum = 0;
            decimal totalDiselSum = 0;

            foreach (DataGridViewRow row in guna2DataGridView1.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Cells["Date"].Value == null || row.Cells["Date"].Value == DBNull.Value) continue;

                DateTime rowDate = Convert.ToDateTime(row.Cells["Date"].Value).Date;
                if (rowDate < startDate || rowDate > endDate) continue;

                if (row.Cells["AddDisel"].Value != null && row.Cells["AddDisel"].Value != DBNull.Value &&
                    row.Cells["Rate"].Value != null && row.Cells["Rate"].Value != DBNull.Value)
                {
                    decimal disel = Convert.ToDecimal(row.Cells["AddDisel"].Value);
                    decimal rate = Convert.ToDecimal(row.Cells["Rate"].Value);
                    totalAmountSum += disel * rate;
                    totalDiselSum += disel;
                }
            }

            lblTotalDisel.Text = $"Total Disel: {totalDiselSum:F2} L";
            lblTotalAmount.Text = $"Total Amount: {totalAmountSum:F2} Rs";

            if (totalDiselSum > 0)
            {
                decimal average = totalAmountSum / totalDiselSum;
                lblResult.Text = $"Sum Amount: {totalAmountSum:F2}, Sum AddDisel: {totalDiselSum:F2}, Average: {average:F2}";
            }
            else
                lblResult.Text = "Total AddDisel is zero, cannot calculate average.";
        }

        private void DatePickers_ValueChanged(object sender, EventArgs e)
        {
            LoadData1();
        }
    }
}
