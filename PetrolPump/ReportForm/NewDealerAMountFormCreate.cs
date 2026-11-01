using Microsoft.Reporting.WinForms;
using Guna.UI2.WinForms;
using ZaibPetroleumService;
using ZaibPetroleumService.ReportForm;
using ZaibPetroleumService.ProjectConnection;
using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HajiBalochSoftwere.ReportForm
{
    public partial class NewDealerAMountFormCreate : Sample
    {
        public NewDealerAMountFormCreate()
        {
            InitializeComponent();
        }

        private void NewDealerAMountFormCreate_Load(object sender, EventArgs e)
        {
            try
            {
                fromdate.Value = DateTime.Now;
                todate.Value = DateTime.Now;
                LoadAllRecords();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Complete Dealer Report open nahi ho saka:\n" + ex.Message,
                    "Report Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadAllRecords()
        {
            // DealerToDealer feature removed — reports mein bhi include nahi
            BindReport(BuildSql(includeDealerToDealer: false, withFilter: false), null, null, null);
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            try
            {
                string searchText = txtSearch.Text ?? "";
                if (fromdate.Value.Date > todate.Value.Date)
                {
                    MessageBox.Show("To Date, From Date se chhoti nahi ho sakti.",
                        "Date Range Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                BindReport(BuildSql(includeDealerToDealer: false, withFilter: true), searchText, fromdate, todate);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Search fail:\n" + ex.Message, "Report Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BindReport(string sql, string searchText, Guna2DateTimePicker from, Guna2DateTimePicker to)
        {
            string reportPath = ResolveReportPath("NewDealerAmountReport.rdlc");
            if (reportPath == null)
            {
                MessageBox.Show("Report file nahi mili: Reports\\NewDealerAmountReport.rdlc",
                    "Missing Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string connectionString = projectconnection.conReturn();
            using (SQLiteConnection con = new SQLiteConnection(connectionString))
            {
                con.Open();
                bool hasDtd = TableExists(con, "DealertoDealer");
                if (!hasDtd)
                    sql = BuildSql(includeDealerToDealer: false, withFilter: searchText != null || from != null);

                using (SQLiteCommand cmd = new SQLiteCommand(sql, con))
                {
                    if (from != null && to != null)
                        ReportDateRangeHelper.AddToCommand(cmd, from, to);
                    if (searchText != null)
                        ReportSearchHelper.BindSearch(cmd, searchText);

                    DataTable dt = new DataTable();
                    using (SQLiteDataAdapter sd = new SQLiteDataAdapter(cmd))
                        sd.Fill(dt);

                    if (dt.Rows.Count == 0 && searchText != null)
                    {
                        MessageBox.Show("Koi data nahi mila is date range ya search ke liye.",
                            "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    reportViewer1.Reset();
                    reportViewer1.ProcessingMode = ProcessingMode.Local;
                    reportViewer1.LocalReport.ReportPath = reportPath;
                    reportViewer1.LocalReport.DataSources.Clear();
                    reportViewer1.LocalReport.DataSources.Add(new ReportDataSource("DataSet1", dt));
                    reportViewer1.RefreshReport();
                }
            }
        }

        private static bool TableExists(SQLiteConnection con, string table)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@n", table);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static string ResolveReportPath(string fileName)
        {
            string[] roots =
            {
                Application.StartupPath,
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFullPath(Path.Combine(Application.StartupPath, "..", ".."))
            };
            foreach (string root in roots)
            {
                string p = Path.Combine(root, "Reports", fileName);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string BuildSql(bool includeDealerToDealer, bool withFilter)
        {
            var sb = new StringBuilder();
            sb.Append(@"
SELECT
    q.Did,
    q.DealerName,
    q.DDAmount_Master,
    q.DAmount_Master,
    q.Balance_Master,
    q.EntrySource,
    q.EntryDate,
    q.Vehicle,
    q.Litter,
    q.Rate,
    q.Amount,
    q.Debit,
    q.Credit,
    q.Note
FROM
(
    SELECT
        d.Did AS Did,
        d.DealerName AS DealerName,
        IFNULL(d.DDAmount, 0) AS DDAmount_Master,
        IFNULL(d.DAmount, 0) AS DAmount_Master,
        (IFNULL(d.DDAmount, 0) - IFNULL(d.DAmount, 0)) AS Balance_Master,
        NULL AS EntrySource,
        NULL AS EntryDate,
        NULL AS Vehicle,
        NULL AS Litter,
        NULL AS Rate,
        NULL AS Amount,
        0 AS Debit,
        0 AS Credit,
        NULL AS Note
    FROM AddDealer d

    UNION ALL

    SELECT
        d.Did AS Did,
        d.DealerName AS DealerName,
        0 AS DDAmount_Master,
        0 AS DAmount_Master,
        0 AS Balance_Master,
        t.Source AS EntrySource,
        t.TranDate AS EntryDate,
        t.Vehicle,
        t.Litter,
        t.Rate,
        t.Amount,
        t.Debit,
        t.Credit,
        t.Note
    FROM AddDealer d
    LEFT JOIN
    (
        SELECT
            'DieselAddLitter' AS Source,
            s.DealerId AS Did,
            s.Date AS TranDate,
            s.Vehicle AS Vehicle,
            IFNULL(s.AddDisel, 0) AS Litter,
            IFNULL(s.Rate, 0) AS Rate,
            (IFNULL(s.AddDisel, 0) * IFNULL(s.Rate, 0)) AS Amount,
            (IFNULL(s.AddDisel, 0) * IFNULL(s.Rate, 0)) AS Debit,
            0 AS Credit,
            s.Note AS Note
        FROM AddStock s

        UNION ALL

        SELECT
            'LedgerCredit' AS Source,
            c.Did AS Did,
            c.Date AS TranDate,
            NULL AS Vehicle,
            0 AS Litter,
            0 AS Rate,
            0 AS Amount,
            0 AS Debit,
            IFNULL(c.AmounGiven, 0) AS Credit,
            c.Note AS Note
        FROM DieselLedgerCredit c

        UNION ALL

        SELECT
            'DirectDealerPayment' AS Source,
            db.Did AS Did,
            db.Date AS TranDate,
            NULL AS Vehicle,
            0 AS Litter,
            0 AS Rate,
            0 AS Amount,
            IFNULL(db.AmounGiven, 0) AS Debit,
            0 AS Credit,
            db.Note AS Note
        FROM DieselLedgerDebit db
");

            if (includeDealerToDealer)
            {
                sb.Append(@"
        UNION ALL

        SELECT
            'DealerToDealer_Pay' AS Source,
            dtd.id AS Did,
            dtd.Date AS TranDate,
            NULL AS Vehicle,
            0 AS Litter,
            0 AS Rate,
            0 AS Amount,
            0 AS Debit,
            IFNULL(dtd.AmounGiven, 0) AS Credit,
            dtd.Note AS Note
        FROM DealertoDealer dtd

        UNION ALL

        SELECT
            'DealerToDealer_Receive' AS Source,
            dtd.Did AS Did,
            dtd.Date AS TranDate,
            NULL AS Vehicle,
            0 AS Litter,
            0 AS Rate,
            0 AS Amount,
            IFNULL(dtd.AmounGiven, 0) AS Debit,
            0 AS Credit,
            dtd.Note AS Note
        FROM DealertoDealer dtd
");
            }

            sb.Append(@"
    ) t ON t.Did = d.Did
) q
");

            if (withFilter)
            {
                sb.Append(@"
WHERE
    (
        @SearchText = ''
        OR TRIM(IFNULL(q.DealerName,'')) = @SearchText COLLATE NOCASE
        OR IFNULL(q.Vehicle, '') LIKE @SearchLike COLLATE NOCASE
    )
    AND
    (
        q.EntryDate IS NULL
        OR (date(q.EntryDate) >= date(@FromDate) AND date(q.EntryDate) <= date(@ToDate))
    )
");
            }

            sb.Append(@"
ORDER BY
    q.DealerName,
    CASE WHEN q.EntrySource IS NULL THEN 0 ELSE 1 END,
    q.EntryDate,
    q.EntrySource;");
            return sb.ToString();
        }
    }
}
