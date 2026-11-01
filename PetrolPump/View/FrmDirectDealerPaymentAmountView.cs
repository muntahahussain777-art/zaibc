using ZaibPetroleumService.Model;
using System;
using System.Data;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class FrmDirectDealerPaymentAmountView : SampleView
    {
        private bool _busy;

        public FrmDirectDealerPaymentAmountView()
        {
            InitializeComponent();
            DealerGridBind.AttachDgvOnly(guna2DataGridView1);
        }

        private void FrmDirectDealerPaymentAmountView_Load(object sender, EventArgs e)
        {
            _busy = true;
            LoadLedgerEntries();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DealerGridBind.StripDgvOnly(guna2DataGridView1);
            _busy = false;
        }

        public void LoadLedgerEntries()
        {
            string searchText = (txtSearch.Text ?? "").Trim().Replace("'", "''");

            string query = @"SELECT 
                    DL.LedgerID,   
                    DL.Did, 
                    DL.Date,
                    DA.DealerName,
                    DL.AmounGiven,
                    DL.Note
                FROM DieselLedgerDebit DL
                LEFT JOIN (SELECT Did, MAX(DealerName) AS DealerName FROM AddDealer GROUP BY Did) DA ON DL.Did = DA.Did
                WHERE IFNULL(DA.DealerName, '') LIKE '%" + searchText + @"%'
                ORDER BY DL.LedgerID ASC";

            BindLedgerGrid(MainClass.GetData(query));
        }

        private void BindLedgerGrid(DataTable dt)
        {
            if (dt == null) dt = new DataTable();
            guna2DataGridView1.SuspendLayout();
            try
            {
                DealerGridBind.ForceNoAuto(guna2DataGridView1);
                guna2DataGridView1.DataSource = null;
                DealerGridBind.StripDgvOnly(guna2DataGridView1);
                EnsureLedgerColumns();
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvid", "LedgerID", false);
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvDate", "Date");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvName", "DealerName");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvAmount", "AmounGiven");
                DealerGridBind.Map(guna2DataGridView1, dt, "dgvNote", "Note");
                guna2DataGridView1.DataSource = dt;
                DealerGridBind.StripDgvOnly(guna2DataGridView1);
                MainClass.SrNo(guna2DataGridView1);
            }
            finally
            {
                guna2DataGridView1.ResumeLayout(true);
            }
        }

        private void EnsureLedgerColumns()
        {
            AddCol("dgvDate", "Date", "Date");
            AddCol("dgvName", "Dealer", "DealerName");
            AddCol("dgvAmount", "Amount", "AmounGiven");
            AddCol("dgvNote", "Note", "Note");
        }

        private void AddCol(string name, string header, string prop)
        {
            if (guna2DataGridView1.Columns.Contains(name))
            {
                guna2DataGridView1.Columns[name].DataPropertyName = prop;
                return;
            }
            guna2DataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                DataPropertyName = prop,
                ReadOnly = true
            });
        }

        public override void btnAdd_Click(object sender, EventArgs e)
        {
            FrmDirectDealerPaymentAmountAdd frmAdd = new FrmDirectDealerPaymentAmountAdd();
            frmAdd.ShowDialog();
            LoadLedgerEntries();
        }

        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            OpenEdit();
        }

        private void OpenEdit()
        {
            if (guna2DataGridView1.CurrentRow == null || guna2DataGridView1.CurrentRow.IsNewRow)
            {
                MessageBox.Show("Please select a valid record.");
                return;
            }

            int id = TryGetLedgerId(guna2DataGridView1.CurrentRow);
            if (id <= 0)
            {
                MessageBox.Show("Please select a valid record.");
                return;
            }

            FrmDirectDealerPaymentAmountAdd frmEdit = new FrmDirectDealerPaymentAmountAdd(id);
            if (frmEdit.ShowDialog() == DialogResult.OK)
                LoadLedgerEntries();
        }

        private static int TryGetLedgerId(DataGridViewRow row)
        {
            try
            {
                if (row.DataBoundItem is DataRowView drv)
                {
                    if (drv.Row.Table.Columns.Contains("LedgerID") && drv["LedgerID"] != DBNull.Value)
                        return Convert.ToInt32(drv["LedgerID"]);
                }
                if (row.DataGridView.Columns.Contains("dgvid") && row.Cells["dgvid"].Value != null &&
                    row.Cells["dgvid"].Value != DBNull.Value)
                    return Convert.ToInt32(row.Cells["dgvid"].Value);
                if (row.DataGridView.Columns.Contains("LedgerID") && row.Cells["LedgerID"].Value != null &&
                    row.Cells["LedgerID"].Value != DBNull.Value)
                    return Convert.ToInt32(row.Cells["LedgerID"].Value);
            }
            catch { }
            return 0;
        }

        public override void txtSearch_TextChanged_1(object sender, EventArgs e) { }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (_busy) return;
            LoadLedgerEntries();
        }

        private void guna2DataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
        }
    }
}
