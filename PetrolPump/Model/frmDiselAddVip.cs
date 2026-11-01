using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    /// <summary>
    /// Daily Diesel Sales VIP logic — Location/Item/Vehicle Model Designer controls use.
    /// Position/size aap Designer se set karo. Ctrl+1 fast empty text; Ctrl+2 combo.
    /// Sync untouched.
    /// </summary>
    public partial class frmDiselAdd
    {
        private static readonly string[] VipLocations = { "Bazar", "Band Murad" };
        private static readonly string[] VipItems = { "Diesel", "Chaliyan" };
        private static readonly string[] VipVehicleModels = { "Single", "Das Wheeler", "Cruiser" };
        private static readonly string[] VipInOut = { "In", "Out" };

        private bool _dailyVipReady;
        private bool _dailyVipFastText;
        private Label lblSaleInOut;
        private Guna.UI2.WinForms.Guna2ComboBox cbSaleInOut;

        private void ApplyDailyVipMode()
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

            if (_dailyVipReady) return;
            _dailyVipReady = true;

            try
            {
                // Advance / Receipt / Balance hide (Designer se wapas dikha sakte ho)
                if (txtadvance != null)
                {
                    txtadvance.Visible = false;
                    txtadvance.Text = "0";
                    txtadvance.TabStop = false;
                }
                if (label11 != null) label11.Visible = false;
                if (txtrecipt != null)
                {
                    txtrecipt.Visible = false;
                    txtrecipt.Text = "";
                    txtrecipt.TabStop = false;
                }
                if (label5 != null) label5.Visible = false;
                if (txtbalance != null)
                {
                    txtbalance.Visible = false;
                    txtbalance.TabStop = false;
                }
                if (label8 != null) label8.Visible = false;

                EnsureVipComboItemsAllowEmpty(cbSaleLocation, VipLocations);
                EnsureVipComboItemsAllowEmpty(cbSaleItem, VipItems);
                EnsureVipComboItemsAllowEmpty(cbSaleVehicleModel, VipVehicleModels);
                EnsureSaleInOutCombo();
                SetupVipTextAutoComplete(txtSaleLocation, VipLocations);
                SetupVipTextAutoComplete(txtSaleItem, VipItems);
                SetupVipTextAutoComplete(txtSaleVehicleModel, VipVehicleModels);

                // Designer defaults: combos visible, fast-text hidden
                if (txtSaleLocation != null) txtSaleLocation.Visible = false;
                if (txtSaleItem != null) txtSaleItem.Visible = false;
                if (txtSaleVehicleModel != null) txtSaleVehicleModel.Visible = false;
                if (cbSaleLocation != null) cbSaleLocation.Visible = true;
                if (cbSaleItem != null) cbSaleItem.Visible = true;
                if (cbSaleVehicleModel != null) cbSaleVehicleModel.Visible = true;

                ApplyDailyVipCleanLayout();
                ApplyDailyVipTabOrder();
                RefreshVehicleAutoSuggest();
                // Default: Location/Item/Model empty; In selected
                ClearVipLocationItemModel();
                if (cbSaleInOut != null && cbSaleInOut.Items.Count > 0)
                    cbSaleInOut.SelectedIndex = 0;
            }
            catch { }
        }

        private void EnsureSaleInOutCombo()
        {
            if (cbSaleInOut != null) return;
            lblSaleInOut = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "In/Out"
            };
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

        /// <summary>
        /// Vehicle No auto-suggest — purane numbers (ttt) type karte hi suggest.
        /// </summary>
        private void RefreshVehicleAutoSuggest()
        {
            try
            {
                if (txtVehicle == null) return;

                var auto = new AutoCompleteStringCollection();
                // Daily Diesel + Roznamcha vehicles
                DataTable dt = MainClass.ExecuteSelectQuery(@"
SELECT DISTINCT TRIM(v) AS v FROM (
  SELECT vehicle AS v FROM PetrolAdd WHERE TRIM(IFNULL(vehicle,'')) <> ''
  UNION ALL
  SELECT Vehicle AS v FROM StockDiesel WHERE TRIM(IFNULL(Vehicle,'')) <> ''
)
ORDER BY v COLLATE NOCASE", null);
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string v = (row["v"]?.ToString() ?? "").Trim();
                        if (v.Length > 0 && !auto.Contains(v))
                            auto.Add(v);
                    }
                }

                txtVehicle.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                txtVehicle.AutoCompleteSource = AutoCompleteSource.CustomSource;
                txtVehicle.AutoCompleteCustomSource = auto;
            }
            catch { }
        }

        /// <summary>
        /// Roznamcha (StockDiesel) vehicle → Location / Item / Vehicle Model (Dealer Amount jaisa).
        /// </summary>
        private void SuggestVipFromRoznamchaVehicle()
        {
            try
            {
                if (!_dailyVipReady) return;
                string vehicle = (txtVehicle != null ? txtVehicle.Text : "") ?? "";
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

                if (dt == null || dt.Rows.Count == 0)
                {
                    // Roznamcha na mile to Daily Diesel purani Sale* fields
                    dt = MainClass.ExecuteSelectQuery(@"
SELECT SaleLocation AS StockType, SaleItem AS StockType2, SaleVehicleModel AS StockType3
FROM PetrolAdd
WHERE TRIM(IFNULL(vehicle,'')) = @veh
  AND (
    TRIM(IFNULL(SaleLocation,'')) <> ''
    OR TRIM(IFNULL(SaleItem,'')) <> ''
    OR TRIM(IFNULL(SaleVehicleModel,'')) <> ''
  )
ORDER BY date(IFNULL(Date,'1900-01-01')) DESC, pid DESC
LIMIT 1",
                        new System.Collections.Hashtable { { "@veh", vehicle } });
                }

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

        /// <summary>
        /// Tab: Vehicle → Amount → Credit → Note (baaki click se; layout change nahi).
        /// </summary>
        private void ApplyDailyVipTabOrder()
        {
            void Stop(Control c)
            {
                if (c != null) c.TabStop = false;
            }
            void Set(Control c, int tab)
            {
                if (c == null) return;
                c.TabIndex = tab;
                c.TabStop = true;
            }

            // Skip in Tab chain (focus start Vehicle pe hai)
            Stop(cbName);
            Stop(txtName);
            Stop(txtdate);
            Stop(txtrecipt);
            Stop(txtadvance);
            Stop(txtbalance);
            Stop(label3); Stop(label6); Stop(label2); Stop(label5);
            Stop(label11); Stop(label8); Stop(label7); Stop(label4);
            Stop(label10); Stop(label9); Stop(label12);
            Stop(lblSaleLocation); Stop(lblSaleItem); Stop(lblSaleVehicleModel);
            Stop(cbSaleLocation); Stop(cbSaleItem); Stop(cbSaleVehicleModel);
            Stop(txtSaleLocation); Stop(txtSaleItem); Stop(txtSaleVehicleModel);
            Stop(txtlitter); Stop(txtrate);

            Set(txtVehicle, 10);
            Set(txtamount, 11);
            Set(txtcredit, 12);
            Set(txtnote, 13);
        }

        private void FocusDailyVipAfterVehicle()
        {
            try
            {
                // Vehicle Enter/Tab → seedha Amount
                if (txtamount != null)
                {
                    txtamount.Focus();
                    txtamount.Select();
                    return;
                }
                if (txtcredit != null) txtcredit.Focus();
            }
            catch { }
        }

        /// <summary>
        /// Overlap-free grid: Row1 Name/Date | Row2 Vehicle/Location/Item/VM |
        /// Row3 Litter/Rate/Amount/Credit | Row4 Note. Sync untouched.
        /// </summary>
        private void ApplyDailyVipCleanLayout()
        {
            ClientSize = new System.Drawing.Size(1000, 520);

            PlacePair(label3, cbName, 20, 78, 12, 104, 200, 36);
            if (txtName != null)
            {
                txtName.Location = new System.Drawing.Point(12, 104);
                txtName.Size = new System.Drawing.Size(200, 36);
            }
            PlacePair(label6, txtdate, 238, 78, 230, 104, 360, 36);
            PlacePair(lblSaleInOut, cbSaleInOut, 610, 78, 602, 104, 120, 36);

            PlacePair(label2, txtVehicle, 20, 155, 12, 181, 200, 36);
            PlacePair(lblSaleLocation, cbSaleLocation, 238, 155, 230, 181, 200, 36);
            PlacePair(lblSaleItem, cbSaleItem, 456, 155, 448, 181, 200, 36);
            PlacePair(lblSaleVehicleModel, cbSaleVehicleModel, 674, 155, 666, 181, 200, 36);
            if (txtSaleLocation != null) { txtSaleLocation.Location = new System.Drawing.Point(230, 181); txtSaleLocation.Size = new System.Drawing.Size(200, 36); }
            if (txtSaleItem != null) { txtSaleItem.Location = new System.Drawing.Point(448, 181); txtSaleItem.Size = new System.Drawing.Size(200, 36); }
            if (txtSaleVehicleModel != null) { txtSaleVehicleModel.Location = new System.Drawing.Point(666, 181); txtSaleVehicleModel.Size = new System.Drawing.Size(200, 36); }

            PlacePair(label7, txtlitter, 20, 232, 12, 258, 200, 36);
            PlacePair(label4, txtrate, 238, 232, 230, 258, 200, 36);
            PlacePair(label10, txtamount, 456, 232, 448, 258, 200, 36);
            PlacePair(label9, txtcredit, 674, 232, 666, 258, 200, 36);

            if (label12 != null) label12.Location = new System.Drawing.Point(20, 309);
            if (txtnote != null)
            {
                txtnote.Location = new System.Drawing.Point(12, 335);
                txtnote.Size = new System.Drawing.Size(854, 64);
            }
        }

        private static void PlacePair(
            Control label, Control field,
            int labelX, int labelY, int fieldX, int fieldY, int fieldW, int fieldH)
        {
            if (label != null) label.Location = new System.Drawing.Point(labelX, labelY);
            if (field == null) return;
            field.Location = new System.Drawing.Point(fieldX, fieldY);
            field.Size = new System.Drawing.Size(fieldW, fieldH);
        }

        /// <summary>Pehle empty option — Amount/Credit-only pe Location/Item/Model blank reh sake.</summary>
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
                cb.SelectedIndex = 0; // blank
        }

        private void ClearVipLocationItemModel()
        {
            try
            {
                if (cbSaleLocation != null && cbSaleLocation.Items.Count > 0)
                    cbSaleLocation.SelectedIndex = 0; // blank
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

        private static void SetupVipTextAutoComplete(Guna.UI2.WinForms.Guna2TextBox tb, string[] items)
        {
            if (tb == null || items == null) return;
            var auto = new AutoCompleteStringCollection();
            foreach (string t in items)
                auto.Add(t);
            tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteCustomSource = auto;
        }

        /// <summary>Ctrl+1 — sab empty + Location/Item/Vehicle Model textboxes.</summary>
        private void EnterDailyVipFastTextMode()
        {
            try
            {
                ApplyDailyVipMode();
                _dailyVipFastText = true;

                if (txtVehicle != null) txtVehicle.Text = "";
                if (txtlitter != null) txtlitter.Text = "";
                if (txtrate != null) txtrate.Text = "";
                if (txtamount != null) txtamount.Text = "";
                if (txtcredit != null) txtcredit.Text = "";
                if (txtbalance != null) txtbalance.Text = "";
                if (txtnote != null) txtnote.Text = "";
                if (txtadvance != null) txtadvance.Text = "0";
                if (txtrecipt != null) txtrecipt.Text = "";
                if (txtName != null) txtName.Text = "";

                if (txtSaleLocation != null) txtSaleLocation.Text = "";
                if (txtSaleItem != null) txtSaleItem.Text = "";
                if (txtSaleVehicleModel != null) txtSaleVehicleModel.Text = "";

                if (cbName != null) cbName.Visible = false;
                if (txtName != null)
                {
                    txtName.Visible = true;
                    txtName.BringToFront();
                }

                if (cbSaleLocation != null) cbSaleLocation.Visible = false;
                if (cbSaleItem != null) cbSaleItem.Visible = false;
                if (cbSaleVehicleModel != null) cbSaleVehicleModel.Visible = false;

                if (txtSaleLocation != null) { txtSaleLocation.Visible = true; txtSaleLocation.BringToFront(); }
                if (txtSaleItem != null) { txtSaleItem.Visible = true; txtSaleItem.BringToFront(); }
                if (txtSaleVehicleModel != null) { txtSaleVehicleModel.Visible = true; txtSaleVehicleModel.BringToFront(); }

                ApplyDailyVipTabOrder();
                try { txtVehicle.Focus(); txtVehicle.Select(); } catch { }
            }
            catch { }
        }

        /// <summary>Ctrl+2 — combo mode.</summary>
        private void ExitDailyVipFastTextMode()
        {
            try
            {
                _dailyVipFastText = false;

                SyncTextToVipCombo(txtSaleLocation, cbSaleLocation);
                SyncTextToVipCombo(txtSaleItem, cbSaleItem);
                SyncTextToVipCombo(txtSaleVehicleModel, cbSaleVehicleModel);

                if (txtSaleLocation != null) txtSaleLocation.Visible = false;
                if (txtSaleItem != null) txtSaleItem.Visible = false;
                if (txtSaleVehicleModel != null) txtSaleVehicleModel.Visible = false;

                if (cbSaleLocation != null) { cbSaleLocation.Visible = true; cbSaleLocation.BringToFront(); }
                if (cbSaleItem != null) { cbSaleItem.Visible = true; cbSaleItem.BringToFront(); }
                if (cbSaleVehicleModel != null) { cbSaleVehicleModel.Visible = true; cbSaleVehicleModel.BringToFront(); }

                if (txtName != null) txtName.Visible = false;
                if (cbName != null)
                {
                    cbName.Visible = true;
                    cbName.BringToFront();
                    string typed = (txtName.Text ?? "").Trim();
                    if (!string.IsNullOrEmpty(typed))
                    {
                        try
                        {
                            int idx = cbName.FindStringExact(typed);
                            if (idx >= 0) cbName.SelectedIndex = idx;
                        }
                        catch { }
                    }
                }
                ApplyDailyVipTabOrder();
                try { txtVehicle.Focus(); txtVehicle.Select(); } catch { }
            }
            catch { }
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

        private string GetVipSaleLocation()
        {
            if (txtSaleLocation != null && txtSaleLocation.Visible)
                return NormalizeVip(txtSaleLocation.Text, VipLocations);
            if (cbSaleLocation?.SelectedItem != null)
                return (cbSaleLocation.SelectedItem.ToString() ?? "").Trim();
            return "";
        }

        private string GetVipSaleItem()
        {
            if (txtSaleItem != null && txtSaleItem.Visible)
                return NormalizeVip(txtSaleItem.Text, VipItems);
            if (cbSaleItem?.SelectedItem != null)
                return (cbSaleItem.SelectedItem.ToString() ?? "").Trim();
            return "";
        }

        private string GetVipSaleVehicleModel()
        {
            if (txtSaleVehicleModel != null && txtSaleVehicleModel.Visible)
                return NormalizeVip(txtSaleVehicleModel.Text, VipVehicleModels);
            if (cbSaleVehicleModel?.SelectedItem != null)
                return (cbSaleVehicleModel.SelectedItem.ToString() ?? "").Trim();
            return "";
        }

        /// <summary>In/Out set — Credit→Out, Amount click→In; baad mein user change bhi kar sakta hai.</summary>
        private void SetVipSaleInOut(string inOrOut)
        {
            if (_loadingData || cbSaleInOut == null) return;
            string want = string.Equals(inOrOut, "Out", StringComparison.OrdinalIgnoreCase) ? "Out" : "In";
            try
            {
                int idx = cbSaleInOut.FindStringExact(want);
                if (idx < 0)
                {
                    for (int i = 0; i < cbSaleInOut.Items.Count; i++)
                    {
                        if (string.Equals(cbSaleInOut.Items[i]?.ToString()?.Trim(), want,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                            break;
                        }
                    }
                }
                if (idx >= 0 && cbSaleInOut.SelectedIndex != idx)
                    cbSaleInOut.SelectedIndex = idx;
            }
            catch { }
        }

        private void AutoOutWhenCreditEntered(decimal creditValue)
        {
            if (creditValue > 0)
                SetVipSaleInOut("Out");
        }

        private void AutoInWhenAmountFocus()
        {
            // Credit pehle se bhara ho to Out rehne do
            try
            {
                decimal credit = 0;
                decimal.TryParse((txtcredit?.Text ?? "").Replace(",", "").Trim(), out credit);
                if (credit > 0) return;
            }
            catch { }
            SetVipSaleInOut("In");
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

        private static string NormalizeVip(string value, string[] allowed)
        {
            string v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v) || allowed == null) return v;
            foreach (string a in allowed)
            {
                if (string.Equals(a, v, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return v;
        }

        private void LoadVipFieldsFromRow(DataRow row)
        {
            try
            {
                if (row == null) return;
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

        private static void SetVipComboFromRow(
            DataRow row, string col,
            Guna.UI2.WinForms.Guna2ComboBox cb,
            Guna.UI2.WinForms.Guna2TextBox tb)
        {
            if (!row.Table.Columns.Contains(col) || row[col] == DBNull.Value) return;
            string v = row[col]?.ToString()?.Trim() ?? "";
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
                    break;
                }
            }
        }

        private void ResetVipFieldsAfterSave()
        {
            try
            {
                // Location / Item / Model empty — Amount/Credit-only entries clean
                ClearVipLocationItemModel();
                // In/Out user ne jo rakha wahi (fix nahi)
            }
            catch { }
        }
    }
}
