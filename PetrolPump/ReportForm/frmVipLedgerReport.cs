using System;
using System.Data;
using System.Windows.Forms;

namespace ZaibPetroleumService.ReportForm
{
    /// <summary>
    /// Shared VIP report window — Daily Diesel / DealerAmount (Zaib Service).
    /// </summary>
    public partial class frmVipLedgerReport : Sample
    {
        private DataTable _ledger;
        private string _title = "Report";
        private DateTime _from = DateTime.Today;
        private DateTime _to = DateTime.Today;

        public frmVipLedgerReport()
        {
            InitializeComponent();
        }

        public void LoadVipReport(DataTable ledger, string title, DateTime fromDate, DateTime toDate)
        {
            _ledger = ledger;
            _title = title ?? "Report";
            _from = fromDate.Date;
            _to = toDate.Date;
            Text = "Zaib Service — " + _title;
            try { if (label1 != null) label1.Text = _title; } catch { }
        }

        private void frmVipLedgerReport_Load(object sender, EventArgs e)
        {
            try
            {
                VipLedgerReportHelper.Bind(reportViewer1, _ledger, _title, _from, _to);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Report load error: " + ex.Message, "Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
