using Microsoft.Reporting.WinForms;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ZaibPetroleumService.ReportForm
{
    public partial class frmStockDieselForm
    {
        private const string StockDetailReportFile = "StockDieselDetailRDLC.rdlc";
        private const string RoznamchaReportFile = "StockPartyRoznamchaRDLC.rdlc";

        private void BindStockDieselReport(DataTable dt, DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                string dateRange = fromDate.HasValue && toDate.HasValue
                    ? $"{fromDate.Value:dd-MMM-yyyy}   to   {toDate.Value:dd-MMM-yyyy}"
                    : string.Empty;

                if (_usePartyStockQuery)
                {
                    BindRoznamchaReport(dt, dateRange);
                    return;
                }

                StockDieselReportHelper.Summary summary = StockDieselReportHelper.EnrichReportTable(dt);

                string reportPath = Path.Combine(Application.StartupPath, "Reports", StockDetailReportFile);
                if (!File.Exists(reportPath))
                {
                    MessageBox.Show("Report file not found: " + reportPath, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                DataTable summaryTable = StockDieselReportHelper.BuildSummaryTable(summary, dateRange);

                reportViewer1.LocalReport.DataSources.Clear();
                reportViewer1.LocalReport.ReportPath = reportPath;
                reportViewer1.LocalReport.DataSources.Add(new ReportDataSource("DataSet1", dt));
                reportViewer1.LocalReport.DataSources.Add(new ReportDataSource("SummarySet", summaryTable));
                reportViewer1.RefreshReport();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Report load karte waqt error: " + ex.Message, "Report Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// VIP Roznamcha report: Sr/Name/Vehicle/Type/Type2/Note/Amount/Date + Total Amount only.
        /// </summary>
        private void BindRoznamchaReport(DataTable dt, string dateRange)
        {
            string reportPath = Path.Combine(Application.StartupPath, "Reports", RoznamchaReportFile);
            if (!File.Exists(reportPath))
            {
                MessageBox.Show("Report file not found: " + reportPath, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (dt == null)
                dt = new DataTable();

            if (!dt.Columns.Contains("Amount"))
                dt.Columns.Add("Amount", typeof(decimal));
            if (!dt.Columns.Contains("StockType"))
                dt.Columns.Add("StockType", typeof(string));
            if (!dt.Columns.Contains("StockType2"))
                dt.Columns.Add("StockType2", typeof(string));
            if (!dt.Columns.Contains("StockType3"))
                dt.Columns.Add("StockType3", typeof(string));
            if (!dt.Columns.Contains("Note"))
                dt.Columns.Add("Note", typeof(string));
            if (!dt.Columns.Contains("DealerName"))
                dt.Columns.Add("DealerName", typeof(string));
            if (!dt.Columns.Contains("Vehicle"))
                dt.Columns.Add("Vehicle", typeof(string));

            decimal total = 0m;
            foreach (DataRow row in dt.Rows)
            {
                decimal amount = 0m;
                if (row["Amount"] != DBNull.Value)
                    amount = Convert.ToDecimal(row["Amount"]);
                else if (dt.Columns.Contains("Credit") && row["Credit"] != DBNull.Value)
                    amount = Convert.ToDecimal(row["Credit"]);
                else if (dt.Columns.Contains("Rate") && row["Rate"] != DBNull.Value)
                    amount = Convert.ToDecimal(row["Rate"]);

                row["Amount"] = amount;
                total += amount;

                if (row["Note"] != DBNull.Value && row["Note"] != null)
                    row["Note"] = Model.StockDieselLitterHelper.StripMinusTag(row["Note"].ToString());
            }

            DataTable summary = new DataTable("SummaryTable");
            summary.Columns.Add("TotalAmount", typeof(string));
            summary.Columns.Add("DateRange", typeof(string));
            summary.Rows.Add(
                total.ToString("N0", CultureInfo.InvariantCulture),
                dateRange ?? "");

            Text = "Roznamcha Details";
            reportViewer1.LocalReport.DataSources.Clear();
            reportViewer1.LocalReport.ReportPath = reportPath;
            reportViewer1.LocalReport.DataSources.Add(new ReportDataSource("DataSet1", dt));
            reportViewer1.LocalReport.DataSources.Add(new ReportDataSource("SummarySet", summary));
            reportViewer1.RefreshReport();
        }
    }
}
