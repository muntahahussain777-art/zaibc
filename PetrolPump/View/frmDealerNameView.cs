using ZaibPetroleumService.Model;
using System;
using System.Data;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class frmDealerNameView : SampleView
    {
        private bool _busy;

        public frmDealerNameView()
        {
            InitializeComponent();
            DealerGridBind.AttachDgvOnly(guna2DataGridView1);
        }

        private void frmDealerNameView_Load(object sender, EventArgs e)
        {
            _busy = true;
            LoadData1();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DealerGridBind.StripDgvOnly(guna2DataGridView1);
            _busy = false;
        }

        public override void btnAdd_Click(object sender, EventArgs e)
        {
            DealerAdd frm = new DealerAdd();
            frm.ShowDialog();
            LoadData1();
        }

        private void LoadData1()
        {
            string searchText = (txtSearch.Text ?? "").Trim().Replace("'", "''");
            // Live hisab = Dealer Ledger: (Direct + Stock litter×rate) − Payout. Stored DDAmount/DAmount display nahi.
            string qry = @"SELECT d.Did, d.DealerName, d.Date,
  IFNULL((SELECT SUM(IFNULL(AmounGiven,0)) FROM DieselLedgerDebit WHERE Did = d.Did), 0)
+ IFNULL((SELECT SUM(IFNULL(Rate,0)*IFNULL(AddDisel,0)) FROM AddStock WHERE DealerId = d.Did), 0) AS DDAmount,
  IFNULL((SELECT SUM(IFNULL(AmounGiven,0)) FROM DieselLedgerCredit WHERE Did = d.Did), 0) AS DAmount,
  (
    IFNULL((SELECT SUM(IFNULL(AmounGiven,0)) FROM DieselLedgerDebit WHERE Did = d.Did), 0)
  + IFNULL((SELECT SUM(IFNULL(Rate,0)*IFNULL(AddDisel,0)) FROM AddStock WHERE DealerId = d.Did), 0)
  - IFNULL((SELECT SUM(IFNULL(AmounGiven,0)) FROM DieselLedgerCredit WHERE Did = d.Did), 0)
  ) AS Balance
FROM AddDealer d
WHERE IFNULL(d.DealerName,'') LIKE '%" + searchText + @"%'
ORDER BY d.Did";

            DataTable dt = MainClass.GetData(qry);
            guna2DataGridView1.SuspendLayout();
            try
            {
                DealerGridBind.ForceNoAuto(guna2DataGridView1);
                guna2DataGridView1.DataSource = null;
                DealerGridBind.StripDgvOnly(guna2DataGridView1);
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvid", "Did", false);
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvName", "DealerName");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvDealerAmount", "DDAmount");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvAmount", "DAmount");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvBalance", "Balance");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvDate", "Date");
                guna2DataGridView1.DataSource = dt ?? new DataTable();
                DealerGridBind.StripDgvOnly(guna2DataGridView1);
                MainClass.SrNo(guna2DataGridView1);
                foreach (DataGridViewColumn column in guna2DataGridView1.Columns)
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            finally
            {
                guna2DataGridView1.ResumeLayout(true);
            }

            decimal sumDDAmount = 0;
            decimal sumDAmount = 0;
            if (dt != null)
            {
                foreach (DataRow row in dt.Rows)
                {
                    if (row["DDAmount"] != null && row["DDAmount"] != DBNull.Value)
                        sumDDAmount += Convert.ToDecimal(row["DDAmount"]);
                    if (row["DAmount"] != null && row["DAmount"] != DBNull.Value)
                        sumDAmount += Convert.ToDecimal(row["DAmount"]);
                }
            }
            lblDDAmount.Text = sumDDAmount.ToString();
            lblDAmount.Text = sumDAmount.ToString();
            lblBalance.Text = (sumDDAmount - sumDAmount).ToString();
        }

        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (guna2DataGridView1.CurrentRow == null || guna2DataGridView1.CurrentRow.IsNewRow)
                return;
            int id = Convert.ToInt32(guna2DataGridView1.CurrentRow.Cells["dgvid"].Value);
            DealerAdd frm = new DealerAdd();
            frm.id = id;
            frm.ShowDialog();
            LoadData1();
        }

        private void guna2DataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (_busy) return;
            LoadData1();
        }
    }
}
