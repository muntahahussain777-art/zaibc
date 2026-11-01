using Microsoft.Reporting.WinForms;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ZaibPetroleumService.ReportForm
{
    /// <summary>
    /// Shared VIP In/Out ledger report binder — Zaib Service + Running Balance.
    /// Used by Daily Diesel Sales and DealerAmount.
    /// </summary>
    internal static class VipLedgerReportHelper
    {
        public const string ReportFile = "VipInOutLedgerRDLC.rdlc";

        public static DataTable NormalizeLedgerTable(DataTable source, bool fromDailyDiesel)
        {
            var dt = new DataTable("VipLedger");
            dt.Columns.Add("Date", typeof(DateTime));
            dt.Columns.Add("DealerName", typeof(string));
            dt.Columns.Add("Vehicle", typeof(string));
            dt.Columns.Add("SaleInOut", typeof(string));
            dt.Columns.Add("SaleLocation", typeof(string));
            dt.Columns.Add("SaleItem", typeof(string));
            dt.Columns.Add("SaleVehicleModel", typeof(string));
            dt.Columns.Add("Amount", typeof(decimal));
            dt.Columns.Add("Credit", typeof(decimal));
            dt.Columns.Add("RunningBalance", typeof(decimal));
            dt.Columns.Add("Note", typeof(string));

            if (source == null) return dt;

            foreach (DataRow row in source.Rows)
            {
                DataRow nr = dt.NewRow();

                if (row.Table.Columns.Contains("Date") && row["Date"] != DBNull.Value)
                {
                    if (DateTime.TryParse(Convert.ToString(row["Date"]), out DateTime d))
                        nr["Date"] = d.Date;
                    else
                        nr["Date"] = DBNull.Value;
                }
                else
                    nr["Date"] = DBNull.Value;

                if (fromDailyDiesel)
                    nr["DealerName"] = ColStr(row, "Name");
                else
                    nr["DealerName"] = ColStr(row, "DealerName");

                if (fromDailyDiesel)
                    nr["Vehicle"] = ColStr(row, "vehicle");
                else
                    nr["Vehicle"] = ColStr(row, "Vehicle");

                string io = ColStr(row, "SaleInOut");
                if (string.IsNullOrWhiteSpace(io)) io = "In";
                nr["SaleInOut"] = io;

                nr["SaleLocation"] = ColStr(row, "SaleLocation");
                nr["SaleItem"] = ColStr(row, "SaleItem");
                nr["SaleVehicleModel"] = ColStr(row, "SaleVehicleModel");
                nr["Amount"] = ColDec(row, "Amount");
                nr["Credit"] = fromDailyDiesel
                    ? (row.Table.Columns.Contains("CreditVal") ? ColDec(row, "CreditVal") : ColDec(row, "Credit"))
                    : ColDec(row, "Credit");

                if (fromDailyDiesel)
                    nr["RunningBalance"] = row.Table.Columns.Contains("TotalBalance")
                        ? ColDec(row, "TotalBalance")
                        : ColDec(row, "Balance");
                else
                    nr["RunningBalance"] = ColDec(row, "RunningBalance");

                nr["Note"] = ColStr(row, "Note");
                dt.Rows.Add(nr);
            }

            return dt;
        }

        public static void Bind(
            ReportViewer viewer,
            DataTable ledger,
            string reportTitle,
            DateTime fromDate,
            DateTime toDate)
        {
            if (viewer == null) return;
            if (ledger == null) ledger = new DataTable();

            string reportPath = Path.Combine(Application.StartupPath, "Reports", ReportFile);
            if (!File.Exists(reportPath))
            {
                MessageBox.Show("Report file not found: " + reportPath, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            decimal inTotal = 0m, outTotal = 0m, creditTotal = 0m;
            foreach (DataRow row in ledger.Rows)
            {
                decimal amt = row["Amount"] != DBNull.Value ? Convert.ToDecimal(row["Amount"]) : 0m;
                decimal cr = row["Credit"] != DBNull.Value ? Convert.ToDecimal(row["Credit"]) : 0m;
                string io = (row["SaleInOut"]?.ToString() ?? "In").Trim();
                if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                    outTotal += amt;
                else
                    inTotal += amt;
                creditTotal += cr;
            }
            decimal net = inTotal - outTotal - creditTotal;

            DataTable summary = new DataTable("SummarySet");
            summary.Columns.Add("ReportTitle", typeof(string));
            summary.Columns.Add("DateRange", typeof(string));
            summary.Columns.Add("InTotal", typeof(string));
            summary.Columns.Add("OutTotal", typeof(string));
            summary.Columns.Add("CreditTotal", typeof(string));
            summary.Columns.Add("NetTotal", typeof(string));
            summary.Rows.Add(
                reportTitle ?? "Report",
                $"{fromDate:dd-MMM-yyyy}   to   {toDate:dd-MMM-yyyy}",
                inTotal.ToString("N0", CultureInfo.InvariantCulture),
                outTotal.ToString("N0", CultureInfo.InvariantCulture),
                creditTotal.ToString("N0", CultureInfo.InvariantCulture),
                net.ToString("N0", CultureInfo.InvariantCulture));

            viewer.LocalReport.DataSources.Clear();
            viewer.LocalReport.ReportPath = reportPath;
            viewer.LocalReport.DataSources.Add(new ReportDataSource("DataSet1", ledger));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("SummarySet", summary));
            viewer.RefreshReport();
        }

        private static string ColStr(DataRow row, string col)
        {
            if (row == null || !row.Table.Columns.Contains(col) || row[col] == DBNull.Value)
                return "";
            return Convert.ToString(row[col]) ?? "";
        }

        private static decimal ColDec(DataRow row, string col)
        {
            if (row == null || !row.Table.Columns.Contains(col) || row[col] == DBNull.Value)
                return 0m;
            try { return Convert.ToDecimal(row[col]); }
            catch { return 0m; }
        }
    }
}
