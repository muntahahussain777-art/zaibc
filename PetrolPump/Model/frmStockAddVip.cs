using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    /// <summary>
    /// DealerAmount VIP — In/Out + Location/Item/Vehicle Model (empty default like Daily Diesel),
    /// Credit, Amount, Ctrl+1 text, Vehicle auto-suggest. Sync untouched.
    /// </summary>
    public partial class frmStockAdd
    {
        private static readonly string[] VipLocations = { "Bazar", "Band Murad" };
        private static readonly string[] VipItems = { "Diesel", "Chaliyan" };
        private static readonly string[] VipVehicleModels = { "Single", "Das Wheeler", "Cruiser" };
        private static readonly string[] VipInOut = { "In", "Out" };

        private bool _dealerVipReady;
        private bool _dealerVipFastText;
        private bool _amountManual;

        private Label lblSaleLocation;
        private Label lblSaleItem;
        private Label lblSaleVehicleModel;
        private Label lblSaleInOut;
        private Label lblCredit;
        private Label lblAmount;
        private Guna.UI2.WinForms.Guna2ComboBox cbSaleLocation;
        private Guna.UI2.WinForms.Guna2ComboBox cbSaleItem;
        private Guna.UI2.WinForms.Guna2ComboBox cbSaleVehicleModel;
        private Guna.UI2.WinForms.Guna2ComboBox cbSaleInOut;
        private Guna.UI2.WinForms.Guna2TextBox txtSaleLocation;
        private Guna.UI2.WinForms.Guna2TextBox txtSaleItem;
        private Guna.UI2.WinForms.Guna2TextBox txtSaleVehicleModel;
        private Guna.UI2.WinForms.Guna2TextBox txtCredit;

        private void ApplyDealerAmountVipMode()
        {
            try
            {
                using (var con = new System.Data.SQLite.SQLiteConnection(
                    ProjectConnection.projectconnection.conReturn()))
                {
                    con.Open();
                    DatabaseSchemaManager.EnsureProfessionalSchema(con);
                }
            }
            catch { }

            if (_dealerVipReady) return;
            _dealerVipReady = true;

            try
            {
                EnsureDealerVipControls();
                EnsureVipComboItemsAllowEmpty(cbSaleLocation, VipLocations);
                EnsureVipComboItemsAllowEmpty(cbSaleItem, VipItems);
                EnsureVipComboItemsAllowEmpty(cbSaleVehicleModel, VipVehicleModels);
                EnsureSaleInOutCombo();
                ApplyDealerVipLayout();
                ApplyDealerVipTabOrder();
                RefreshDealerVehicleAutoSuggest();

                if (label1 != null) label1.Text = "DealerAmount";
                if (label7 != null) label7.Text = "Litter";
                if (txtlitterRateAmount != null)
                {
                    txtlitterRateAmount.ReadOnly = false;
                    txtlitterRateAmount.PlaceholderText = "Amount";
                }
                ApplyDealerMoneyFieldStyle();
                // Daily Diesel jaisa: Location/Item/Model empty; In selected
                ClearVipLocationItemModel();
                if (cbSaleInOut != null && cbSaleInOut.Items.Count > 0)
                    cbSaleInOut.SelectedIndex = 0;
            }
            catch { }
        }

        private bool _fmtMoney;
        private static readonly Font MoneyFont = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);

        /// <summary>Litter / Rate / Amount / Credit — bada font + typing pe 100,000 comma.</summary>
        private void ApplyDealerMoneyFieldStyle()
        {
            StyleMoneyBox(txtAddDisel);
            StyleMoneyBox(txtRate);
            StyleMoneyBox(txtlitterRateAmount);
            StyleMoneyBox(txtCredit);
            if (txtCredit != null) txtCredit.ForeColor = Color.Red;

            if (txtAddDisel != null)
            {
                txtAddDisel.TextChanged -= DealerMoney_TextChanged;
                txtAddDisel.TextChanged += DealerMoney_TextChanged;
            }
            if (txtRate != null)
            {
                txtRate.TextChanged -= DealerMoney_TextChanged;
                txtRate.TextChanged += DealerMoney_TextChanged;
            }
            if (txtlitterRateAmount != null)
            {
                txtlitterRateAmount.TextChanged -= TxtAmount_TextChanged;
                txtlitterRateAmount.TextChanged -= DealerMoney_TextChanged;
                txtlitterRateAmount.TextChanged += DealerMoney_TextChanged;
                txtlitterRateAmount.TextChanged += TxtAmount_TextChanged;
            }
            if (txtCredit != null)
            {
                txtCredit.TextChanged -= DealerMoney_TextChanged;
                txtCredit.TextChanged += DealerMoney_TextChanged;
            }
        }

        private void StyleMoneyBox(Guna.UI2.WinForms.Guna2TextBox box)
        {
            if (box == null) return;
            box.Font = MoneyFont;
            box.Height = 40;
        }

        private void DealerMoney_TextChanged(object sender, EventArgs e)
        {
            if (_fmtMoney) return;
            var box = sender as Guna.UI2.WinForms.Guna2TextBox;
            if (box == null) return;
            try
            {
                string raw = (box.Text ?? "").Replace(",", "").Trim();
                if (string.IsNullOrEmpty(raw)) return;
                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
                    return;

                string formatted = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);
                if (formatted == box.Text) return;

                _fmtMoney = true;
                int caretFromEnd = box.Text.Length - box.SelectionStart;
                box.Text = formatted;
                box.SelectionStart = Math.Max(0, box.Text.Length - Math.Max(0, caretFromEnd));
                _fmtMoney = false;
            }
            catch { _fmtMoney = false; }
        }

        private void EnsureDealerVipControls()
        {
            if (cbSaleLocation != null) return;

            lblSaleLocation = MakeLbl("Location");
            lblSaleItem = MakeLbl("Item");
            lblSaleVehicleModel = MakeLbl("Vehicle Model");
            lblCredit = MakeLbl("Credit");
            lblAmount = MakeLbl("Amount");

            cbSaleLocation = MakeComboEmpty(VipLocations);
            cbSaleItem = MakeComboEmpty(VipItems);
            cbSaleVehicleModel = MakeComboEmpty(VipVehicleModels);

            txtSaleLocation = MakeTxt();
            txtSaleItem = MakeTxt();
            txtSaleVehicleModel = MakeTxt();
            txtCredit = MakeTxt();
            txtCredit.ForeColor = Color.Red;

            txtSaleLocation.Visible = false;
            txtSaleItem.Visible = false;
            txtSaleVehicleModel.Visible = false;

            SetupVipTextAutoComplete(txtSaleLocation, VipLocations);
            SetupVipTextAutoComplete(txtSaleItem, VipItems);
            SetupVipTextAutoComplete(txtSaleVehicleModel, VipVehicleModels);

            Controls.Add(lblSaleLocation);
            Controls.Add(cbSaleLocation);
            Controls.Add(txtSaleLocation);
            Controls.Add(lblSaleItem);
            Controls.Add(cbSaleItem);
            Controls.Add(txtSaleItem);
            Controls.Add(lblSaleVehicleModel);
            Controls.Add(cbSaleVehicleModel);
            Controls.Add(txtSaleVehicleModel);
            Controls.Add(lblCredit);
            Controls.Add(txtCredit);
            Controls.Add(lblAmount);
        }

        private void EnsureSaleInOutCombo()
        {
            if (cbSaleInOut != null) return;
            lblSaleInOut = MakeLbl("In/Out");
            cbSaleInOut = new Guna.UI2.WinForms.Guna2ComboBox
            {
                AutoRoundedCorners = true,
                BackColor = Color.Transparent,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                DrawMode = DrawMode.OwnerDrawFixed,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FillColor = Color.FromArgb(37, 41, 74),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                ItemHeight = 30,
                Size = new Size(120, 36)
            };
            foreach (string t in VipInOut)
                cbSaleInOut.Items.Add(t);
            cbSaleInOut.SelectedIndex = 0;
            Controls.Add(lblSaleInOut);
            Controls.Add(cbSaleInOut);
            lblSaleInOut.BringToFront();
            cbSaleInOut.BringToFront();
        }

        private static Label MakeLbl(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Text = text
            };
        }

        private static Guna.UI2.WinForms.Guna2ComboBox MakeComboEmpty(string[] items)
        {
            var cb = new Guna.UI2.WinForms.Guna2ComboBox
            {
                AutoRoundedCorners = true,
                BackColor = Color.Transparent,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                DrawMode = DrawMode.OwnerDrawFixed,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FillColor = Color.FromArgb(37, 41, 74),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                ItemHeight = 30,
                Size = new Size(200, 36)
            };
            cb.Items.Add(""); // Daily Diesel jaisa — pehle empty
            if (items != null)
                foreach (string t in items) cb.Items.Add(t);
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
            return cb;
        }

        private static void EnsureVipComboItemsAllowEmpty(Guna.UI2.WinForms.Guna2ComboBox cb, string[] items)
        {
            if (cb == null || items == null) return;
            bool hasBlank = false;
            foreach (object o in cb.Items)
            {
                if (string.IsNullOrWhiteSpace(o?.ToString()))
                {
                    hasBlank = true;
                    break;
                }
            }
            if (!hasBlank)
                cb.Items.Insert(0, "");
            foreach (string t in items)
            {
                bool exists = false;
                foreach (object o in cb.Items)
                {
                    if (string.Equals(o?.ToString()?.Trim(), t, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) cb.Items.Add(t);
            }
            if (cb.Items.Count > 0 && cb.SelectedIndex < 0)
                cb.SelectedIndex = 0;
        }

        private void ClearVipLocationItemModel()
        {
            try
            {
                if (cbSaleLocation != null && cbSaleLocation.Items.Count > 0)
                    cbSaleLocation.SelectedIndex = 0;
                if (cbSaleItem != null && cbSaleItem.Items.Count > 0)
                    cbSaleItem.SelectedIndex = 0;
                if (cbSaleVehicleModel != null && cbSaleVehicleModel.Items.Count > 0)
                    cbSaleVehicleModel.SelectedIndex = 0;
                if (txtSaleLocation != null) txtSaleLocation.Text = "";
                if (txtSaleItem != null) txtSaleItem.Text = "";
                if (txtSaleVehicleModel != null) txtSaleVehicleModel.Text = "";
            }
            catch { }
        }

        private static Guna.UI2.WinForms.Guna2TextBox MakeTxt()
        {
            return new Guna.UI2.WinForms.Guna2TextBox
            {
                AutoRoundedCorners = true,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                FillColor = Color.FromArgb(37, 41, 74),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Size = new Size(200, 36)
            };
        }

        private static void SetupVipTextAutoComplete(Guna.UI2.WinForms.Guna2TextBox tb, string[] items)
        {
            if (tb == null || items == null) return;
            var auto = new AutoCompleteStringCollection();
            foreach (string t in items) auto.Add(t);
            tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteCustomSource = auto;
        }

        private void ApplyDealerVipLayout()
        {
            // Form thora bara — Credit / Note cut na hon
            MinimumSize = new Size(980, 580);
            ClientSize = new Size(980, 580);

            const int colW = 220;
            const int gap = 18;
            int x0 = 16, x1 = x0 + colW + gap, x2 = x1 + colW + gap, x3 = x2 + colW + gap;
            int yLab1 = 72, yBox1 = 98;
            int yLab2 = 150, yBox2 = 176;
            int yLab3 = 228, yBox3 = 254;
            int yLab4 = 306, yBox4 = 332;

            StyleField(cbDealer, colW, 40);
            StyleField(txtDealerName, colW, 40);
            StyleField(txtdate, colW, 40);
            StyleField(txtvehicle, colW, 40);
            StyleField(cbSaleLocation, colW, 40);
            StyleField(txtSaleLocation, colW, 40);
            StyleField(cbSaleItem, colW, 40);
            StyleField(txtSaleItem, colW, 40);
            StyleField(cbSaleVehicleModel, colW, 40);
            StyleField(txtSaleVehicleModel, colW, 40);
            StyleField(txtAddDisel, colW, 40);
            StyleField(txtRate, colW, 40);
            StyleField(txtlitterRateAmount, colW, 40);
            StyleField(txtCredit, colW, 40);

            // Row1: Dealer | Date | In/Out (Daily Diesel jaisa tag)
            Place(label3, x0 + 8, yLab1);
            Place(cbDealer, x0, yBox1, colW, 40);
            Place(txtDealerName, x0, yBox1, colW, 40);
            Place(label6, x1 + 8, yLab1);
            Place(txtdate, x1, yBox1, colW, 40);
            Place(lblSaleInOut, x2 + 8, yLab1);
            Place(cbSaleInOut, x2, yBox1, 140, 40);
            if (cbSaleInOut != null) { cbSaleInOut.Visible = true; cbSaleInOut.BringToFront(); }
            if (lblSaleInOut != null) { lblSaleInOut.Visible = true; lblSaleInOut.BringToFront(); }

            // Row2: Vehicle | Location | Item | Vehicle Model
            Place(label5, x0 + 8, yLab2);
            Place(txtvehicle, x0, yBox2, colW, 40);
            Place(lblSaleLocation, x1 + 8, yLab2);
            Place(cbSaleLocation, x1, yBox2, colW, 40);
            Place(txtSaleLocation, x1, yBox2, colW, 40);
            Place(lblSaleItem, x2 + 8, yLab2);
            Place(cbSaleItem, x2, yBox2, colW, 40);
            Place(txtSaleItem, x2, yBox2, colW, 40);
            Place(lblSaleVehicleModel, x3 + 8, yLab2);
            Place(cbSaleVehicleModel, x3, yBox2, colW, 40);
            Place(txtSaleVehicleModel, x3, yBox2, colW, 40);

            // Row3: Litter | Rate | Amount | Credit
            Place(label7, x0 + 8, yLab3);
            Place(txtAddDisel, x0, yBox3, colW, 40);
            Place(label2, x1 + 8, yLab3);
            Place(txtRate, x1, yBox3, colW, 40);
            Place(lblAmount, x2 + 8, yLab3);
            Place(txtlitterRateAmount, x2, yBox3, colW, 40);
            Place(lblCredit, x3 + 8, yLab3);
            Place(txtCredit, x3, yBox3, colW, 40);

            // Row4: Note
            Place(label8, x0 + 8, yLab4);
            Place(txtNote, x0, yBox4, x3 + colW - x0, 72);
            if (txtNote != null)
            {
                txtNote.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
                txtNote.BringToFront();
            }

            if (cbDealer != null) { cbDealer.Enabled = true; cbDealer.Visible = !_dealerVipFastText; cbDealer.BringToFront(); }
            if (txtDealerName != null) { txtDealerName.Enabled = true; txtDealerName.Visible = _dealerVipFastText; if (_dealerVipFastText) txtDealerName.BringToFront(); }

            if (_dealerVipFastText)
            {
                if (cbSaleLocation != null) cbSaleLocation.Visible = false;
                if (cbSaleItem != null) cbSaleItem.Visible = false;
                if (cbSaleVehicleModel != null) cbSaleVehicleModel.Visible = false;
                if (txtSaleLocation != null) { txtSaleLocation.Visible = true; txtSaleLocation.BringToFront(); }
                if (txtSaleItem != null) { txtSaleItem.Visible = true; txtSaleItem.BringToFront(); }
                if (txtSaleVehicleModel != null) { txtSaleVehicleModel.Visible = true; txtSaleVehicleModel.BringToFront(); }
            }
            else
            {
                if (txtSaleLocation != null) txtSaleLocation.Visible = false;
                if (txtSaleItem != null) txtSaleItem.Visible = false;
                if (txtSaleVehicleModel != null) txtSaleVehicleModel.Visible = false;
                if (cbSaleLocation != null) { cbSaleLocation.Visible = true; cbSaleLocation.BringToFront(); }
                if (cbSaleItem != null) { cbSaleItem.Visible = true; cbSaleItem.BringToFront(); }
                if (cbSaleVehicleModel != null) { cbSaleVehicleModel.Visible = true; cbSaleVehicleModel.BringToFront(); }
            }

            // Credit hamesha dikhe
            if (lblCredit != null) { lblCredit.Visible = true; lblCredit.Text = "Credit"; lblCredit.BringToFront(); }
            if (txtCredit != null)
            {
                txtCredit.Visible = true;
                txtCredit.Enabled = true;
                txtCredit.ForeColor = Color.Red;
                txtCredit.BringToFront();
            }
            if (lblAmount != null) { lblAmount.Visible = true; lblAmount.BringToFront(); }
            if (txtlitterRateAmount != null) { txtlitterRateAmount.Visible = true; txtlitterRateAmount.BringToFront(); }

            // Hide minus litter if present (VIP compact)
            try
            {
                if (txtMinusDisel != null) { txtMinusDisel.Visible = false; txtMinusDisel.TabStop = false; }
                if (lblMinusDisel != null) lblMinusDisel.Visible = false;
            }
            catch { }
        }

        private static void StyleField(Control c, int w, int h)
        {
            if (c == null) return;
            c.Size = new Size(w, h);
            if (c is Guna.UI2.WinForms.Guna2TextBox tb)
                tb.Font = MoneyFont;
            else if (c is Guna.UI2.WinForms.Guna2ComboBox cb)
            {
                cb.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
                cb.ItemHeight = 34;
            }
            else if (c is Guna.UI2.WinForms.Guna2DateTimePicker dp)
                dp.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
        }

        private static void Place(Control c, int x, int y, int w = 0, int h = 0)
        {
            if (c == null) return;
            c.Location = new Point(x, y);
            if (w > 0 && h > 0) c.Size = new Size(w, h);
            c.Visible = c.Visible; // keep current visibility
        }

        private void ApplyDealerVipTabOrder()
        {
            void Stop(Control c) { if (c != null) c.TabStop = false; }
            void Set(Control c, int t) { if (c == null) return; c.TabIndex = t; c.TabStop = true; }

            Stop(label3); Stop(label6); Stop(label5); Stop(label7); Stop(label2);
            Stop(label8); Stop(lblSaleLocation); Stop(lblSaleItem); Stop(lblSaleVehicleModel);
            Stop(lblSaleInOut); Stop(lblCredit); Stop(lblAmount);
            Stop(cbSaleLocation); Stop(cbSaleItem); Stop(cbSaleVehicleModel);
            Stop(txtSaleLocation); Stop(txtSaleItem); Stop(txtSaleVehicleModel);
            Stop(txtAddDisel); Stop(txtRate);
            Stop(txtdate); Stop(cbSaleInOut);

            if (_dealerVipFastText)
            {
                Stop(cbDealer);
                Set(txtDealerName, 1);
            }
            else
            {
                Stop(txtDealerName);
                Set(cbDealer, 1);
            }

            // Vehicle → Amount → Credit → Note (Enter/Tab pe Amount)
            Set(txtvehicle, 4);
            Set(txtlitterRateAmount, 5);
            Set(txtCredit, 6);
            Set(txtNote, 7);
        }

        /// <summary>Vehicle Enter/Tab ke baad seedha Amount.</summary>
        private void FocusDealerVipAfterVehicle()
        {
            try
            {
                if (txtlitterRateAmount != null)
                {
                    txtlitterRateAmount.Focus();
                    txtlitterRateAmount.Select();
                    return;
                }
                if (txtCredit != null) txtCredit.Focus();
            }
            catch { }
        }

        private void EnterDealerVipFastText()
        {
            _dealerVipFastText = true;
            // Daily Diesel Ctrl+1 jaisa — Name / Location / Item / Model empty
            try
            {
                if (txtDealerName != null) txtDealerName.Text = "";
                if (txtvehicle != null) txtvehicle.Text = "";
                if (txtAddDisel != null) txtAddDisel.Text = "";
                if (txtRate != null) txtRate.Text = "";
                if (txtlitterRateAmount != null) txtlitterRateAmount.Text = "";
                if (txtCredit != null) txtCredit.Text = "";
                if (txtNote != null) txtNote.Text = "";
                _amountManual = false;
                ClearVipLocationItemModel();
            }
            catch { }

            if (cbDealer != null) cbDealer.Visible = false;
            if (txtDealerName != null) { txtDealerName.Visible = true; txtDealerName.BringToFront(); }
            if (cbSaleLocation != null) cbSaleLocation.Visible = false;
            if (cbSaleItem != null) cbSaleItem.Visible = false;
            if (cbSaleVehicleModel != null) cbSaleVehicleModel.Visible = false;
            if (txtSaleLocation != null) { txtSaleLocation.Visible = true; txtSaleLocation.BringToFront(); }
            if (txtSaleItem != null) { txtSaleItem.Visible = true; txtSaleItem.BringToFront(); }
            if (txtSaleVehicleModel != null) { txtSaleVehicleModel.Visible = true; txtSaleVehicleModel.BringToFront(); }
            ApplyDealerVipTabOrder();
            try { txtvehicle.Focus(); } catch { }
        }

        private void ExitDealerVipFastText()
        {
            _dealerVipFastText = false;
            SyncTextToVipCombo(txtSaleLocation, cbSaleLocation);
            SyncTextToVipCombo(txtSaleItem, cbSaleItem);
            SyncTextToVipCombo(txtSaleVehicleModel, cbSaleVehicleModel);
            if (txtSaleLocation != null) txtSaleLocation.Visible = false;
            if (txtSaleItem != null) txtSaleItem.Visible = false;
            if (txtSaleVehicleModel != null) txtSaleVehicleModel.Visible = false;
            if (cbSaleLocation != null) { cbSaleLocation.Visible = true; cbSaleLocation.BringToFront(); }
            if (cbSaleItem != null) { cbSaleItem.Visible = true; cbSaleItem.BringToFront(); }
            if (cbSaleVehicleModel != null) { cbSaleVehicleModel.Visible = true; cbSaleVehicleModel.BringToFront(); }
            if (txtDealerName != null) txtDealerName.Visible = false;
            if (cbDealer != null) { cbDealer.Visible = true; cbDealer.BringToFront(); }
            ApplyDealerVipTabOrder();
            try { txtvehicle.Focus(); } catch { }
        }

        private static void SyncTextToVipCombo(Guna.UI2.WinForms.Guna2TextBox tb, Guna.UI2.WinForms.Guna2ComboBox cb)
        {
            if (tb == null || cb == null) return;
            string v = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(v)) return;
            int idx = cb.FindStringExact(v);
            if (idx >= 0) { cb.SelectedIndex = idx; return; }
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals(cb.Items[i]?.ToString()?.Trim(), v, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private void RefreshDealerVehicleAutoSuggest()
        {
            try
            {
                if (txtvehicle == null) return;
                var auto = new AutoCompleteStringCollection();
                // DealerAmount + Roznamcha vehicles
                DataTable dt = MainClass.ExecuteSelectQuery(@"
SELECT DISTINCT TRIM(v) AS v FROM (
  SELECT Vehicle AS v FROM AddStock WHERE TRIM(IFNULL(Vehicle,'')) <> ''
  UNION ALL
  SELECT Vehicle AS v FROM StockDiesel WHERE TRIM(IFNULL(Vehicle,'')) <> ''
)
ORDER BY v COLLATE NOCASE", null);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string v = (row["v"]?.ToString() ?? "").Trim();
                        if (v.Length > 0 && !auto.Contains(v)) auto.Add(v);
                    }
                }
                txtvehicle.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                txtvehicle.AutoCompleteSource = AutoCompleteSource.CustomSource;
                txtvehicle.AutoCompleteCustomSource = auto;
            }
            catch { }
        }

        /// <summary>
        /// Roznamcha (StockDiesel) mein isi vehicle ki last Location/Item/Vehicle Model → DealerAmount VIP.
        /// </summary>
        private void SuggestVipFromRoznamchaVehicle()
        {
            try
            {
                if (!_dealerVipReady) return;
                string vehicle = (txtvehicle != null ? txtvehicle.Text : "") ?? "";
                vehicle = vehicle.Trim();
                if (string.IsNullOrEmpty(vehicle)) return;

                DataTable dt = MainClass.ExecuteSelectQuery(@"
SELECT StockType, StockType2, StockType3
FROM StockDiesel
WHERE TRIM(IFNULL(Vehicle,'')) = @veh
  AND (
    TRIM(IFNULL(StockType,'')) <> ''
    OR TRIM(IFNULL(StockType2,'')) <> ''
    OR TRIM(IFNULL(StockType3,'')) <> ''
  )
ORDER BY date(IFNULL(Date,'1900-01-01')) DESC, SID DESC
LIMIT 1",
                    new System.Collections.Hashtable { { "@veh", vehicle } });

                if (dt == null || dt.Rows.Count == 0) return;

                DataRow row = dt.Rows[0];
                string loc = row["StockType"] != DBNull.Value ? (row["StockType"]?.ToString() ?? "").Trim() : "";
                string item = row["StockType2"] != DBNull.Value ? (row["StockType2"]?.ToString() ?? "").Trim() : "";
                string model = row["StockType3"] != DBNull.Value ? (row["StockType3"]?.ToString() ?? "").Trim() : "";

                ApplyVipSuggestValue(loc, cbSaleLocation, txtSaleLocation);
                ApplyVipSuggestValue(item, cbSaleItem, txtSaleItem);
                ApplyVipSuggestValue(model, cbSaleVehicleModel, txtSaleVehicleModel);
            }
            catch { }
        }

        private static void ApplyVipSuggestValue(string value, Guna.UI2.WinForms.Guna2ComboBox cb, Guna.UI2.WinForms.Guna2TextBox tb)
        {
            string v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (tb != null) tb.Text = v;
            if (cb == null) return;
            int idx = cb.FindStringExact(v);
            if (idx >= 0) { cb.SelectedIndex = idx; return; }
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals(cb.Items[i]?.ToString()?.Trim(), v, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private void TxtAmount_TextChanged(object sender, EventArgs e)
        {
            if (_fmtMoney) return;
            _amountManual = true;
        }

        private string GetVipSaleLocation()
        {
            if (_dealerVipFastText && txtSaleLocation != null)
                return (txtSaleLocation.Text ?? "").Trim();
            return cbSaleLocation?.SelectedItem?.ToString()?.Trim() ?? "";
        }

        private string GetVipSaleItem()
        {
            if (_dealerVipFastText && txtSaleItem != null)
                return (txtSaleItem.Text ?? "").Trim();
            return cbSaleItem?.SelectedItem?.ToString()?.Trim() ?? "";
        }

        private string GetVipSaleVehicleModel()
        {
            if (_dealerVipFastText && txtSaleVehicleModel != null)
                return (txtSaleVehicleModel.Text ?? "").Trim();
            return cbSaleVehicleModel?.SelectedItem?.ToString()?.Trim() ?? "";
        }

        private string GetVipSaleInOut()
        {
            if (cbSaleInOut?.SelectedItem != null)
            {
                string v = (cbSaleInOut.SelectedItem.ToString() ?? "").Trim();
                if (string.Equals(v, "Out", StringComparison.OrdinalIgnoreCase))
                    return "Out";
            }
            return "In";
        }

        private decimal GetVipCredit()
        {
            if (txtCredit == null) return 0m;
            decimal.TryParse((txtCredit.Text ?? "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal c);
            return c;
        }

        private decimal GetVipAmountDirect()
        {
            if (txtlitterRateAmount == null) return 0m;
            decimal.TryParse((txtlitterRateAmount.Text ?? "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal a);
            return a;
        }

        private void LoadVipFieldsFromRow(DataRow row)
        {
            try
            {
                if (row.Table.Columns.Contains("Credit") && txtCredit != null)
                    txtCredit.Text = Convert.ToDecimal(row["Credit"] == DBNull.Value ? 0 : row["Credit"]).ToString("N2");
                if (row.Table.Columns.Contains("Amount") && txtlitterRateAmount != null)
                {
                    decimal amt = Convert.ToDecimal(row["Amount"] == DBNull.Value ? 0 : row["Amount"]);
                    if (amt <= 0 && row.Table.Columns.Contains("Rate") && row.Table.Columns.Contains("AddDisel"))
                    {
                        decimal r = Convert.ToDecimal(row["Rate"] == DBNull.Value ? 0 : row["Rate"]);
                        decimal l = Convert.ToDecimal(row["AddDisel"] == DBNull.Value ? 0 : row["AddDisel"]);
                        amt = r * l;
                    }
                    txtlitterRateAmount.Text = amt.ToString("N2");
                    _amountManual = true;
                }
                ClearVipLocationItemModel();
                SetVipComboFromRow(row, "SaleLocation", cbSaleLocation, txtSaleLocation);
                SetVipComboFromRow(row, "SaleItem", cbSaleItem, txtSaleItem);
                SetVipComboFromRow(row, "SaleVehicleModel", cbSaleVehicleModel, txtSaleVehicleModel);
                if (row.Table.Columns.Contains("SaleInOut") && cbSaleInOut != null)
                {
                    string io = (row["SaleInOut"]?.ToString() ?? "").Trim();
                    if (string.Equals(io, "Out", StringComparison.OrdinalIgnoreCase))
                        cbSaleInOut.SelectedIndex = cbSaleInOut.FindStringExact("Out");
                    else
                        cbSaleInOut.SelectedIndex = cbSaleInOut.FindStringExact("In");
                    if (cbSaleInOut.SelectedIndex < 0 && cbSaleInOut.Items.Count > 0)
                        cbSaleInOut.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private static void SetVipComboFromRow(DataRow row, string col, Guna.UI2.WinForms.Guna2ComboBox cb, Guna.UI2.WinForms.Guna2TextBox tb)
        {
            if (row == null || !row.Table.Columns.Contains(col)) return;
            string v = (row[col]?.ToString() ?? "").Trim();
            if (tb != null) tb.Text = v;
            if (cb == null || string.IsNullOrEmpty(v)) return;
            int idx = cb.FindStringExact(v);
            if (idx >= 0) cb.SelectedIndex = idx;
            else
            {
                for (int i = 0; i < cb.Items.Count; i++)
                {
                    if (string.Equals(cb.Items[i]?.ToString()?.Trim(), v, StringComparison.OrdinalIgnoreCase))
                    {
                        cb.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        private void ResetVipFieldsAfterSave()
        {
            if (txtCredit != null) txtCredit.Text = "";
            if (txtlitterRateAmount != null) txtlitterRateAmount.Text = "";
            _amountManual = false;
            // Location/Item/Model empty — In/Out user ne jo rakha wahi (Daily Diesel jaisa)
            ClearVipLocationItemModel();
            RefreshDealerVehicleAutoSuggest();
        }
    }
}
