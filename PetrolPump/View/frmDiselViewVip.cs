using Guna.UI2.WinForms;
using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.ReportForm;

namespace ZaibPetroleumService.View
{
    /// <summary>
    /// Daily Diesel Sales VIP grid + filters (Location/Item/Vehicle Model/In-Out);
    /// Balance column hide — sirf Running Balance (TotalBalance); both scrollbars.
    /// </summary>
    public partial class frmDiselView
    {
        private static readonly string[] VipFilterLocations = { "Bazar", "Band Murad" };
        private static readonly string[] VipFilterItems = { "Diesel", "Chaliyan" };
        private static readonly string[] VipFilterModels = { "Single", "Das Wheeler", "Cruiser" };

        private bool _dailyVipGridReady;
        private bool _dailyVipFiltersReady;
        private bool _suppressDailyVipFilterLoad;
        private Guna2ComboBox cbDailyLocFilter;
        private Guna2ComboBox cbDailyItemFilter;
        private Guna2ComboBox cbDailyModelFilter;
        private Guna2ComboBox cbDailyInOutFilter;
        private Guna2Button _btnDailyVipReport;

        private void ApplyDailyVipGridUi()
        {
            if (_dailyVipGridReady) return;
            _dailyVipGridReady = true;

            try
            {
                if (guna2DataGridView1 == null) return;

                guna2DataGridView1.ScrollBars = ScrollBars.Both;
                guna2DataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                guna2DataGridView1.AllowUserToResizeColumns = true;

                EnsureVipGridColumn("dgvSaleInOut", "In/Out", 70);
                EnsureVipGridColumn("dgvSaleLocation", "Location", 110);
                EnsureVipGridColumn("dgvSaleItem", "Item", 100);
                EnsureVipGridColumn("dgvSaleVehicleModel", "Vehicle Model", 120);

                HideVipGridCol("dgvid");
                HideVipGridCol("dgvreciptno");
                HideVipGridCol("dgvAdvance");
                HideVipGridCol("dgvBalance"); // row Balance band — sirf Running Balance

                EnsureDailyVipFilters();
                try { EnableQuickAmountCreditEdit(); } catch { }
            }
            catch { }
        }

        /// <summary>VIP filters + clear panel layout (Average band; dates rahenge).</summary>
        private void EnsureDailyVipFilters()
        {
            if (_dailyVipFiltersReady) return;
            _dailyVipFiltersReady = true;
            try
            {
                ApplyDailyVipPanelChrome();

                cbDailyLocFilter = MakeDailyVipFilterCombo("cbDailyLocFilter", new Point(12, 52), 125);
                cbDailyLocFilter.Items.Add("All Location");
                foreach (string t in VipFilterLocations) cbDailyLocFilter.Items.Add(t);
                cbDailyLocFilter.SelectedIndex = 0;
                cbDailyLocFilter.SelectedIndexChanged += DailyVipFilter_Changed;

                cbDailyItemFilter = MakeDailyVipFilterCombo("cbDailyItemFilter", new Point(145, 52), 120);
                cbDailyItemFilter.Items.Add("All Item");
                foreach (string t in VipFilterItems) cbDailyItemFilter.Items.Add(t);
                cbDailyItemFilter.SelectedIndex = 0;
                cbDailyItemFilter.SelectedIndexChanged += DailyVipFilter_Changed;

                cbDailyModelFilter = MakeDailyVipFilterCombo("cbDailyModelFilter", new Point(273, 52), 155);
                cbDailyModelFilter.Items.Add("All Vehicle Model");
                foreach (string t in VipFilterModels) cbDailyModelFilter.Items.Add(t);
                cbDailyModelFilter.SelectedIndex = 0;
                cbDailyModelFilter.SelectedIndexChanged += DailyVipFilter_Changed;

                cbDailyInOutFilter = MakeDailyVipFilterCombo("cbDailyInOutFilter", new Point(436, 52), 105);
                cbDailyInOutFilter.Items.Add("All In/Out");
                cbDailyInOutFilter.Items.Add("In");
                cbDailyInOutFilter.Items.Add("Out");
                cbDailyInOutFilter.SelectedIndex = 0;
                cbDailyInOutFilter.SelectedIndexChanged += DailyVipFilter_Changed;

                if (panel1 != null)
                {
                    panel1.Controls.Add(cbDailyLocFilter);
                    panel1.Controls.Add(cbDailyItemFilter);
                    panel1.Controls.Add(cbDailyModelFilter);
                    panel1.Controls.Add(cbDailyInOutFilter);
                    cbDailyLocFilter.BringToFront();
                    cbDailyItemFilter.BringToFront();
                    cbDailyModelFilter.BringToFront();
                    cbDailyInOutFilter.BringToFront();
                }

                SetupDailyVipReportButton();
            }
            catch { }
        }

