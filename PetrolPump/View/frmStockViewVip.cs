using Guna.UI2.WinForms;
using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.ReportForm;

namespace ZaibPetroleumService.View
{
    /// <summary>
    /// DealerAmount VIP grid + filters (Location/Item/Vehicle Model/In-Out) —
    /// Credit red, Running Balance gold, both scrollbars.
    /// </summary>
    public partial class frmStockView
    {
        private static readonly string[] DealerVipFilterLocations = { "Bazar", "Band Murad" };
        private static readonly string[] DealerVipFilterItems = { "Diesel", "Chaliyan" };
        private static readonly string[] DealerVipFilterModels = { "Single", "Das Wheeler", "Cruiser" };

        private bool _dealerVipFiltersReady;
        private bool _suppressDealerVipFilterLoad;
        private Guna2ComboBox cbDealerLocFilter;
        private Guna2ComboBox cbDealerItemFilter;
        private Guna2ComboBox cbDealerModelFilter;
        private Guna2ComboBox cbDealerInOutFilter;
        private Guna2Button _btnDealerVipReport;

        /// <summary>VIP filters + clear panel (Average band; dates rahenge).</summary>
        private void EnsureDealerVipFilters()
        {
            if (_dealerVipFiltersReady) return;
            _dealerVipFiltersReady = true;
            try
            {
                ApplyDealerVipPanelChrome();

                cbDealerLocFilter = MakeDealerVipFilterCombo("cbDealerLocFilter", new Point(12, 52), 125);
                cbDealerLocFilter.Items.Add("All Location");
                foreach (string t in DealerVipFilterLocations) cbDealerLocFilter.Items.Add(t);
                cbDealerLocFilter.SelectedIndex = 0;
                cbDealerLocFilter.SelectedIndexChanged += DealerVipFilter_Changed;

                cbDealerItemFilter = MakeDealerVipFilterCombo("cbDealerItemFilter", new Point(145, 52), 120);
                cbDealerItemFilter.Items.Add("All Item");
                foreach (string t in DealerVipFilterItems) cbDealerItemFilter.Items.Add(t);
                cbDealerItemFilter.SelectedIndex = 0;
                cbDealerItemFilter.SelectedIndexChanged += DealerVipFilter_Changed;

                cbDealerModelFilter = MakeDealerVipFilterCombo("cbDealerModelFilter", new Point(273, 52), 155);
                cbDealerModelFilter.Items.Add("All Vehicle Model");
                foreach (string t in DealerVipFilterModels) cbDealerModelFilter.Items.Add(t);
                cbDealerModelFilter.SelectedIndex = 0;
                cbDealerModelFilter.SelectedIndexChanged += DealerVipFilter_Changed;

                cbDealerInOutFilter = MakeDealerVipFilterCombo("cbDealerInOutFilter", new Point(436, 52), 105);
                cbDealerInOutFilter.Items.Add("All In/Out");
                cbDealerInOutFilter.Items.Add("In");
                cbDealerInOutFilter.Items.Add("Out");
                cbDealerInOutFilter.SelectedIndex = 0;
                cbDealerInOutFilter.SelectedIndexChanged += DealerVipFilter_Changed;

                if (panel1 != null)
                {
                    panel1.Controls.Add(cbDealerLocFilter);
                    panel1.Controls.Add(cbDealerItemFilter);
                    panel1.Controls.Add(cbDealerModelFilter);
                    panel1.Controls.Add(cbDealerInOutFilter);
                    cbDealerLocFilter.BringToFront();
                    cbDealerItemFilter.BringToFront();
                    cbDealerModelFilter.BringToFront();
                    cbDealerInOutFilter.BringToFront();
                }

                SetupDealerVipReportButton();
            }
            catch { }
        }

