using Guna.UI2.WinForms;
using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.Model;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.View
{
    /// <summary>
    /// Stock VIP: Name (Add2) / Type / Type2 / Type3 filters + search + Report.
    /// </summary>
    public partial class frmStockDieselView
    {
        private static readonly string[] StockPartyTypesPair1 = { "Bazar", "Band Murad" };
        private static readonly string[] StockPartyTypesPair2 = { "Diesel", "Chaliyan" };
        private static readonly string[] StockPartyTypesPair3 = { "Single", "Das Wheeler", "Cruiser" };

        private Guna2ComboBox cbStockTypeFilter;
        private Guna2ComboBox cbStockType2Filter;
        private Guna2ComboBox cbStockType3Filter;
        private Guna2ComboBox cbStockNameFilter;
        private Guna2Button _btnStockAdd2;
        private Guna2Button _btnStockVipReport;
        private bool _partyViewReady;
        private bool _suppressPartyFilterLoad;

        private void EnsurePartyStockViewUi()
        {
            if (_partyViewReady) return;
            _partyViewReady = true;

            try
            {
                using (var con = new System.Data.SQLite.SQLiteConnection(
                    ZaibPetroleumService.ProjectConnection.projectconnection.conReturn()))
                {
                    con.Open();
                    DatabaseSchemaManager.EnsureProfessionalSchema(con);
                }
                StockPartyNameStore.EnsureDefault();
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage err = new ErrorFormMessage(
                        "Stock schema error: " + ex.Message, "Error");
                    err.ShowDialog();
                }
                catch { }
            }

            EnsureVipFilterCombos();
            HideStockFazoolUi();
            SetupStockVipReportButton();
        }

        private void EnsureVipFilterCombos()
        {
            try
            {
                // Name (Add2 list)
                if (cbStockNameFilter == null)
                {
                    cbStockNameFilter = MakeVipFilterCombo("cbStockNameFilter", new Point(12, 52), 160);
                    if (panel1 != null)
                    {
                        panel1.Controls.Add(cbStockNameFilter);
                        cbStockNameFilter.BringToFront();
                    }
                }
                ReloadStockNameFilter();

                // Type = Pair1
                if (cbStockTypeFilter == null)
                {
                    cbStockTypeFilter = MakeVipFilterCombo("cbStockTypeFilter", new Point(176, 52), 118);
                    cbStockTypeFilter.Items.Add("All Location");
                    foreach (string t in StockPartyTypesPair1)
                        cbStockTypeFilter.Items.Add(t);
                    cbStockTypeFilter.SelectedIndex = 0;
                    cbStockTypeFilter.SelectedIndexChanged += PartyTypeFilter_SelectedIndexChanged;
                    if (panel1 != null)
                    {
                        panel1.Controls.Add(cbStockTypeFilter);
                        cbStockTypeFilter.BringToFront();
                    }
                }

                // Type 2 = Pair2
                if (cbStockType2Filter == null)
                {
                    cbStockType2Filter = MakeVipFilterCombo("cbStockType2Filter", new Point(300, 52), 118);
                    cbStockType2Filter.Items.Add("All Item");
                    foreach (string t in StockPartyTypesPair2)
                        cbStockType2Filter.Items.Add(t);
                    cbStockType2Filter.SelectedIndex = 0;
                    cbStockType2Filter.SelectedIndexChanged += PartyTypeFilter_SelectedIndexChanged;
                    if (panel1 != null)
                    {
                        panel1.Controls.Add(cbStockType2Filter);
                        cbStockType2Filter.BringToFront();
                    }
                }

                // Type 3 = Pair3
                if (cbStockType3Filter == null)
                {
                    cbStockType3Filter = MakeVipFilterCombo("cbStockType3Filter", new Point(424, 52), 150);
                    cbStockType3Filter.Items.Add("All Vehicle Model");
                    foreach (string t in StockPartyTypesPair3)
                        cbStockType3Filter.Items.Add(t);
                    cbStockType3Filter.SelectedIndex = 0;
                    cbStockType3Filter.SelectedIndexChanged += PartyTypeFilter_SelectedIndexChanged;
                    if (panel1 != null)
                    {
                        panel1.Controls.Add(cbStockType3Filter);
                        cbStockType3Filter.BringToFront();
                    }
                }
            }
            catch { }
        }

        private static Guna2ComboBox MakeVipFilterCombo(string name, Point location, int width)
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

        private void ReloadStockNameFilter()
        {
            try
            {
                if (cbStockNameFilter == null) return;

                int keepId = 0;
                try
                {
                    if (cbStockNameFilter.SelectedValue != null &&
                        cbStockNameFilter.SelectedValue != DBNull.Value &&
                        !(cbStockNameFilter.SelectedValue is DataRowView))
                        keepId = Convert.ToInt32(cbStockNameFilter.SelectedValue);
                    else if (cbStockNameFilter.SelectedItem is DataRowView drv && drv["Did"] != DBNull.Value)
                        keepId = Convert.ToInt32(drv["Did"]);
                }
                catch { }

                // Strict: sirf StockPartyName (Add2) — AddDealer kabhi nahi
                DataTable names = MainClass.ExecuteSelectQuery(
                    @"SELECT Pid AS Did, PartyName AS DealerName
                      FROM StockPartyName
                      WHERE TRIM(IFNULL(PartyName,'')) <> ''
                      ORDER BY PartyName COLLATE NOCASE
                      LIMIT 2000", null) ?? new DataTable();

                DataTable combo = new DataTable();
                combo.Columns.Add("Did", typeof(int));
                combo.Columns.Add("DealerName", typeof(string));

                DataRow all = combo.NewRow();
                all["Did"] = 0;
                all["DealerName"] = "All Names";
                combo.Rows.Add(all);
                foreach (DataRow r in names.Rows)
                {
                    DataRow nr = combo.NewRow();
                    nr["Did"] = r["Did"];
                    nr["DealerName"] = r["DealerName"];
                    combo.Rows.Add(nr);
                }

                // Rebind ke dauran SelectedIndexChanged LoadData1 ko galat Name filter na de
                cbStockNameFilter.SelectedIndexChanged -= StockNameFilter_SelectedIndexChanged;
                cbStockNameFilter.DataSource = null;
                cbStockNameFilter.DisplayMember = "DealerName";
                cbStockNameFilter.ValueMember = "Did";
                cbStockNameFilter.DataSource = combo;

                try
                {
                    if (keepId > 0)
                        cbStockNameFilter.SelectedValue = keepId;
                    else
                        cbStockNameFilter.SelectedIndex = 0;
                }
                catch { cbStockNameFilter.SelectedIndex = 0; }

                cbStockNameFilter.SelectedIndexChanged -= StockNameFilter_SelectedIndexChanged;
                cbStockNameFilter.SelectedIndexChanged += StockNameFilter_SelectedIndexChanged;
            }
            catch { }
        }

        private void StockNameFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressPartyFilterLoad) return;
            try { LoadData1(); } catch { }
        }

        private void PartyTypeFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressPartyFilterLoad) return;
            try { LoadData1(); } catch { }
        }

        private void ResetPartyFiltersToAll()
        {
            try
            {
                _suppressPartyFilterLoad = true;

                if (cbStockNameFilter != null && cbStockNameFilter.Items.Count > 0)
                {
                    try { cbStockNameFilter.SelectedIndex = 0; } catch { }
                }

                if (cbStockTypeFilter != null && cbStockTypeFilter.Items.Count > 0)
                    cbStockTypeFilter.SelectedIndex = 0;
                if (cbStockType2Filter != null && cbStockType2Filter.Items.Count > 0)
                    cbStockType2Filter.SelectedIndex = 0;
                if (cbStockType3Filter != null && cbStockType3Filter.Items.Count > 0)
                    cbStockType3Filter.SelectedIndex = 0;
            }
            catch { }
            finally
            {
                _suppressPartyFilterLoad = false;
            }
        }

        private void HideStockFazoolUi()
        {
            try
            {
                if (lblTotalLitter != null)
                {
                    lblTotalLitter.Visible = false;
                    lblTotalLitter.Text = "";
                }
                if (btnAverage != null)
                    btnAverage.Visible = false;
                if (comboBox1 != null)
                    comboBox1.Visible = false;
                if (lblResult != null)
                    lblResult.Visible = false;

                if (_btnStockAssets != null) _btnStockAssets.Visible = false;
                if (_btnStockPdf != null) _btnStockPdf.Visible = false;
                // old Report button hide — VIP Report alag
                if (_btnStockReport != null) _btnStockReport.Visible = false;

                if (lblTotalAmount != null)
                    lblTotalAmount.Visible = true;
            }
            catch { }
        }

        private void SetupStockAdd2Button()
        {
            if (_btnStockAdd2 != null) return;

            _btnStockAdd2 = new Guna2Button
            {
                Text = "Add2",
                AutoRoundedCorners = true,
                BorderRadius = 15,
                Size = new Size(90, 36),
                FillColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                Name = "btnStockAdd2"
            };
            _btnStockAdd2.Click += (s, e) => OpenAdd2NameDialog();
            if (panel1 != null)
            {
                panel1.Controls.Add(_btnStockAdd2);
                _btnStockAdd2.BringToFront();
            }
            PositionTopActionButtons();
        }

        private void SetupStockVipReportButton()
        {
            try
            {
                if (_btnStockVipReport != null) return;

                _btnStockVipReport = new Guna2Button
                {
                    Text = "Report",
                    AutoRoundedCorners = true,
                    BorderRadius = 15,
                    Size = new Size(95, 36),
                    FillColor = Color.FromArgb(112, 51, 255),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                    Name = "btnStockVipReport"
                };
                _btnStockVipReport.Click += (s, e) => OpenStockVipReport();
                if (panel1 != null)
                {
                    panel1.Controls.Add(_btnStockVipReport);
                    _btnStockVipReport.BringToFront();
                }
                PositionTopActionButtons();
            }
            catch { }
        }

        private void OpenAdd2NameDialog()
        {
            try
            {
                using (var frm = new ZaibPetroleumService.Model.frmStockPartyNames())
                    frm.ShowDialog(this);
                try { NameSuggestHelper.Invalidate(); } catch { }
                ReloadStockNameFilter();
                LoadData1();
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage err = new ErrorFormMessage("Add2 error: " + ex.Message, "Error");
                    err.ShowDialog();
                }
                catch { }
            }
        }

        private void OpenStockVipReport()
        {
            try
            {
                SetupStockReportButton();
                if (_btnStockReport != null)
                    _btnStockReport.Visible = false;

                ReportWindowHelper.ShowOnce(() =>
                {
                    var frm = new ZaibPetroleumService.ReportForm.frmStockDieselForm();
                    frm.Text = "Roznamcha Details";
                    string search = (txtSearch != null ? txtSearch.Text : "") ?? "";
                    int nameId = GetPartyNameFilterId();
                    string type1 = GetPartyTypeFilter();
                    string type2 = GetPartyType2Filter();
                    string type3 = GetPartyType3Filter();
                    frm.SetStockPartyReportFilters(
                        dtpStart.Value.Date,
                        dtpEnd.Value.Date,
                        search,
                        nameId,
                        type1,
                        type2,
                        type3);
                    return frm;
                });
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage err = new ErrorFormMessage("Report error: " + ex.Message, "Error");
                    err.ShowDialog();
                }
                catch { }
            }
        }

        private int GetPartyNameFilterId()
        {
            try
            {
                if (cbStockNameFilter == null || cbStockNameFilter.SelectedIndex <= 0)
                    return 0;
                if (cbStockNameFilter.SelectedValue != null &&
                    cbStockNameFilter.SelectedValue != DBNull.Value &&
                    !(cbStockNameFilter.SelectedValue is DataRowView))
                {
                    int id = Convert.ToInt32(cbStockNameFilter.SelectedValue);
                    return id > 0 ? id : 0;
                }
                if (cbStockNameFilter.SelectedItem is DataRowView drv && drv["Did"] != DBNull.Value)
                {
                    int id = Convert.ToInt32(drv["Did"]);
                    return id > 0 ? id : 0;
                }
            }
            catch { }
            return 0;
        }

        private string GetPartyTypeFilter()
        {
            try
            {
                if (cbStockTypeFilter == null || cbStockTypeFilter.SelectedIndex <= 0)
                    return "";
                return cbStockTypeFilter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetPartyType2Filter()
        {
            try
            {
                if (cbStockType2Filter == null || cbStockType2Filter.SelectedIndex <= 0)
                    return "";
                return cbStockType2Filter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private string GetPartyType3Filter()
        {
            try
            {
                if (cbStockType3Filter == null || cbStockType3Filter.SelectedIndex <= 0)
                    return "";
                return cbStockType3Filter.SelectedItem?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private void ApplyPartyGridColumns()
        {
            try
            {
                if (guna2DataGridView1 == null) return;

                string[] hide = { "Rate", "Litter", "Credit", "Debit", "TotalLitter", "Balance", "EntryType", "SDid", "SID" };
                foreach (string c in hide)
                {
                    try
                    {
                        if (guna2DataGridView1.Columns.Contains(c))
                            guna2DataGridView1.Columns[c].Visible = false;
                    }
                    catch { }
                }

                // Order: S/N, Name, Vehicle No, Location, Item, Vehicle Model, Note, Amount, Date
                SetPartyCol("Sr", "S/N", true, 0);
                SetPartyCol("DealerName", "Name", true, 1);
                SetPartyCol("Vehicle", "Vehicle No", true, 2);
                SetPartyCol("StockType", "Location", true, 3);
                SetPartyCol("StockType2", "Item", true, 4);
                SetPartyCol("StockType3", "Vehicle Model", true, 5);
                SetPartyCol("Note", "Note", true, 6);
                if (guna2DataGridView1.Columns.Contains("Amount"))
                {
                    guna2DataGridView1.Columns["Amount"].HeaderText = "Amount";
                    guna2DataGridView1.Columns["Amount"].DefaultCellStyle.Format = "N2";
                    guna2DataGridView1.Columns["Amount"].Visible = true;
                    try { guna2DataGridView1.Columns["Amount"].DisplayIndex = 7; } catch { }
                }
                SetPartyCol("Date", "Date", true, 8);
            }
            catch { }
        }

        private void SetPartyCol(string name, string header, bool visible, int displayIndex)
        {
            try
            {
                if (guna2DataGridView1 == null || !guna2DataGridView1.Columns.Contains(name)) return;
                var col = guna2DataGridView1.Columns[name];
                col.HeaderText = header;
                col.Visible = visible;
                col.DisplayIndex = displayIndex;
            }
            catch { }
        }
    }
}
