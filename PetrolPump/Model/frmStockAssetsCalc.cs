using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.ReportForm;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public class frmStockAssetsCalc : Form
    {
        private readonly DateTime _startDate;
        private readonly DateTime _endDate;
        private bool _busy;

        private decimal _addLitter;
        private decimal _minusLitter;
        private decimal _addRate;
        private decimal _minusRate;

        private TextBox txtBaqaya;
        private TextBox txtRate;
        private TextBox txtBaqayaValue;
        private TextBox txtPurchase;
        private TextBox txtSale;
        private TextBox txtAssets;
        private Label lblMode;
        private Label lblAddInfo;
        private Label lblMinusInfo;

        public frmStockAssetsCalc(DateTime startDate, DateTime endDate)
        {
            _startDate = startDate.Date;
            _endDate = endDate.Date;
            BuildUi();
            Load += (s, e) => LoadFromStock();
        }

        private void BuildUi()
        {
            Text = "Stock Assets";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(32, 36, 61);
            MinimumSize = new Size(520, 480);
            Size = new Size(640, 760);
            Padding = new Padding(0);
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);

            var bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = Color.FromArgb(26, 30, 52),
                Padding = new Padding(12, 12, 12, 12)
            };

            var btnClose = new Button
            {
                Text = "Close",
                Dock = DockStyle.Right,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();

            var btnReport = new Button
            {
                Text = "Assets → 2 Dealer & Customer",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(112, 51, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold)
            };
            btnReport.FlatAppearance.BorderSize = 0;
            btnReport.Click += (s, e) => SendToTwoTableReport();

            bottom.Controls.Add(btnReport);
            bottom.Controls.Add(btnClose);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(32, 36, 61),
                Padding = new Padding(16)
            };

            var grid = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(32, 36, 61)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var title = MakeLabel("Stock Assets / Profit", 16F, Color.FromArgb(0, 229, 201));
            lblAddInfo = MakeLabel("Add Litter: 0    Rate: 0", 11F, Color.White);
            lblMinusInfo = MakeLabel("Minus/Sale Litter: 0    Rate: 0", 11F, Color.White);
            lblMode = MakeLabel(" ", 10F, Color.FromArgb(212, 175, 55));
            lblMode.MaximumSize = new Size(560, 0);

            txtBaqaya = MakeBox(false);
            txtBaqaya.TextChanged += (s, e) => Recalc();
            txtRate = MakeBox(false);
            txtRate.TextChanged += (s, e) => Recalc();
            txtBaqayaValue = MakeBox(true);
            txtPurchase = MakeBox(false);
            txtPurchase.TextChanged += (s, e) => Recalc();
            txtSale = MakeBox(false);
            txtSale.TextChanged += (s, e) => Recalc();
            txtAssets = MakeBox(true);

            AddRow(grid, title);
            AddRow(grid, lblAddInfo);
            AddRow(grid, lblMinusInfo);
            AddRow(grid, lblMode);
            AddRow(grid, MakeLabel("Baqaya Litter", 11F, Color.White));
            AddRow(grid, txtBaqaya, 40);
            AddRow(grid, MakeLabel("Rate (baqaya ke sath)", 11F, Color.White));
            AddRow(grid, txtRate, 40);
            AddRow(grid, MakeLabel("Baqaya × Rate", 11F, Color.White));
            AddRow(grid, txtBaqayaValue, 40);
            AddRow(grid, MakeLabel("Purchase Amount", 11F, Color.White));
            AddRow(grid, txtPurchase, 40);
            AddRow(grid, MakeLabel("Sale Amount", 11F, Color.White));
            AddRow(grid, txtSale, 40);
            AddRow(grid, MakeLabel("Assets / Profit", 11F, Color.White));
            AddRow(grid, txtAssets, 40);

            scroll.Controls.Add(grid);
            Controls.Add(scroll);
            Controls.Add(bottom);
            grid.PerformLayout();
            scroll.AutoScrollMinSize = new Size(0, Math.Max(820, grid.PreferredSize.Height + 24));

            Resize += (s, e) =>
            {
                int inner = Math.Max(400, ClientSize.Width - 48);
                grid.MinimumSize = new Size(inner, 0);
                lblMode.MaximumSize = new Size(inner, 0);
            };
        }

        private static void AddRow(TableLayoutPanel grid, Control c, int height = 0)
        {
            c.Margin = new Padding(0, 4, 8, 4);
            c.Dock = DockStyle.Fill;
            int r = grid.RowCount++;
            if (height > 0)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            else
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(c, 0, r);
        }

        private static Label MakeLabel(string text, float size, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Font = new Font("Segoe UI Semibold", size, FontStyle.Bold),
                Padding = new Padding(0, 4, 0, 0)
            };
        }

        private static TextBox MakeBox(bool readOnly)
        {
            return new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(37, 41, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Height = 36,
                ReadOnly = readOnly
            };
        }

        private void LoadFromStock()
        {
            _busy = true;
            try
            {
                string qry = "SELECT Rate, Litter, Note, Date, SID FROM StockDiesel ORDER BY date(Date) ASC, SID ASC";
                DataTable dt = MainClass.ExecuteSelectQuery(qry, null);

                DateTime start = _startDate.Date;
                DateTime end = _endDate.Date;
                if (end < start)
                {
                    DateTime tmp = start;
                    start = end;
                    end = tmp;
                }

                decimal addLitter = 0, minusLitter = 0, purchase = 0, sale = 0, sabka = 0;
                decimal latestAddRate = 0, latestMinusRate = 0;
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        decimal rate = row["Rate"] == DBNull.Value ? 0 : Convert.ToDecimal(row["Rate"]);
                        decimal litter = row["Litter"] == DBNull.Value ? 0 : Convert.ToDecimal(row["Litter"]);
                        string note = row["Note"] == DBNull.Value ? "" : Convert.ToString(row["Note"]);
                        DateTime rowDate = DateTime.MinValue;
                        if (row["Date"] != DBNull.Value)
                            DateTime.TryParse(row["Date"].ToString(), out rowDate);
                        rowDate = rowDate.Date;

                        decimal signed = StockDieselLitterHelper.GetSignedLitter(litter, note);
                        decimal qty = StockDieselLitterHelper.GetDisplayLitter(litter, note);
                        bool minus = StockDieselLitterHelper.IsMinusEntry(note, litter);

                        if (rowDate != DateTime.MinValue && rowDate > end)
                            continue;

                        if (minus)
                            latestMinusRate = rate;
                        else
                            latestAddRate = rate;

                        if (rowDate != DateTime.MinValue && rowDate < start)
                        {
                            sabka += signed;
                            continue;
                        }

                        if (minus)
                        {
                            minusLitter += qty;
                            sale += qty * rate;
                        }
                        else
                        {
                            addLitter += qty;
                            purchase += qty * rate;
                        }
                    }
                }

                _addLitter = addLitter;
                _minusLitter = minusLitter;
                _addRate = latestAddRate;
                _minusRate = latestMinusRate;
                decimal baqaya = sabka + addLitter - minusLitter;
                decimal rateForBaqaya = baqaya < 0m ? latestAddRate : latestMinusRate;

                lblAddInfo.Text = "Sabka: " + FormatN(sabka) + "    Add Litter: " + FormatN(addLitter) + "    Rate: " + FormatN(_addRate);
                lblMinusInfo.Text = "Minus/Sale Litter: " + FormatN(minusLitter) + "    Rate: " + FormatN(_minusRate);

                txtBaqaya.Text = FormatN(baqaya);
                txtRate.Text = FormatN(rateForBaqaya);
                txtPurchase.Text = FormatN(purchase);
                txtSale.Text = FormatN(sale);
            }
            finally
            {
                _busy = false;
            }
            Recalc();
        }

        private void Recalc()
        {
            if (_busy) return;

            decimal baqaya = ParseDec(txtBaqaya.Text);
            decimal rate = ParseDec(txtRate.Text);
            decimal purchase = ParseDec(txtPurchase.Text);
            decimal sale = ParseDec(txtSale.Text);
            decimal baqayaValue = StockAssetsCalcEngine.BaqayaValue(baqaya, rate);
            decimal assets = StockAssetsCalcEngine.ComputeAssets(
                baqaya, rate, sale, purchase, _addRate, _minusRate);

            lblMode.Text = baqaya < 0
                ? "Baqaya negative — latest Add rate × baqaya + Purchase − Sale."
                : "Baqaya positive — latest Sale/Minus rate × baqaya + Sale − Purchase.";

            txtBaqayaValue.Text = FormatN(baqayaValue);
            txtAssets.Text = FormatN(assets);
        }

        private void SendToTwoTableReport()
        {
            Recalc();
            decimal assets = ParseDec(txtAssets.Text);
            foreach (Form open in Application.OpenForms)
            {
                var existing = open as TwoTableJoiningReport;
                if (existing == null || existing.IsDisposed) continue;
                existing.ApplyAssetsLine(assets);
                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;
                existing.BringToFront();
                existing.Activate();
                return;
            }

            ReportWindowHelper.ShowOnce(() => new TwoTableJoiningReport(assets));
        }

        private static decimal ParseDec(string text)
        {
            string s = (text ?? "").Replace(",", "").Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v))
                return v;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out v))
                return v;
            return 0;
        }

        private static string FormatN(decimal v)
        {
            return v.ToString("N2", CultureInfo.InvariantCulture);
        }
    }
}