        private void SetupDealerVipReportButton()
        {
            try
            {
                if (_btnDealerVipReport != null) return;

                _btnDealerVipReport = new Guna2Button
                {
                    Text = "Report",
                    AutoRoundedCorners = true,
                    BorderRadius = 15,
                    Size = new Size(95, 36),
                    FillColor = Color.FromArgb(112, 51, 255),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    Name = "btnDealerVipReport"
                };
                _btnDealerVipReport.Click += (s, e) => OpenDealerVipReport();
                if (panel1 != null)
                {
                    panel1.Controls.Add(_btnDealerVipReport);
                    _btnDealerVipReport.BringToFront();
                }
                PositionDealerVipReportButton();
            }
            catch { }
        }

        private void PositionDealerVipReportButton()
        {
            try
            {
                if (_btnDealerVipReport == null || panel1 == null) return;
                int addLeft = btnAdd != null ? btnAdd.Left : Math.Max(panel1.Width - 120, 660);
                _btnDealerVipReport.Location = new Point(Math.Max(addLeft - _btnDealerVipReport.Width - 8, 560), 10);
                _btnDealerVipReport.BringToFront();
                if (btnAdd != null) btnAdd.BringToFront();
            }
            catch { }
        }

        private void OpenDealerVipReport()
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
                string locF = GetDealerVipLocFilter();
                string itemF = GetDealerVipItemFilter();
                string modelF = GetDealerVipModelFilter();
                string inOutF = GetDealerVipInOutFilter();

                DataTable raw = FetchStockData(searchText, startDate, endDate, locF, itemF, modelF, inOutF);
                DataTable ledger = VipLedgerReportHelper.NormalizeLedgerTable(raw, fromDailyDiesel: false);

