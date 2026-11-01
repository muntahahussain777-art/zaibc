using ZaibPetroleumService.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class frmStockDieselView : SampleView
    {
        public frmStockDieselView()
        {
            InitializeComponent();
        }

        private void frmStockDieselView_Load(object sender, EventArgs e)
        {
            try
            {
                // Pehle date range — warna filter combo LoadData1 ko 2024 designer dates se empty dikhata hai
                dtpStart.Value = new DateTime(DateTime.Now.Year, 1, 1);
                dtpEnd.Value = DateTime.Now.Date.AddDays(1).AddSeconds(-1);

                EnsurePartyStockViewUi();
                if (comboBox1.Items.Count == 0)
                    comboBox1.Items.Add("TotalAmount/Divide");
                if (comboBox1.Items.Count > 0)
                    comboBox1.SelectedIndex = 0;

                dtpStart.ValueChanged -= DatePickers_ValueChanged;
                dtpEnd.ValueChanged -= DatePickers_ValueChanged;
                dtpStart.ValueChanged += DatePickers_ValueChanged;
                dtpEnd.ValueChanged += DatePickers_ValueChanged;
                LoadData1();
                SetupStockGridFormatting();
                // Fazool: PDF / Report / Assets band — sirf Stock party work
                HideStockFazoolUi();
                SetupStockAdd2Button();
                SetupStockActionLayout();
                Shown += (s, ev) =>
                {
                    HideStockFazoolUi();
                    PositionTopActionButtons();
                    ApplyPartyGridColumns();
                    try { LoadData1(); } catch { }
                };
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (Load): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        public override void btnAdd_Click(object sender, EventArgs e)
        {
            try
            {
                frmStockDieselAdd frm = new frmStockDieselAdd();
                frm.id = 0;
                frm.ShowDialog(this);
                // Save ke dauran pehle se refresh ho chuka — yahan halka Load
                try { LoadData1(); } catch { }
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (Add): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        private bool _forceShowAllStock;

        /// <summary>Add/Edit/Delete ke baad — halka refresh (date range expand + LoadData1).</summary>
        public void RefreshPartyStockGrid(DateTime? entryDate = null)
        {
            try
            {
                DateTime d = (entryDate ?? DateTime.Now).Date;
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
                    if (dtpEnd != null && dtpEnd.Value.Date < d)
                        dtpEnd.Value = d;
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

                LoadData1();
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage err = new ErrorFormMessage("Error (Refresh): " + ex.Message, "Error");
                    err.ShowDialog();
                }
                catch { }
            }
        }

        /// <summary>Filters/dates clear — nayi save entries grid mein dikhein (Daily Diesel style).</summary>
        public void PreparePartyGridForShowAll(DateTime? entryDate = null)
        {
            try
            {
                dtpStart.ValueChanged -= DatePickers_ValueChanged;
                dtpEnd.ValueChanged -= DatePickers_ValueChanged;
            }
            catch { }

            try
            {
                if (txtSearch != null)
                {
                    txtSearch.TextChanged -= txtSearch_TextChanged;
                    txtSearch.Text = "";
                    txtSearch.TextChanged += txtSearch_TextChanged;
                }
            }
            catch
            {
                try { if (txtSearch != null) txtSearch.Text = ""; } catch { }
            }

            try
            {
                DateTime d = (entryDate ?? DateTime.Now).Date;
                DateTime yearStart = new DateTime(DateTime.Now.Year, 1, 1);
                DateTime endWide = DateTime.Today.AddMonths(1);
                if (d < yearStart) yearStart = d;
                if (d > endWide) endWide = d.AddDays(1);

                if (dtpStart != null)
                {
                    if (dtpStart.Value.Date > yearStart)
                        dtpStart.Value = yearStart;
                    if (dtpStart.Value.Date > d)
                        dtpStart.Value = d;
                }
                if (dtpEnd != null)
                {
                    if (dtpEnd.Value.Date < endWide)
                        dtpEnd.Value = endWide;
                    if (dtpEnd.Value.Date < d)
                        dtpEnd.Value = d;
                }
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

            try { ResetPartyFiltersToAll(); } catch { }
        }

        /// <summary>
        /// Delete ke baad DataSource=null se Guna columns toot jati hain —
        /// naya save DB mein hota hai lekin grid khali dikhti hai. Har bind pe columns reset.
        /// </summary>
        private void BindStockPartyGrid(DataTable displayTable)
        {
            if (guna2DataGridView1 == null) return;

            try
            {
                guna2DataGridView1.SuspendLayout();
                guna2DataGridView1.AutoGenerateColumns = true;
                guna2DataGridView1.DataSource = null;
                guna2DataGridView1.Columns.Clear();
                guna2DataGridView1.AutoGenerateColumns = true;
                guna2DataGridView1.DataSource = displayTable ?? new DataTable();
                try { guna2DataGridView1.Refresh(); } catch { }
            }
            catch
            {
                try
                {
                    guna2DataGridView1.AutoGenerateColumns = true;
                    guna2DataGridView1.DataSource = displayTable;
                }
                catch { return; }
            }
            finally
            {
                try { guna2DataGridView1.ResumeLayout(); } catch { }
            }

            try
            {
                if (guna2DataGridView1.Columns.Contains("Sr"))
                    guna2DataGridView1.Columns["Sr"].HeaderText = "S/N";

                if (guna2DataGridView1.Columns.Contains("DealerName"))
                    guna2DataGridView1.Columns["DealerName"].HeaderText = "Name";

                if (guna2DataGridView1.Columns.Contains("EntryType"))
                {
                    guna2DataGridView1.Columns["EntryType"].HeaderText = "Type";
                    try { guna2DataGridView1.Columns["EntryType"].DisplayIndex = 6; } catch { }
                }

                if (guna2DataGridView1.Columns.Contains("TotalLitter"))
                {
                    guna2DataGridView1.Columns["TotalLitter"].HeaderText = "Total Litter";
                    guna2DataGridView1.Columns["TotalLitter"].DefaultCellStyle.Format = "F2";
                }

                if (guna2DataGridView1.Columns.Contains("Balance"))
                {
                    guna2DataGridView1.Columns["Balance"].HeaderText = "Balance";
                    guna2DataGridView1.Columns["Balance"].DefaultCellStyle.Format = "F2";
                }

                if (guna2DataGridView1.Columns.Contains("Amount"))
                {
                    guna2DataGridView1.Columns["Amount"].HeaderText = "Amount";
                    guna2DataGridView1.Columns["Amount"].DefaultCellStyle.Format = "N2";
                }

                if (guna2DataGridView1.Columns.Contains("SID"))
                    guna2DataGridView1.Columns["SID"].Visible = false;

                if (guna2DataGridView1.Columns.Contains("SDid"))
                    guna2DataGridView1.Columns["SDid"].Visible = false;

                ApplyPartyGridColumns();
            }
            catch { }

            try
            {
                foreach (DataGridViewColumn column in guna2DataGridView1.Columns)
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            catch { }
        }

        private void LoadData1()
        {
            try
            {
                // StockType3 column ensure — pehle save ke baad grid empty na ho
                try
                {
                    ProjectConnection.projectconnection.EnsureSchemaUpdates();
                }
                catch { }

                string searchText = (txtSearch != null ? txtSearch.Text : "") ?? "";
                searchText = searchText.Trim();

                string typeFilter = "";
                string type2Filter = "";
                string type3Filter = "";
                int nameFilterId = 0;
                if (!_forceShowAllStock)
                {
                    try { typeFilter = GetPartyTypeFilter(); } catch { typeFilter = ""; }
                    try { type2Filter = GetPartyType2Filter(); } catch { type2Filter = ""; }
                    try { type3Filter = GetPartyType3Filter(); } catch { type3Filter = ""; }
                    try { nameFilterId = GetPartyNameFilterId(); } catch { nameFilterId = 0; }
                }
                else
                {
                    searchText = "";
                }

                DateTime startDate = dtpStart != null ? dtpStart.Value.Date : new DateTime(DateTime.Now.Year, 1, 1);
                DateTime endDate = dtpEnd != null ? dtpEnd.Value.Date : DateTime.Today.AddMonths(1);
                if (_forceShowAllStock || (startDate.Year <= 2024 && endDate.Year <= 2024 && startDate == endDate))
                {
                    startDate = new DateTime(DateTime.Now.Year, 1, 1);
                    endDate = DateTime.Today.AddMonths(1);
                    try
                    {
                        if (dtpStart != null) dtpStart.Value = startDate;
                        if (dtpEnd != null) dtpEnd.Value = endDate;
                    }
                    catch { }
                }
                if (endDate < startDate)
                {
                    DateTime tmp = startDate;
                    startDate = endDate;
                    endDate = tmp;
                }

                // Save ke baad sab entries dikhao — date optional wide range
                string qry = @"
SELECT 
    StockDiesel.SID AS SID,
    StockDiesel.SDid,
    StockDiesel.Date,
    IFNULL(StockPartyName.PartyName,'') AS DealerName, 
    StockDiesel.Vehicle,
    IFNULL(StockDiesel.StockType,'') AS StockType,
    IFNULL(StockDiesel.StockType2,'') AS StockType2,
    IFNULL(StockDiesel.StockType3,'') AS StockType3,
    StockDiesel.Rate,
    StockDiesel.Litter,
    StockDiesel.Credit,
    StockDiesel.Debit,
    StockDiesel.Note
FROM StockDiesel
LEFT JOIN StockPartyName ON StockDiesel.SDid = StockPartyName.Pid 
WHERE 1=1 ";
                Hashtable parameters = new Hashtable();

                if (!_forceShowAllStock)
                {
                    qry += @"
  AND date(IFNULL(StockDiesel.Date,'1900-01-01')) >= date(@StartDate)
  AND date(IFNULL(StockDiesel.Date,'1900-01-01')) <= date(@EndDate)
  AND (
        @searchText = '%%'
        OR IFNULL(StockPartyName.PartyName,'') LIKE @searchText COLLATE NOCASE
        OR IFNULL(StockDiesel.Vehicle,'') LIKE @searchText COLLATE NOCASE
        OR IFNULL(StockDiesel.Note,'') LIKE @searchText COLLATE NOCASE
        OR IFNULL(StockDiesel.StockType,'') LIKE @searchText COLLATE NOCASE
        OR IFNULL(StockDiesel.StockType2,'') LIKE @searchText COLLATE NOCASE
        OR IFNULL(StockDiesel.StockType3,'') LIKE @searchText COLLATE NOCASE
        OR CAST(IFNULL(StockDiesel.Credit,0) AS TEXT) LIKE @searchText
        OR CAST(IFNULL(StockDiesel.Rate,0) AS TEXT) LIKE @searchText
      )";
                    parameters.Add("@searchText", "%" + searchText + "%");
                    parameters.Add("@StartDate", startDate.ToString("yyyy-MM-dd"));
                    parameters.Add("@EndDate", endDate.ToString("yyyy-MM-dd"));
                    if (nameFilterId > 0)
                    {
                        qry += " AND StockDiesel.SDid = @NameId ";
                        parameters.Add("@NameId", nameFilterId);
                    }
                    if (!string.IsNullOrEmpty(typeFilter))
                    {
                        qry += " AND TRIM(IFNULL(StockDiesel.StockType,'')) = @StockType ";
                        parameters.Add("@StockType", typeFilter);
                    }
                    if (!string.IsNullOrEmpty(type2Filter))
                    {
                        qry += " AND TRIM(IFNULL(StockDiesel.StockType2,'')) = @StockType2 ";
                        parameters.Add("@StockType2", type2Filter);
                    }
                    if (!string.IsNullOrEmpty(type3Filter))
                    {
                        qry += " AND TRIM(IFNULL(StockDiesel.StockType3,'')) = @StockType3 ";
                        parameters.Add("@StockType3", type3Filter);
                    }
                }

                qry += " ORDER BY date(StockDiesel.Date) ASC, StockDiesel.SID ASC;";

                DataTable dt = MainClass.ExecuteSelectQuery(qry, parameters.Count > 0 ? parameters : null);

                if (dt != null && dt.Rows.Count > 0)
                {
                    DataTable displayTable = new DataTable();
                    displayTable.Columns.Add("Sr", typeof(int));
                    displayTable.Columns.Add("SID", typeof(int));
                    displayTable.Columns.Add("SDid", typeof(int));
                    displayTable.Columns.Add("Date", typeof(DateTime));
                    displayTable.Columns.Add("DealerName", typeof(string));
                    displayTable.Columns.Add("Vehicle", typeof(string));
                    displayTable.Columns.Add("StockType", typeof(string));
                    displayTable.Columns.Add("StockType2", typeof(string));
                    displayTable.Columns.Add("StockType3", typeof(string));
                    displayTable.Columns.Add("Rate", typeof(decimal));
                    displayTable.Columns.Add("EntryType", typeof(string));
                    displayTable.Columns.Add("Litter", typeof(decimal));
                    displayTable.Columns.Add("Amount", typeof(decimal));
                    displayTable.Columns.Add("Credit", typeof(decimal));
                    displayTable.Columns.Add("Debit", typeof(decimal));
                    displayTable.Columns.Add("TotalLitter", typeof(decimal));
                    displayTable.Columns.Add("Balance", typeof(decimal));
                    displayTable.Columns.Add("Note", typeof(string));

                    var dealerTotals = new Dictionary<int, (decimal totalLitter, decimal totalCredit, decimal totalDebit)>();
                    var dealerNames = new Dictionary<int, string>();

                    foreach (DataRow row in dt.Rows)
                    {
                        try
                        {
                            int dealerId = row["SDid"] != DBNull.Value ? Convert.ToInt32(row["SDid"]) : 0;
                            int sid = row["SID"] != DBNull.Value ? Convert.ToInt32(row["SID"]) : 0;
                            string dealerName = row["DealerName"] != DBNull.Value ? row["DealerName"].ToString() : "Unknown";

                            decimal litterValue = row["Litter"] != DBNull.Value ? Convert.ToDecimal(row["Litter"]) : 0;
                            string noteValue = row["Note"] != DBNull.Value ? row["Note"].ToString() : "";
                            decimal signedLitter = StockDieselLitterHelper.GetSignedLitter(litterValue, noteValue);
                            decimal displayLitter = StockDieselLitterHelper.GetDisplayLitter(litterValue, noteValue);
                            string entryType = StockDieselLitterHelper.GetEntryType(litterValue, noteValue);
                            string displayNote = StockDieselLitterHelper.StripMinusTag(noteValue);

                            decimal creditValue = row["Credit"] != DBNull.Value ? Convert.ToDecimal(row["Credit"]) : 0;
                            decimal debitValue = row["Debit"] != DBNull.Value ? Convert.ToDecimal(row["Debit"]) : 0;
                            decimal rateValue = row["Rate"] != DBNull.Value ? Convert.ToDecimal(row["Rate"]) : 0;

                            if (!dealerTotals.ContainsKey(dealerId))
                            {
                                dealerTotals[dealerId] = (0, 0, 0);
                                dealerNames[dealerId] = dealerName;
                            }

                            dealerTotals[dealerId] = (
                                dealerTotals[dealerId].totalLitter + signedLitter,
                                dealerTotals[dealerId].totalCredit + creditValue,
                                dealerTotals[dealerId].totalDebit + debitValue
                            );

                            DataRow newRow = displayTable.NewRow();
                            newRow["Sr"] = displayTable.Rows.Count + 1;
                            newRow["SID"] = sid;
                            newRow["SDid"] = dealerId;

                            DateTime rowDateVal;
                            if (row["Date"] != DBNull.Value &&
                                DateTime.TryParse(Convert.ToString(row["Date"]), out rowDateVal))
                                newRow["Date"] = rowDateVal.Date;
                            else
                                newRow["Date"] = DBNull.Value;

                            newRow["DealerName"] = dealerName;
                            newRow["Vehicle"] = row["Vehicle"] != DBNull.Value ? row["Vehicle"].ToString() : "";
                            newRow["StockType"] = row.Table.Columns.Contains("StockType") && row["StockType"] != DBNull.Value
                                ? row["StockType"].ToString()
                                : "";
                            newRow["StockType2"] = row.Table.Columns.Contains("StockType2") && row["StockType2"] != DBNull.Value
                                ? row["StockType2"].ToString()
                                : "";
                            newRow["StockType3"] = row.Table.Columns.Contains("StockType3") && row["StockType3"] != DBNull.Value
                                ? row["StockType3"].ToString()
                                : "";
                            newRow["Rate"] = rateValue;
                            newRow["EntryType"] = entryType;
                            newRow["Litter"] = displayLitter;
                            decimal amountValue = creditValue != 0 ? creditValue : (displayLitter * rateValue);
                            newRow["Amount"] = amountValue;
                            newRow["Credit"] = creditValue;
                            newRow["Debit"] = debitValue;
                            newRow["Note"] = displayNote;
                            displayTable.Rows.Add(newRow);
                        }
                        catch
                        {
                            // aik row fail ho to baqi entries grid mein aati rahein
                        }
                    }

                    for (int i = 0; i < displayTable.Rows.Count; i++)
                        displayTable.Rows[i]["Sr"] = i + 1;

                    // VIP Stock: dealer total rows mat dikhao — sirf real entries
                    BindStockPartyGrid(displayTable);
                }
                else
                {
                    // Empty bind bhi columns reset — warna delete ke baad naya save hide
                    BindStockPartyGrid(new DataTable());
                    UpdateStockSummaryLabels();
                }

                UpdateStockSummaryLabels();
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (LoadData1): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex < 0 || guna2DataGridView1.CurrentRow == null)
                    return;

                if (!guna2DataGridView1.Columns.Contains("SID"))
                {
                    CustomeMessage msg = new CustomeMessage("SID column nahi mila!", "Warning");
                    msg.ShowDialog();
                    return;
                }

                var cellValue = guna2DataGridView1.CurrentRow.Cells["SID"].Value;
                if (cellValue == null || cellValue == DBNull.Value)
                {
                    CustomeMessage msg = new CustomeMessage("SID empty hai, record edit nahi ho sakta!", "Warning");
                    msg.ShowDialog();
                    return;
                }

                int id = Convert.ToInt32(cellValue);

                frmStockDieselAdd frm = new frmStockDieselAdd();
                frm.id = id; // Set the ID for editing
                frm.ShowDialog(this); // Open the add form in edit mode
                try { ReloadStockNameFilter(); } catch { }
                RefreshPartyStockGrid(DateTime.Now);

                try
                {
                    frm.txtcredit.Focus();
                    frm.txtcredit.SelectAll();
                    frm.txtdebit.Focus();
                    frm.txtdebit.SelectAll();
                }
                catch
                {
                    // agar access na mile to ignore
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (CellDoubleClick): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            try
            {
                LoadData1();
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (Search): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        private void btnAverage_Click(object sender, EventArgs e)
        {
            try
            {
                if (comboBox1.SelectedItem != null)
                {
                    string selectedOption = comboBox1.SelectedItem.ToString();

                    if (selectedOption == "TotalAmount/Divide")
                    {
                        CalculateAverageDiselRate();
                    }
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
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (Average): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        // Method to calculate the sum of Litter * Rate and divide by Litter
        private void CalculateAverageDiselRate()
        {
            try
            {
                if (guna2DataGridView1.DataSource == null)
                {
                    lblResult.Text = "Koi data nahi hai.";
                    return;
                }

                DateTime startDate = dtpStart.Value.Date;
                DateTime endDate = dtpEnd.Value.Date;

                decimal totalAmountSum = 0;
                decimal totalLitterSum = 0;

                foreach (DataGridViewRow row in guna2DataGridView1.Rows)
                {
                    if (row.IsNewRow) continue;

                    if (!guna2DataGridView1.Columns.Contains("Date") ||
                        !guna2DataGridView1.Columns.Contains("Litter") ||
                        !guna2DataGridView1.Columns.Contains("Rate"))
                    {
                        continue;
                    }

                    if (row.Cells["Date"].Value == null || row.Cells["Date"].Value == DBNull.Value)
                        continue;

                    DateTime rowDate;
                    if (!DateTime.TryParse(row.Cells["Date"].Value.ToString(), out rowDate))
                        continue;

                    rowDate = rowDate.Date;

                    if (rowDate >= startDate && rowDate <= endDate)
                    {
                        if (row.Cells["SID"].Value == null || row.Cells["SID"].Value == DBNull.Value)
                            continue;

                        if (row.Cells["Litter"].Value != DBNull.Value &&
                            row.Cells["Rate"].Value != DBNull.Value &&
                            row.Cells["Litter"].Value != null &&
                            row.Cells["Rate"].Value != null)
                        {
                            decimal litter;
                            decimal rate;

                            if (!decimal.TryParse(row.Cells["Litter"].Value.ToString(), out litter))
                                litter = 0;

                            if (!decimal.TryParse(row.Cells["Rate"].Value.ToString(), out rate))
                                rate = 0;

                            string entryType = row.Cells["EntryType"].Value?.ToString() ?? "";
                            bool isMinus = entryType.IndexOf("Minus", StringComparison.OrdinalIgnoreCase) >= 0;
                            decimal signedLitter = isMinus ? -Math.Abs(litter) : Math.Abs(litter);

                            decimal amount = Math.Abs(litter) * rate;
                            totalAmountSum += isMinus ? -amount : amount;
                            totalLitterSum += signedLitter;
                        }
                    }
                }

                UpdateStockSummaryLabels(startDate, endDate);

                if (totalLitterSum != 0)
                {
                    decimal average = totalAmountSum / totalLitterSum;
                    lblResult.Text = $"Sum Amount: {totalAmountSum:N0}, Sum Litter: {totalLitterSum:N2}, Average: {average:N2}";
                }
                else
                {
                    lblResult.Text = "Total Litter is zero, cannot calculate average.";
                }
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (AverageCalc): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }

        private void DatePickers_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                LoadData1();
            }
            catch (Exception ex)
            {
                ErrorFormMessage err = new ErrorFormMessage("Error (DateChange): " + ex.Message, "Error");
                err.ShowDialog();
            }
        }
      
    }
}
