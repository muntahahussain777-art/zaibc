using ZaibPetroleumService.Model;
using System;
using System.Data;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class frmDieselLedgerView : SampleView
    {
        private bool _busy;

        public frmDieselLedgerView()
        {
            InitializeComponent();
            DealerGridBind.AttachDgvOnly(guna2DataGridView1);
        }

        private void frmDieselLedgerView_Load(object sender, EventArgs e)
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
                     FROM DieselLedgerCredit DL
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
            frmDieselLedgerDealerAdd frmAdd = new frmDieselLedgerDealerAdd();
            frmAdd.ShowDialog();
            LoadLedgerEntries();
        }

        private void guna2DataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (guna2DataGridView1.CurrentRow != null)
            {
                int id = Convert.ToInt32(guna2DataGridView1.CurrentRow.Cells["dgvid"].Value);
                frmDieselLedgerDealerAdd frmEdit = new frmDieselLedgerDealerAdd(id);
                if (frmEdit.ShowDialog() == DialogResult.OK)
                    LoadLedgerEntries();
            }
            else
                MessageBox.Show("Please select a valid record.");
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