                ReportWindowHelper.ShowOnceKeyed("VipLedger.DealerAmount", () =>
                {
                    var frm = new frmVipLedgerReport();
                    frm.LoadVipReport(ledger, "DealerAmount", startDate, endDate);
                    return frm;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("VIP report open nahi ho saki:\n" + ex.Message, "Report",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Clear VIP chrome: Average hide, dates top, In/Out/Credit totals bottom.</summary>
        private void ApplyDealerVipPanelChrome()
        {
            try
            {
                if (panel1 != null)
                    panel1.Height = Math.Max(panel1.Height, 135);

                try
                {
                    if (label1 != null) label1.Text = "DealerAmount";
                    if (txtSearch != null)
                    {
                        txtSearch.PlaceholderText = "Name / Vehicle No";
                        txtSearch.Location = new Point(25, 10);
                    }
                }
                catch { }

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
                PositionDealerVipReportButton();

                if (lblTotalAmount != null)
                {
                    lblTotalAmount.Location = new Point(12, 98);
                    lblTotalAmount.Size = new Size(220, 28);
                    lblTotalAmount.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblTotalAmount.ForeColor = Color.LightGreen;
                    lblTotalAmount.Text = "In: 0";
                    lblTotalAmount.Visible = true;
                    lblTotalAmount.BringToFront();
                }
                if (lblResult != null)
                {
                    lblResult.Location = new Point(240, 98);
                    lblResult.Size = new Size(420, 28);
                    lblResult.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblResult.ForeColor = Color.Red;
                    lblResult.Text = "Out −0  |  Credit −0";
                    lblResult.Visible = true;
                    lblResult.BringToFront();
                }
                if (lblTotalDisel != null)
                {
                    lblTotalDisel.Location = new Point(670, 98);
                    lblTotalDisel.Size = new Size(220, 28);
                    lblTotalDisel.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                    lblTotalDisel.ForeColor = Color.FromArgb(212, 175, 55);
                    lblTotalDisel.Text = "Net: 0";
                    lblTotalDisel.Visible = true;
                    lblTotalDisel.BringToFront();
                }
            }
            catch { }
        }

        private static Guna2ComboBox MakeDealerVipFilterCombo(string name, Point location, int width)
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

        private void DealerVipFilter_Changed(object sender, EventArgs e)
        {
            if (_suppressDealerVipFilterLoad) return;
            try { LoadData1(); } catch { }
        }

        private string GetDealerVipLocFilter()
        {
            try
            {
                if (cbDealerLocFilter == null || cbDealerLocFilter.SelectedIndex <= 0) return "";
                return cbDealerLocFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDealerVipItemFilter()
        {
            try
            {
                if (cbDealerItemFilter == null || cbDealerItemFilter.SelectedIndex <= 0) return "";
                return cbDealerItemFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDealerVipModelFilter()
        {
            try
            {
                if (cbDealerModelFilter == null || cbDealerModelFilter.SelectedIndex <= 0) return "";
                return cbDealerModelFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetDealerVipInOutFilter()
        {
            try
            {
                if (cbDealerInOutFilter == null || cbDealerInOutFilter.SelectedIndex <= 0) return "";
                return cbDealerInOutFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private void ApplyDealerAmountVipGrid()
        {
            try
            {
                if (guna2DataGridView1 == null) return;
                guna2DataGridView1.ScrollBars = ScrollBars.Both;
                guna2DataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                guna2DataGridView1.AllowUserToResizeColumns = true;
                guna2DataGridView1.AutoGenerateColumns = true;

                if (label1 != null) label1.Text = "DealerAmount";
                try { EnsureDealerVipFilters(); } catch { }

                void Style(string name, string header, int width, Color? fore = null, bool bold = false)
                {
                    if (!guna2DataGridView1.Columns.Contains(name)) return;
                    var c = guna2DataGridView1.Columns[name];
                    c.HeaderText = header;
                    c.Width = width;
                    c.MinimumWidth = 60;
                    if (fore.HasValue) c.DefaultCellStyle.ForeColor = fore.Value;
                    if (bold) c.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                }

                if (guna2DataGridView1.Columns.Contains("Sid"))
                    guna2DataGridView1.Columns["Sid"].Visible = false;
                if (guna2DataGridView1.Columns.Contains("DealerId"))
                    guna2DataGridView1.Columns["DealerId"].Visible = false;
                if (guna2DataGridView1.Columns.Contains("Src"))
                    guna2DataGridView1.Columns["Src"].Visible = false;

                Style("Sr", "S/N", 50);
                Style("DealerName", "Name", 140);
                Style("Date", "Date", 100);
                Style("Vehicle", "Vehicle No", 110);
                Style("SaleInOut", "In/Out", 70);
                Style("SaleLocation", "Location", 100);
                Style("SaleItem", "Item", 90);
                Style("SaleVehicleModel", "Vehicle Model", 110);
                Style("AddDisel", "Litter", 90);
                Style("Rate", "Rate", 80);
                Style("Amount", "Amount", 110);
                Style("Credit", "Credit", 100, Color.Red, true);
                Style("Note", "Note", 140);
                Style("RunningBalance", "Running Balance", 140, Color.FromArgb(212, 175, 55), true);

                if (guna2DataGridView1.Columns.Contains("RunningBalance"))
                {
                    var rb = guna2DataGridView1.Columns["RunningBalance"];
                    rb.DefaultCellStyle.Format = "N0";
                    rb.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
                if (guna2DataGridView1.Columns.Contains("Credit"))
                    guna2DataGridView1.Columns["Credit"].DefaultCellStyle.Format = "N2";
                if (guna2DataGridView1.Columns.Contains("Amount"))
                    guna2DataGridView1.Columns["Amount"].DefaultCellStyle.Format = "N2";

                int i = 0;
                void Ord(string n)
                {
                    if (guna2DataGridView1.Columns.Contains(n))
                        guna2DataGridView1.Columns[n].DisplayIndex = i++;
                }
                Ord("Sr"); Ord("DealerName"); Ord("Date"); Ord("Vehicle");
                Ord("SaleInOut"); Ord("SaleLocation"); Ord("SaleItem"); Ord("SaleVehicleModel");
                Ord("AddDisel"); Ord("Rate"); Ord("Amount"); Ord("Credit");
                Ord("Note"); Ord("RunningBalance");
            }
            catch { }
        }
    }
}
