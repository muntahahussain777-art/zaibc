using Microsoft.Reporting.WinForms;
using ZaibPetroleumService.ProjectConnection;
using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;

namespace ZaibPetroleumService.ReportForm
{
    public partial class frmStockDieselForm : Sample
    {
        public frmStockDieselForm()
        {
            InitializeComponent();
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            string searchText = txtSearch.Text;
            string connectionString = projectconnection.conReturn();

            using (SQLiteConnection con = new SQLiteConnection(connectionString))
            {
                con.Open();
                string sql = _usePartyStockQuery
                    ? @"
                    SELECT 
                        StockDiesel.SID AS SID, 
                        StockDiesel.SDid,
                        StockDiesel.Date,
                        IFNULL(StockPartyName.PartyName,'') AS DealerName, 
                        IFNULL(StockDiesel.Vehicle,'') AS Vehicle,
                        IFNULL(StockDiesel.StockType,'') AS StockType,
                        IFNULL(StockDiesel.StockType2,'') AS StockType2,
                        IFNULL(StockDiesel.StockType3,'') AS StockType3,
                        IFNULL(StockDiesel.Note,'') AS Note,
                        IFNULL(StockDiesel.Credit, IFNULL(StockDiesel.Rate,0)) AS Amount,
                        IFNULL(StockDiesel.Rate, IFNULL(StockDiesel.Credit,0)) AS Rate,
                        IFNULL(StockDiesel.Litter,1) AS Litter,
                        StockDiesel.Credit,
                        StockDiesel.Debit
                    FROM StockDiesel
                    LEFT JOIN StockPartyName ON StockDiesel.SDid = StockPartyName.Pid 
                    WHERE (@SearchText = '' OR IFNULL(StockPartyName.PartyName,'') LIKE @SearchLike COLLATE NOCASE
                        OR IFNULL(StockDiesel.Vehicle,'') LIKE @SearchLike COLLATE NOCASE
                        OR IFNULL(StockDiesel.Note,'') LIKE @SearchLike COLLATE NOCASE
                        OR IFNULL(StockDiesel.StockType,'') LIKE @SearchLike COLLATE NOCASE
                        OR IFNULL(StockDiesel.StockType2,'') LIKE @SearchLike COLLATE NOCASE
                        OR IFNULL(StockDiesel.StockType3,'') LIKE @SearchLike COLLATE NOCASE)
                    AND date(StockDiesel.Date) >= date(@FromDate) AND date(StockDiesel.Date) <= date(@ToDate)
                    AND (@NameId = 0 OR StockDiesel.SDid = @NameId)
                    AND (@Type1 = '' OR TRIM(IFNULL(StockDiesel.StockType,'')) = @Type1)
                    AND (@Type2 = '' OR TRIM(IFNULL(StockDiesel.StockType2,'')) = @Type2)
                    AND (@Type3 = '' OR TRIM(IFNULL(StockDiesel.StockType3,'')) = @Type3)
                    ORDER BY date(StockDiesel.Date) ASC, StockDiesel.SID ASC
                    "
                    : @"
                    SELECT 
                        StockDiesel.SID AS SID, 
                        StockDiesel.SDid,
                        StockDiesel.Date,
                        AddDealer.DealerName, 
                        StockDiesel.Vehicle,
                        StockDiesel.Rate,
                        StockDiesel.Litter,
                        StockDiesel.Credit,
                        StockDiesel.Debit,
                        StockDiesel.Note
                    FROM StockDiesel
                    LEFT JOIN AddDealer ON StockDiesel.SDid = AddDealer.Did 
                    WHERE (@SearchText = '' OR IFNULL(AddDealer.DealerName,'') LIKE @SearchLike COLLATE NOCASE OR IFNULL(StockDiesel.Vehicle,'') LIKE @SearchLike COLLATE NOCASE)
                    AND date(StockDiesel.Date) >= date(@FromDate) AND date(StockDiesel.Date) <= date(@ToDate)
                    ORDER BY AddDealer.DealerName ASC
                ";

                SQLiteCommand cmd = new SQLiteCommand(sql, con);

                ReportDateRangeHelper.AddToCommand(cmd, fromdate, todate);
                ReportSearchHelper.BindSearch(cmd, searchText);
                if (_usePartyStockQuery)
                {
                    cmd.Parameters.AddWithValue("@NameId", _partyNameId);
                    cmd.Parameters.AddWithValue("@Type1", _partyType1 ?? "");
                    cmd.Parameters.AddWithValue("@Type2", _partyType2 ?? "");
                    cmd.Parameters.AddWithValue("@Type3", _partyType3 ?? "");
                }

                SQLiteDataAdapter sd = new SQLiteDataAdapter(cmd);
                DataTable dt = new DataTable();
                sd.Fill(dt);

                BindStockDieselReport(dt, fromdate.Value.Date, todate.Value.Date);
            }
        }

        private void frmStockDieselForm_Load(object sender, EventArgs e)
        {
            fromdate.Value = DateTime.Now;
            todate.Value = DateTime.Now;
            LoadAllRecords();
        }

        private void LoadAllRecords()
        {
            // Party/VIP report: SetStockPartyReportFilters → btnSearch loads Roznamcha RDLC
            if (_usePartyStockQuery)
                return;

            string connectionString = projectconnection.conReturn();

            using (SQLiteConnection con = new SQLiteConnection(connectionString))
            {
                SQLiteCommand cmd = new SQLiteCommand(@"
                                       SELECT 
                        StockDiesel.SID AS SID, 
                        StockDiesel.SDid,
                        StockDiesel.Date,
                        AddDealer.DealerName, 
                        StockDiesel.Vehicle,
                        StockDiesel.Rate,
                        StockDiesel.Litter,
                        StockDiesel.Credit,
                        StockDiesel.Debit,
                        StockDiesel.Note
                    FROM StockDiesel
                    LEFT JOIN AddDealer ON StockDiesel.SDid = AddDealer.Did", con);

                SQLiteDataAdapter sd = new SQLiteDataAdapter(cmd);
                DataTable dt = new DataTable();
                sd.Fill(dt);

                BindStockDieselReport(dt);
            }
        }
    }
}