        private void SetupDailyVipReportButton()
        {
            try
            {
                if (_btnDailyVipReport != null) return;

                _btnDailyVipReport = new Guna2Button
                {
                    Text = "Report",
                    AutoRoundedCorners = true,
                    BorderRadius = 15,
                    Size = new Size(95, 36),
                    FillColor = Color.FromArgb(112, 51, 255),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    Name = "btnDailyVipReport"
                };
                _btnDailyVipReport.Click += (s, e) => OpenDailyVipReport();
                if (panel1 != null)
                {
                    panel1.Controls.Add(_btnDailyVipReport);
                    _btnDailyVipReport.BringToFront();
                }
                PositionDailyVipReportButton();
            }
            catch { }
        }

        private void PositionDailyVipReportButton()
        {
            try
            {
                if (_btnDailyVipReport == null || panel1 == null) return;
                int addLeft = btnAdd != null ? btnAdd.Left : Math.Max(panel1.Width - 120, 660);
                _btnDailyVipReport.Location = new Point(Math.Max(addLeft - _btnDailyVipReport.Width - 8, 560), 10);
                _btnDailyVipReport.BringToFront();
                if (btnAdd != null) btnAdd.BringToFront();
            }
            catch { }
        }

        private void OpenDailyVipReport()
        {
            try
            {
                DateTime startDate = dtpStart != null ? dtpStart.Value.Date : DateTime.Today;
                DateTime endDate = dtpEnd != null ? dtpEnd.Value.Date : DateTime.Today;
                if (endDate < startDate)
                {
                    DateTime tmp = startDate;
                    startDate = endDate;
                    endDate = tmp;
                }

                string searchText = txtSearch != null ? (txtSearch.Text ?? "") : "";
                string locF = GetDailyVipLocFilter();
                string itemF = GetDailyVipItemFilter();
                string modelF = GetDailyVipModelFilter();
                string inOutF = GetDailyVipInOutFilter();

                DataTable raw = FetchPetrolData(searchText, startDate, endDate, locF, itemF, modelF, inOutF);
                DataTable ledger = VipLedgerReportHelper.NormalizeLedgerTable(raw, fromDailyDiesel: true);

                ReportWindowHelper.ShowOnceKeyed("VipLedger.DailyDiesel", () =>
                {
                    var frm = new frmVipLedgerReport();
                    frm.LoadVipReport(ledger, "Daily Diesel Sales", startDate, endDate);
                    return frm;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("VIP report open nahi ho saki:\n" + ex.Message, "Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Clear VIP chrome: Average/combo hide, dates top-right, totals bottom row.
        /// </summary>
        private void ApplyDailyVipPanelChrome()
        {
            try
            {
                if (panel1 != null)
                    panel1.Height = Math.Max(panel1.Height, 128);

                try
                {
                    if (txtSearch != null)
                    {
                        txtSearch.PlaceholderText = "Name / Vehicle No";
                        txtSearch.Location = new Point(25, 10);
                    }
                }
                catch { }

                // Average sirf band — date filters lazmi
                try { if (comboBox1 != null) comboBox1.Visible = false; } catch { }
                try { if (btnAverage != null) btnAverage.Visible = false; } catch { }

                if (dtpStart != null)
                {
                    dtpStart.Location = new Point(250, 10);
                    dtpStart.Size = new Size(195, 36);
                    dtpStart.Visible = true;
                    dtpStart.BringToFront();
                }
                if (dtpEnd != null)
                {
                    dtpEnd.Location = new Point(455, 10);
                    dtpEnd.Size = new Size(195, 36);
                    dtpEnd.Visible = true;
                    dtpEnd.BringToFront();
                }
                if (btnAdd != null)
                {
                    btnAdd.Location = new Point(Math.Max(panel1.Width - 120, 660), 10);
                    btnAdd.BringToFront();
                }
                PositionDailyVipReportButton();

                // VIP totals row — In / Out− / Credit− / Litter
                if (lblAmount != null)
                {
                    lblAmount.Location = new Point(12, 94);
                    lblAmount.Size = new Size(220, 28);
                    lblAmount.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblAmount.ForeColor = Color.LightGreen;
                    lblAmount.Text = "In: 0";
                    lblAmount.Visible = true;
                    lblAmount.BringToFront();
                }
                if (lblResult != null)
                {
                    lblResult.Location = new Point(240, 94);
                    lblResult.Size = new Size(420, 28);
                    lblResult.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblResult.ForeColor = Color.Red;
                    lblResult.Text = "Out −0  |  Credit −0";
                    lblResult.Visible = true;
                    lblResult.BringToFront();
                }
                if (lblLitter != null)
                {
                    lblLitter.Location = new Point(670, 94);
                    lblLitter.Size = new Size(220, 28);
                    lblLitter.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblLitter.ForeColor = Color.FromArgb(212, 175, 55);
                    lblLitter.Text = "Net: 0";
                    lblLitter.Visible = true;
                    lblLitter.BringToFront();
                }
            }
            catch { }
        }

        private static Guna2ComboBox MakeDailyVipFilterCombo(string name, Point location, int width)
        {
            return new Guna2ComboBox
            {
                AutoRoundedCorners = true,
                BackColor = Color.Transparent,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                DrawMode = DrawMode.OwnerDrawFixed,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FillColor = Color.FromArgb(32, 36, 61),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                ItemHeight = 28,
                Location = location,
                Name = name,
                Size = new Size(width, 36)
            };
        }

        private void DailyVipFilter_Changed(object sender, EventArgs e)
        {
            if (_suppressDailyVipFilterLoad) return;
            try { LoadData1(); } catch { }
        }

        private string GetDailyVipLocFilter()
        {
            try
            {
                if (cbDailyLocFilter == null || cbDailyLocFilter.SelectedIndex <= 0) return "";
                return cbDailyLocFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDailyVipItemFilter()
        {
            try
            {
                if (cbDailyItemFilter == null || cbDailyItemFilter.SelectedIndex <= 0) return "";
                return cbDailyItemFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDailyVipModelFilter()
        {
            try
            {
                if (cbDailyModelFilter == null || cbDailyModelFilter.SelectedIndex <= 0) return "";
                return cbDailyModelFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDailyVipInOutFilter()
        {
            try
            {
                if (cbDailyInOutFilter == null || cbDailyInOutFilter.SelectedIndex <= 0) return "";
                return cbDailyInOutFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private void EnsureVipGridColumn(string name, string header, int width)
        {
            if (guna2DataGridView1 == null) return;
            if (guna2DataGridView1.Columns.Contains(name)) return;

            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                MinimumWidth = 80,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            guna2DataGridView1.Columns.Add(col);
        }

        private void HideVipGridCol(string name)
        {
            try
            {
                if (guna2DataGridView1 != null && guna2DataGridView1.Columns.Contains(name))
                    guna2DataGridView1.Columns[name].Visible = false;
            }
            catch { }
        }

        private void ApplyDailyVipColumnVisibilityAndOrder()
        {
            try
            {
                ApplyDailyVipGridUi();

                HideVipGridCol("dgvid");
                HideVipGridCol("dgvreciptno");
                HideVipGridCol("dgvAdvance");
                HideVipGridCol("dgvBalance");

                // Order: Sr, Name, Date, Vehicle, In/Out, Location, Item, Vehicle Model, Litter, Rate, Amount, Credit, Note, Running Balance
                SetDisp("dgvSr", 0);
                SetDisp("dgvName", 1);
                SetDisp("dgvDate", 2);
                SetDisp("dgvVechleNo", 3);
                SetDisp("dgvSaleInOut", 4);
                SetDisp("dgvSaleLocation", 5);
                SetDisp("dgvSaleItem", 6);
                SetDisp("dgvSaleVehicleModel", 7);
                SetDisp("dgvLitter", 8);
                SetDisp("dgvrate", 9);
                SetDisp("dgvAmount", 10);
                SetDisp("dgvCredit", 11);
                SetDisp("dgvNote", 12);
                SetDisp("dgvTotalBalance", 13);

                if (guna2DataGridView1.Columns.Contains("dgvSaleInOut"))
                {
                    var io = guna2DataGridView1.Columns["dgvSaleInOut"];
                    io.HeaderText = "In/Out";
                    io.Visible = true;
                    io.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
                    io.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
                if (guna2DataGridView1.Columns.Contains("dgvSaleLocation"))
                {
                    guna2DataGridView1.Columns["dgvSaleLocation"].HeaderText = "Location";
                    guna2DataGridView1.Columns["dgvSaleLocation"].Visible = true;
                }
                if (guna2DataGridView1.Columns.Contains("dgvSaleItem"))
                {
                    guna2DataGridView1.Columns["dgvSaleItem"].HeaderText = "Item";
                    guna2DataGridView1.Columns["dgvSaleItem"].Visible = true;
                }
                if (guna2DataGridView1.Columns.Contains("dgvSaleVehicleModel"))
                {
                    guna2DataGridView1.Columns["dgvSaleVehicleModel"].HeaderText = "Vehicle Model";
                    guna2DataGridView1.Columns["dgvSaleVehicleModel"].Visible = true;
                }

                // VIP Running Balance — sale + simple credit, har row lazmi
                if (guna2DataGridView1.Columns.Contains("dgvTotalBalance"))
                {
                    var col = guna2DataGridView1.Columns["dgvTotalBalance"];
                    col.HeaderText = "Running Balance";
                    col.Visible = true;
                    col.ReadOnly = true;
                    col.MinimumWidth = 130;
                    col.Width = 150;
                    col.DefaultCellStyle.Format = "N0";
                    col.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                    col.DefaultCellStyle.ForeColor = Color.FromArgb(212, 175, 55); // gold VIP
                    col.DefaultCellStyle.BackColor = Color.FromArgb(28, 32, 58);
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    col.DefaultCellStyle.SelectionForeColor = Color.FromArgb(255, 230, 120);
                    col.DefaultCellStyle.SelectionBackColor = Color.FromArgb(88, 60, 160);
                    col.HeaderCell.Style.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
                    col.HeaderCell.Style.ForeColor = Color.FromArgb(212, 175, 55);
                }

                if (guna2DataGridView1.Columns.Contains("dgvAmount"))
                {
                    var amt = guna2DataGridView1.Columns["dgvAmount"];
                    amt.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    amt.DefaultCellStyle.Format = "N0";
                    amt.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                if (guna2DataGridView1.Columns.Contains("dgvCredit"))
                {
                    var credit = guna2DataGridView1.Columns["dgvCredit"];
                    credit.DefaultCellStyle.ForeColor = Color.Red;
                    credit.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    credit.DefaultCellStyle.Format = "N0";
                    credit.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            }
            catch { }
        }

        private void SetDisp(string colName, int index)
        {
            try
            {
                if (guna2DataGridView1 != null && guna2DataGridView1.Columns.Contains(colName))
                    guna2DataGridView1.Columns[colName].DisplayIndex = index;
            }
            catch { }
        }
    }
}
