using System;
using System.Collections;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    /// <summary>
    /// Stock menu (frmStockDieselAdd): Vehicle / Type / Amount / Date / Note + inline new party.
    /// Does not change DealerAmount (frmStockAdd). Form title "Stock Details" unchanged.
    /// </summary>
    public partial class frmStockDieselAdd
    {
        // Pair 1 = Type, Pair 2 = Type 2, Pair 3 = Type 3
        private static readonly string[] StockPartyTypesPair1 =
        {
            "Bazar", "Band Murad"
        };
        private static readonly string[] StockPartyTypesPair2 =
        {
            "Diesel", "Chaliyan"
        };
        private static readonly string[] StockPartyTypesPair3 =
        {
            "Single", "Das Wheeler", "Cruiser"
        };

        private Guna.UI2.WinForms.Guna2ComboBox cbStockType;
        private Guna.UI2.WinForms.Guna2ComboBox cbStockType2;
        private Guna.UI2.WinForms.Guna2ComboBox cbStockType3;
        private Guna.UI2.WinForms.Guna2TextBox txtStockType;
        private Guna.UI2.WinForms.Guna2TextBox txtStockType2;
        private Guna.UI2.WinForms.Guna2TextBox txtStockType3;
        private Label lblStockType;
        private Label lblStockType2;
        private Label lblStockType3;
        private bool _partyMode;
        private bool _partyFastTextMode;

        private void ApplyPartyStockMode()
        {
            try
            {
                // Type 3 poora dikhe — form wider
                if (ClientSize.Width < 1120)
                    ClientSize = new Size(1120, Math.Max(ClientSize.Height, 360));
            }
            catch { }

            if (_partyMode) return;
            _partyMode = true;

            try
            {
                // Hide litter / credit / debit extras — keep labels/titles that already exist
                if (txtlitter != null) txtlitter.Visible = false;
                if (label7 != null) { label7.Visible = false; label7.Text = ""; }
                if (txtMinusLitter != null) txtMinusLitter.Visible = false;
                if (lblMinusLitter != null) { lblMinusLitter.Visible = false; lblMinusLitter.Text = ""; }
                if (txtcredit != null) txtcredit.Visible = false;
                if (label4 != null) label4.Visible = false;
                if (txtdebit != null) txtdebit.Visible = false;
                if (label9 != null) label9.Visible = false;

                // Name (not AddDealer): keep designer positions for Name / Vehicle
                if (label3 != null)
                    label3.Text = "Name";

                // Amount + Type / Type2 / Type3
                if (label2 != null)
                {
                    label2.Text = "Amount";
                    label2.Location = new Point(455, 78);
                    label2.Visible = true;
                }
                if (txtRate != null)
                {
                    txtRate.Location = new Point(445, 104);
                    txtRate.Size = new Size(120, 36);
                    txtRate.TabIndex = 5;
                    txtRate.KeyDown -= PartyAmount_KeyDown;
                    txtRate.KeyDown += PartyAmount_KeyDown;
                }

                EnsureStockTypeCombo();
                EnsurePartyTypeTextBoxes();
                LayoutPartyTypeControls();
                SetupVehicleAutoCompleteParty();
                ApplyPartyFocusOrder();

                txtDealerName.Visible = false;
                if (cbDealer != null) cbDealer.Visible = true;
            }
            catch { }
        }

        private void LayoutPartyTypeControls()
        {
            // Amount(445) → Type(575) → Type2(720) → Type3(865) — form 1120
            if (lblStockType != null) lblStockType.Location = new Point(582, 78);
            if (lblStockType2 != null) lblStockType2.Location = new Point(727, 78);
            if (lblStockType3 != null) lblStockType3.Location = new Point(872, 78);

            var typeLoc = new Point(575, 104);
            var type2Loc = new Point(720, 104);
            var type3Loc = new Point(865, 104);
            var typeSize = new Size(135, 36);

            if (cbStockType != null) { cbStockType.Location = typeLoc; cbStockType.Size = typeSize; }
            if (cbStockType2 != null) { cbStockType2.Location = type2Loc; cbStockType2.Size = typeSize; }
            if (cbStockType3 != null) { cbStockType3.Location = type3Loc; cbStockType3.Size = typeSize; }
            if (txtStockType != null) { txtStockType.Location = typeLoc; txtStockType.Size = typeSize; }
            if (txtStockType2 != null) { txtStockType2.Location = type2Loc; txtStockType2.Size = typeSize; }
            if (txtStockType3 != null) { txtStockType3.Location = type3Loc; txtStockType3.Size = typeSize; }
        }

        private void EnsureStockTypeCombo()
        {
            if (cbStockType == null)
            {
                lblStockType = new Label
                {
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(575, 78),
                    Name = "lblStockType",
                    Text = "Location"
                };

                cbStockType = MakePartyTypeCombo("cbStockType", new Point(568, 104), 2, StockPartyTypesPair1);
                cbStockType.KeyDown += PartyType_KeyDown;

                Controls.Add(lblStockType);
                Controls.Add(cbStockType);
                lblStockType.BringToFront();
                cbStockType.BringToFront();
            }

            if (cbStockType2 == null)
            {
                lblStockType2 = new Label
                {
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(727, 78),
                    Name = "lblStockType2",
                    Text = "Item"
                };

                cbStockType2 = MakePartyTypeCombo("cbStockType2", new Point(720, 104), 3, StockPartyTypesPair2);
                cbStockType2.KeyDown += PartyType2_KeyDown;

                Controls.Add(lblStockType2);
                Controls.Add(cbStockType2);
                lblStockType2.BringToFront();
                cbStockType2.BringToFront();
            }

            if (cbStockType3 == null)
            {
                lblStockType3 = new Label
                {
                    AutoSize = true,
                    Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(872, 78),
                    Name = "lblStockType3",
                    Text = "Vehicle Model"
                };

                cbStockType3 = MakePartyTypeCombo("cbStockType3", new Point(865, 104), 4, StockPartyTypesPair3);
                cbStockType3.KeyDown += PartyType3_KeyDown;

                Controls.Add(lblStockType3);
                Controls.Add(cbStockType3);
                lblStockType3.BringToFront();
                cbStockType3.BringToFront();
            }
        }

        private void EnsurePartyTypeTextBoxes()
        {
            if (txtStockType == null)
            {
                txtStockType = MakePartyTypeTextBox("txtStockType", new Point(575, 104), 2, StockPartyTypesPair1);
                txtStockType.KeyDown += PartyType_KeyDown;
                Controls.Add(txtStockType);
                txtStockType.Visible = false;
            }
            if (txtStockType2 == null)
            {
                txtStockType2 = MakePartyTypeTextBox("txtStockType2", new Point(720, 104), 3, StockPartyTypesPair2);
                txtStockType2.KeyDown += PartyType2_KeyDown;
                Controls.Add(txtStockType2);
                txtStockType2.Visible = false;
            }
            if (txtStockType3 == null)
            {
                txtStockType3 = MakePartyTypeTextBox("txtStockType3", new Point(865, 104), 4, StockPartyTypesPair3);
                txtStockType3.KeyDown += PartyType3_KeyDown;
                Controls.Add(txtStockType3);
                txtStockType3.Visible = false;
            }
        }

        private Guna.UI2.WinForms.Guna2TextBox MakePartyTypeTextBox(
            string name, Point location, int tabIndex, string[] items)
        {
            var tb = new Guna.UI2.WinForms.Guna2TextBox
            {
                AutoRoundedCorners = true,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                Cursor = Cursors.IBeam,
                FillColor = Color.FromArgb(32, 36, 61),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = location,
                Name = name,
                Size = new Size(135, 36),
                TabIndex = tabIndex,
                PlaceholderText = ""
            };
            var auto = new AutoCompleteStringCollection();
            if (items != null)
            {
                foreach (string t in items)
                    auto.Add(t);
            }
            tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteCustomSource = auto;
            return tb;
        }

        /// <summary>Ctrl+1 — Name + Location/Item/Vehicle Model empty textbox (fast Tab entry).</summary>
        private void EnterPartyFastTextMode()
        {
            try
            {
                EnsureStockTypeCombo();
                EnsurePartyTypeTextBoxes();
                LayoutPartyTypeControls();
                _partyFastTextMode = true;

                // Combos hide — text empty (user type kare)
                if (txtDealerName != null)
                {
                    txtDealerName.Text = "";
                    txtDealerName.Visible = true;
                    txtDealerName.TabStop = true;
                }
                if (cbDealer != null) cbDealer.Visible = false;

                if (cbStockType != null) cbStockType.Visible = false;
                if (cbStockType2 != null) cbStockType2.Visible = false;
                if (cbStockType3 != null) cbStockType3.Visible = false;

                if (txtStockType != null)
                {
                    txtStockType.Text = "";
                    txtStockType.Visible = true;
                    txtStockType.BringToFront();
                }
                if (txtStockType2 != null)
                {
                    txtStockType2.Text = "";
                    txtStockType2.Visible = true;
                    txtStockType2.BringToFront();
                }
                if (txtStockType3 != null)
                {
                    txtStockType3.Text = "";
                    txtStockType3.Visible = true;
                    txtStockType3.BringToFront();
                }

                if (txtDealerName != null) txtDealerName.BringToFront();
                ApplyPartyFocusOrder();
                try { txtDealerName.Focus(); txtDealerName.Select(); } catch { }
            }
            catch { }
        }

        /// <summary>Ctrl+2 — wapas combo mode.</summary>
        private void ExitPartyFastTextMode()
        {
            try
            {
                _partyFastTextMode = false;

                SyncTextToCombo(txtStockType, cbStockType, StockPartyTypesPair1);
                SyncTextToCombo(txtStockType2, cbStockType2, StockPartyTypesPair2);
                SyncTextToCombo(txtStockType3, cbStockType3, StockPartyTypesPair3);

                if (txtStockType != null) txtStockType.Visible = false;
                if (txtStockType2 != null) txtStockType2.Visible = false;
                if (txtStockType3 != null) txtStockType3.Visible = false;
                if (cbStockType != null) { cbStockType.Visible = true; cbStockType.BringToFront(); }
                if (cbStockType2 != null) { cbStockType2.Visible = true; cbStockType2.BringToFront(); }
                if (cbStockType3 != null) { cbStockType3.Visible = true; cbStockType3.BringToFront(); }

                txtDealerName.Visible = false;
                txtDealerName.TabStop = false;
                if (cbDealer != null)
                {
                    cbDealer.Visible = true;
                    cbDealer.BringToFront();
                    // typed name select / create on next save via EnsurePartyDealerId
                    string typed = (txtDealerName.Text ?? "").Trim();
                    if (!string.IsNullOrEmpty(typed))
                    {
                        try
                        {
                            int idx = cbDealer.FindStringExact(typed);
                            if (idx >= 0) cbDealer.SelectedIndex = idx;
                        }
                        catch { }
                    }
                    try { cbDealer.Focus(); } catch { }
                }

                ApplyPartyFocusOrder();
            }
            catch { }
        }

        private static void CopyComboToText(Guna.UI2.WinForms.Guna2ComboBox cb, Guna.UI2.WinForms.Guna2TextBox tb)
        {
            if (tb == null) return;
            string v = "";
            try
            {
                if (cb != null && cb.SelectedItem != null)
                    v = cb.SelectedItem.ToString().Trim();
            }
            catch { }
            tb.Text = v;
        }

        private static void SyncTextToCombo(
            Guna.UI2.WinForms.Guna2TextBox tb,
            Guna.UI2.WinForms.Guna2ComboBox cb,
            string[] allowed)
        {
            if (cb == null || tb == null) return;
            string v = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(v)) return;
            int idx = cb.FindStringExact(v);
            if (idx >= 0)
            {
                cb.SelectedIndex = idx;
                return;
            }
            // case-insensitive match
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (string.Equals(cb.Items[i]?.ToString()?.Trim(), v, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedIndex = i;
                    return;
                }
            }
        }

        private Control[] GetPartyTabCycle()
        {
            if (_partyFastTextMode || (txtDealerName != null && txtDealerName.Visible))
            {
                return new Control[]
                {
                    txtDealerName, txtvehicle,
                    txtStockType ?? (Control)cbStockType,
                    txtStockType2 ?? (Control)cbStockType2,
                    txtStockType3 ?? (Control)cbStockType3,
                    txtRate, txtNote
                };
            }

            return new Control[]
            {
                txtvehicle, cbStockType, cbStockType2, cbStockType3, txtRate, txtNote
            };
        }

        private Guna.UI2.WinForms.Guna2ComboBox MakePartyTypeCombo(
            string name, Point location, int tabIndex, string[] items)
        {
            var cb = new Guna.UI2.WinForms.Guna2ComboBox
            {
                AutoRoundedCorners = true,
                BackColor = Color.Transparent,
                BorderColor = Color.FromArgb(112, 51, 255),
                BorderRadius = 17,
                DrawMode = DrawMode.OwnerDrawFixed,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FillColor = Color.FromArgb(32, 36, 61),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                ItemHeight = 30,
                Location = location,
                Name = name,
                Size = new Size(145, 36),
                TabIndex = tabIndex
            };
            if (items != null)
            {
                foreach (string t in items)
                    cb.Items.Add(t);
            }
            if (cb.Items.Count > 0)
                cb.SelectedIndex = 0;
            return cb;
        }

        private void ApplyPartyFocusOrder()
        {
            if (_partyFastTextMode || (txtDealerName != null && txtDealerName.Visible))
            {
                if (txtDealerName != null) txtDealerName.TabIndex = 1;
                if (txtvehicle != null) txtvehicle.TabIndex = 2;
                if (txtStockType != null) txtStockType.TabIndex = 3;
                if (txtStockType2 != null) txtStockType2.TabIndex = 4;
                if (txtStockType3 != null) txtStockType3.TabIndex = 5;
                if (txtRate != null) txtRate.TabIndex = 6;
                if (txtdate != null) txtdate.TabIndex = 7;
                if (txtNote != null) txtNote.TabIndex = 8;
            }
            else
            {
                if (txtvehicle != null) txtvehicle.TabIndex = 1;
                if (cbStockType != null) cbStockType.TabIndex = 2;
                if (cbStockType2 != null) cbStockType2.TabIndex = 3;
                if (cbStockType3 != null) cbStockType3.TabIndex = 4;
                if (txtRate != null) txtRate.TabIndex = 5;
                if (txtdate != null) txtdate.TabIndex = 6;
                if (txtNote != null) txtNote.TabIndex = 7;
                if (cbDealer != null) cbDealer.TabIndex = 10;
                if (txtDealerName != null) txtDealerName.TabIndex = 11;
            }

            if (txtvehicle != null)
            {
                txtvehicle.KeyDown -= PartyVehicle_KeyDown;
                txtvehicle.KeyDown += PartyVehicle_KeyDown;
                txtvehicle.Leave -= PartyVehicle_Leave;
                txtvehicle.Leave += PartyVehicle_Leave;
            }
        }

        private void SetupVehicleAutoCompleteParty()
        {
            try
            {
                DataTable dt = MainClass.ExecuteSelectQuery(
                    @"SELECT DISTINCT TRIM(IFNULL(Vehicle,'')) AS Vehicle
                      FROM StockDiesel
                      WHERE TRIM(IFNULL(Vehicle,'')) <> ''
                      ORDER BY Vehicle COLLATE NOCASE
                      LIMIT 2000", null);
                var auto = new AutoCompleteStringCollection();
                if (dt != null)
                {
                    foreach (DataRow row in dt.Rows)
                    {
                        string v = row["Vehicle"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(v))
                            auto.Add(v);
                    }
                }
                txtvehicle.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                txtvehicle.AutoCompleteSource = AutoCompleteSource.CustomSource;
                txtvehicle.AutoCompleteCustomSource = auto;
            }
            catch { }
        }

        private void PartyVehicle_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SuggestPartyByVehicle();
            if (_partyFastTextMode && txtStockType != null && txtStockType.Visible)
                txtStockType.Focus();
            else if (cbStockType != null && cbStockType.Visible)
                cbStockType.Focus();
        }

        private void PartyVehicle_Leave(object sender, EventArgs e)
        {
            SuggestPartyByVehicle();
        }

        private void PartyType_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (_partyFastTextMode && txtStockType2 != null && txtStockType2.Visible)
                txtStockType2.Focus();
            else if (cbStockType2 != null && cbStockType2.Visible)
                cbStockType2.Focus();
            else if (txtRate != null)
            {
                txtRate.Focus();
                txtRate.Select();
            }
        }

        private void PartyType2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (_partyFastTextMode && txtStockType3 != null && txtStockType3.Visible)
                txtStockType3.Focus();
            else if (cbStockType3 != null && cbStockType3.Visible)
                cbStockType3.Focus();
            else if (txtRate != null)
            {
                txtRate.Focus();
                txtRate.Select();
            }
        }

        private void PartyType3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (txtRate != null)
            {
                txtRate.Focus();
                txtRate.Select();
            }
        }

        private void PartyAmount_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (txtNote != null)
            {
                txtNote.Focus();
                txtNote.Select();
            }
        }

        /// <summary>
        /// Vehicle likho → Name auto: pehle usi date ki last entry, warna nearest pehli date.
        /// Same vehicle multiple names (Irtaza aaj / Muntaha kal) — COUNT nahi, date-wise latest.
        /// </summary>
        private void SuggestPartyByVehicle()
        {
            try
            {
                string vehicle = (txtvehicle.Text ?? "").Trim();
                if (string.IsNullOrEmpty(vehicle)) return;

                DateTime entryDate = DateTime.Today;
                try
                {
                    if (txtdate != null)
                        entryDate = txtdate.Value.Date;
                }
                catch { entryDate = DateTime.Today; }

                // 1) Same date latest  2) else latest on/before date  3) else overall latest
                DataTable dt = MainClass.ExecuteSelectQuery(@"
                    SELECT s.SDid,
                           p.PartyName AS DealerName,
                           IFNULL(s.StockType,'') AS StockType,
                           IFNULL(s.StockType2,'') AS StockType2,
                           IFNULL(s.StockType3,'') AS StockType3
                    FROM StockDiesel s
                    INNER JOIN StockPartyName p ON p.Pid = s.SDid
                    WHERE TRIM(IFNULL(s.Vehicle,'')) = @veh COLLATE NOCASE
                    ORDER BY
                      CASE WHEN date(s.Date) = date(@entryDate) THEN 0 ELSE 1 END,
                      CASE WHEN date(s.Date) <= date(@entryDate) THEN 0 ELSE 1 END,
                      date(s.Date) DESC,
                      s.SID DESC
                    LIMIT 1",
                    new Hashtable
                    {
                        { "@veh", vehicle },
                        { "@entryDate", entryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                    });

                if (dt == null || dt.Rows.Count == 0) return;

                DataRow row = dt.Rows[0];
                int did = Convert.ToInt32(row["SDid"]);
                string partyName = row["DealerName"]?.ToString() ?? "";

                if (cbDealer != null && cbDealer.DataSource != null)
                    cbDealer.SelectedValue = did;
                if (txtDealerName != null)
                    txtDealerName.Text = partyName;

                ApplyPartyTypeFromSuggest(row["StockType"]?.ToString(), cbStockType, txtStockType);
                ApplyPartyTypeFromSuggest(row["StockType2"]?.ToString(), cbStockType2, txtStockType2);
                ApplyPartyTypeFromSuggest(row["StockType3"]?.ToString(), cbStockType3, txtStockType3);
            }
            catch { }
        }

        private void ApplyPartyTypeFromSuggest(
            string value,
            Guna.UI2.WinForms.Guna2ComboBox combo,
            Guna.UI2.WinForms.Guna2TextBox textBox)
        {
            string v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v)) return;

            if (_partyFastTextMode && textBox != null && textBox.Visible)
            {
                textBox.Text = v;
                return;
            }

            if (combo != null && combo.Visible)
            {
                int idx = combo.FindStringExact(v);
                if (idx >= 0) combo.SelectedIndex = idx;
            }
            else if (textBox != null)
            {
                textBox.Text = v;
            }
        }

        private void FocusPartyVehicle()
        {
            try
            {
                if (txtvehicle == null || txtvehicle.IsDisposed) return;
                ActiveControl = txtvehicle;
                txtvehicle.Focus();
                txtvehicle.Select();
            }
            catch { }
        }

        private string GetPartyStockType()
        {
            if (_partyFastTextMode && txtStockType != null && txtStockType.Visible)
                return (txtStockType.Text ?? "").Trim();
            if (cbStockType != null && cbStockType.SelectedItem != null)
                return cbStockType.SelectedItem.ToString().Trim();
            if (txtStockType != null)
                return (txtStockType.Text ?? "").Trim();
            return "";
        }

        private string GetPartyStockType2()
        {
            if (_partyFastTextMode && txtStockType2 != null && txtStockType2.Visible)
                return (txtStockType2.Text ?? "").Trim();
            if (cbStockType2 != null && cbStockType2.SelectedItem != null)
                return cbStockType2.SelectedItem.ToString().Trim();
            if (txtStockType2 != null)
                return (txtStockType2.Text ?? "").Trim();
            return "";
        }

        private string GetPartyStockType3()
        {
            if (_partyFastTextMode && txtStockType3 != null && txtStockType3.Visible)
                return (txtStockType3.Text ?? "").Trim();
            if (cbStockType3 != null && cbStockType3.SelectedItem != null)
                return cbStockType3.SelectedItem.ToString().Trim();
            if (txtStockType3 != null)
                return (txtStockType3.Text ?? "").Trim();
            return "";
        }

        private static string NormalizePartyType(string value, string[] allowed)
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

        private void LoadPartyFieldsFromRow(DataRow row)
        {
            try
            {
                decimal rate = row["Rate"] != DBNull.Value ? Convert.ToDecimal(row["Rate"]) : 0;
                decimal litter = row["Litter"] != DBNull.Value ? Convert.ToDecimal(row["Litter"]) : 0;
                decimal credit = row["Credit"] != DBNull.Value ? Convert.ToDecimal(row["Credit"]) : 0;
                decimal amount = credit != 0 ? credit : (rate * Math.Abs(litter == 0 ? 1 : litter));
                if (txtRate != null)
                    txtRate.Text = amount.ToString("0.##", CultureInfo.InvariantCulture);

                if (cbStockType != null && row.Table.Columns.Contains("StockType") && row["StockType"] != DBNull.Value)
                {
                    string type = row["StockType"]?.ToString()?.Trim();
                    int idx = string.IsNullOrEmpty(type) ? -1 : cbStockType.FindStringExact(type);
                    if (idx >= 0) cbStockType.SelectedIndex = idx;
                    else if (cbStockType2 != null)
                    {
                        // purana data: Diesel/Chaliyan Type1 pe save tha to Type2 pe set
                        int idx2 = cbStockType2.FindStringExact(type);
                        if (idx2 >= 0) cbStockType2.SelectedIndex = idx2;
                    }
                }

                if (cbStockType2 != null && row.Table.Columns.Contains("StockType2") && row["StockType2"] != DBNull.Value)
                {
                    string type2 = row["StockType2"]?.ToString()?.Trim();
                    int idx2 = string.IsNullOrEmpty(type2) ? -1 : cbStockType2.FindStringExact(type2);
                    if (idx2 >= 0) cbStockType2.SelectedIndex = idx2;
                    else if (cbStockType != null)
                    {
                        int idx1 = cbStockType.FindStringExact(type2);
                        if (idx1 >= 0) cbStockType.SelectedIndex = idx1;
                    }
                }

                if (cbStockType3 != null && row.Table.Columns.Contains("StockType3") && row["StockType3"] != DBNull.Value)
                {
                    string type3 = row["StockType3"]?.ToString()?.Trim();
                    int idx3 = string.IsNullOrEmpty(type3) ? -1 : cbStockType3.FindStringExact(type3);
                    if (idx3 >= 0) cbStockType3.SelectedIndex = idx3;
                }

                if (txtStockType != null && cbStockType != null && cbStockType.SelectedItem != null)
                    txtStockType.Text = cbStockType.SelectedItem.ToString();
                if (txtStockType2 != null && cbStockType2 != null && cbStockType2.SelectedItem != null)
                    txtStockType2.Text = cbStockType2.SelectedItem.ToString();
                if (txtStockType3 != null && cbStockType3 != null && cbStockType3.SelectedItem != null)
                    txtStockType3.Text = cbStockType3.SelectedItem.ToString();

                if (txtNote != null)
                    txtNote.Text = StockDieselLitterHelper.StripMinusTag(row["Note"]?.ToString() ?? "");
            }
            catch { }
        }

        /// <summary>Stock Add combo: StockPartyName only (AddDealer nahi).</summary>
        private void PopulatePartyDealerNames()
        {
            StockPartyNameStore.EnsureDefault();
            try
            {
                DataTable dt = StockPartyNameStore.ListAll();

                cbDealer.DataSource = null;
                if (dt != null && dt.Rows.Count > 0)
                {
                    cbDealer.DisplayMember = "DealerName";
                    cbDealer.ValueMember = "Did";
                    cbDealer.DataSource = dt;
                    cbDealer.DropDownHeight = 220;
                    cbDealer.IntegralHeight = false;

                    var autoSource = new AutoCompleteStringCollection();
                    foreach (DataRow row in dt.Rows)
                        autoSource.Add(row["DealerName"].ToString());
                    txtDealerName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    txtDealerName.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    txtDealerName.AutoCompleteCustomSource = autoSource;
                }
                else
                {
                    cbDealer.DataSource = null;
                }

                txtDealerName.Visible = false;
                cbDealer.Visible = true;
            }
            catch (Exception ex)
            {
                ErrorFormMessage errorMessage = new ErrorFormMessage("Error: " + ex.Message, "Error");
                errorMessage.ShowDialog();
            }
        }

        private void SelectRozNamchaDefault()
        {
            try
            {
                int did = StockPartyNameStore.EnsureDefault();
                if (did <= 0) return;
                if (cbDealer.DataSource != null)
                    cbDealer.SelectedValue = did;
                txtDealerName.Text = StockPartyNameStore.DefaultName;
                txtDealerName.Visible = false;
                cbDealer.Visible = true;
            }
            catch { }
        }

        /// <summary>Select existing StockPartyName or create from typed name (Ctrl+1 / Add2).</summary>
        private int EnsurePartyDealerId()
        {
            // Fast text mode: typed Name pehle
            if (_partyFastTextMode || (txtDealerName != null && txtDealerName.Visible))
            {
                string typed = (txtDealerName.Text ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(typed))
                {
                    int newId = StockPartyNameStore.EnsureExists(typed);
                    if (newId <= 0) return 0;
                    try
                    {
                        PopulatePartyDealerNames();
                        cbDealer.SelectedValue = newId;
                        txtDealerName.Text = typed;
                        if (!_partyFastTextMode)
                        {
                            txtDealerName.Visible = false;
                            cbDealer.Visible = true;
                        }
                    }
                    catch { }
                    return newId;
                }
            }

            // Guna combo kabhi DataRowView return karta hai — SelectedValue Int32 fail
            try
            {
                if (cbDealer != null && cbDealer.SelectedItem is DataRowView drv
                    && drv.Row != null && drv.Row.Table.Columns.Contains("Did")
                    && drv["Did"] != DBNull.Value)
                    return Convert.ToInt32(drv["Did"]);
            }
            catch { }

            if (cbDealer != null && cbDealer.SelectedValue != null
                && cbDealer.SelectedValue != DBNull.Value
                && !(cbDealer.SelectedValue is DataRowView))
            {
                try { return Convert.ToInt32(cbDealer.SelectedValue); } catch { }
            }

            string name = "";
            try
            {
                if (cbDealer != null && cbDealer.SelectedItem is DataRowView drv2
                    && drv2.Row != null && drv2.Row.Table.Columns.Contains("DealerName"))
                    name = Convert.ToString(drv2["DealerName"]) ?? "";
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name) && cbDealer != null)
                name = (cbDealer.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            int idFromName = StockPartyNameStore.EnsureExists(name);
            if (idFromName <= 0) return 0;

            PopulatePartyDealerNames();
            try
            {
                cbDealer.SelectedValue = idFromName;
                txtDealerName.Text = name;
                txtDealerName.Visible = false;
                cbDealer.Visible = true;
            }
            catch { }
            return idFromName;
        }

        private bool TrySavePartyStockEntry()
        {
            // Party mode confirm — warna galat save path
            try { ApplyPartyStockMode(); } catch { }

            DateTime specifiedDate = DateTime.Today;
            try { if (txtdate != null) specifiedDate = txtdate.Value.Date; } catch { }

            // Amount optional — empty/invalid pe 0 (validation nahi)
            string amountText = (txtRate != null ? txtRate.Text : "") ?? "";
            amountText = amountText.Trim().Replace(",", "");
            double amountD = 0;
            if (!string.IsNullOrWhiteSpace(amountText))
            {
                if (!double.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amountD))
                    double.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out amountD);
            }

            // Empty fields pe default — Ctrl+1 empty hone pe block mat karo
            int dealerId = EnsurePartyDealerId();
            if (dealerId <= 0)
            {
                try { dealerId = StockPartyNameStore.EnsureDefault(); } catch { dealerId = 0; }
            }
            if (dealerId <= 0)
            {
                ErrorFormMessage err = new ErrorFormMessage(
                    "Name save nahi hua. Add2 se Name add karein.", "Error");
                err.ShowDialog();
                return true;
            }

            string stockType = NormalizePartyType(GetPartyStockType(), StockPartyTypesPair1);
            if (string.IsNullOrWhiteSpace(stockType)) stockType = StockPartyTypesPair1[0];
            string stockType2 = NormalizePartyType(GetPartyStockType2(), StockPartyTypesPair2);
            if (string.IsNullOrWhiteSpace(stockType2)) stockType2 = StockPartyTypesPair2[0];
            string stockType3 = NormalizePartyType(GetPartyStockType3(), StockPartyTypesPair3);
            if (string.IsNullOrWhiteSpace(stockType3)) stockType3 = StockPartyTypesPair3[0];

            object vehicleVal = string.IsNullOrWhiteSpace(txtvehicle != null ? txtvehicle.Text : null)
                ? (object)DBNull.Value : txtvehicle.Text.Trim();
            object noteVal = string.IsNullOrWhiteSpace(txtNote != null ? txtNote.Text : null)
                ? (object)DBNull.Value : txtNote.Text.Trim();
            string dateStr = specifiedDate.ToString("yyyy-MM-dd");
            int editId = id;

            if (editId > 0)
            {
                try
                {
                    DataTable exists = MainClass.ExecuteSelectQuery(
                        "SELECT SID FROM StockDiesel WHERE SID=@id LIMIT 1",
                        new Hashtable { { "@id", editId } });
                    if (exists == null || exists.Rows.Count == 0)
                        editId = 0;
                }
                catch { editId = 0; }
            }

            try
            {
                string cs = ZaibPetroleumService.ProjectConnection.projectconnection.conReturn();
                long savedSid = 0;
                using (var con = new System.Data.SQLite.SQLiteConnection(cs))
                {
                    con.Open();

                    using (var cmd = con.CreateCommand())
                    {
                        if (editId == 0)
                        {
                            cmd.CommandText =
                                @"INSERT INTO StockDiesel
                                    (Vehicle, Rate, Litter, Date, SDid, Credit, Debit, Note,
                                     StockType, StockType2, StockType3, SyncDirty, UpdatedAt)
                                  VALUES
                                    (@vehicle, @rate, 1, @date, @did, @credit, 0, @note,
                                     @stype, @stype2, @stype3, 1,
                                     strftime('%Y-%m-%dT%H:%M:%fZ','now'))";
                        }
                        else
                        {
                            cmd.CommandText =
                                @"UPDATE StockDiesel SET
                                    Vehicle=@vehicle, Rate=@rate, Litter=1, Date=@date,
                                    SDid=@did, Credit=@credit, Debit=0, Note=@note,
                                    StockType=@stype, StockType2=@stype2, StockType3=@stype3,
                                    SyncDirty=1,
                                    UpdatedAt=strftime('%Y-%m-%dT%H:%M:%fZ','now')
                                  WHERE SID=@id";
                            cmd.Parameters.AddWithValue("@id", editId);
                        }

                        cmd.Parameters.AddWithValue("@vehicle", vehicleVal);
                        cmd.Parameters.AddWithValue("@rate", amountD);
                        cmd.Parameters.AddWithValue("@date", dateStr);
                        cmd.Parameters.AddWithValue("@did", dealerId);
                        cmd.Parameters.AddWithValue("@credit", amountD);
                        cmd.Parameters.AddWithValue("@note", noteVal);
                        cmd.Parameters.AddWithValue("@stype", stockType);
                        cmd.Parameters.AddWithValue("@stype2", stockType2);
                        cmd.Parameters.AddWithValue("@stype3", stockType3);

                        int affected = cmd.ExecuteNonQuery();
                        if (editId == 0)
                            savedSid = con.LastInsertRowId;
                        else if (affected > 0)
                            savedSid = editId;
                        else
                        {
                            // delete-then-save: UPDATE 0 → INSERT
                            cmd.Parameters.Clear();
                            cmd.CommandText =
                                @"INSERT INTO StockDiesel
                                    (Vehicle, Rate, Litter, Date, SDid, Credit, Debit, Note,
                                     StockType, StockType2, StockType3, SyncDirty, UpdatedAt)
                                  VALUES
                                    (@vehicle, @rate, 1, @date, @did, @credit, 0, @note,
                                     @stype, @stype2, @stype3, 1,
                                     strftime('%Y-%m-%dT%H:%M:%fZ','now'))";
                            cmd.Parameters.AddWithValue("@vehicle", vehicleVal);
                            cmd.Parameters.AddWithValue("@rate", amountD);
                            cmd.Parameters.AddWithValue("@date", dateStr);
                            cmd.Parameters.AddWithValue("@did", dealerId);
                            cmd.Parameters.AddWithValue("@credit", amountD);
                            cmd.Parameters.AddWithValue("@note", noteVal);
                            cmd.Parameters.AddWithValue("@stype", stockType);
                            cmd.Parameters.AddWithValue("@stype2", stockType2);
                            cmd.Parameters.AddWithValue("@stype3", stockType3);
                            cmd.ExecuteNonQuery();
                            savedSid = con.LastInsertRowId;
                        }
                    }

                    // Audit (MainClass jaisa) — confirm save DB mein gaya
                    try
                    {
                        using (var audit = con.CreateCommand())
                        {
                            audit.CommandText =
                                @"INSERT INTO AuditLog
                                    (ActionTime, UserName, UserRole, ActionType, TableName, RecordId, QuerySummary, Details)
                                  VALUES
                                    (@t, @u, @r, @a, @tbl, @rid, @q, @d)";
                            audit.Parameters.AddWithValue("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            audit.Parameters.AddWithValue("@u", "admin");
                            audit.Parameters.AddWithValue("@r", "Admin");
                            audit.Parameters.AddWithValue("@a", editId > 0 && savedSid == editId ? "UPDATE" : "INSERT");
                            audit.Parameters.AddWithValue("@tbl", "StockDiesel");
                            audit.Parameters.AddWithValue("@rid", savedSid.ToString());
                            audit.Parameters.AddWithValue("@q", "party-stock-save");
                            audit.Parameters.AddWithValue("@d",
                                "SID=" + savedSid + "; SDid=" + dealerId + "; Amount=" + amountD + "; Date=" + dateStr);
                            audit.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }

                if (savedSid <= 0)
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("Stock data save nahi hua!", "Error");
                    errorMessage.ShowDialog();
                    return true;
                }

                try
                {
                    CustomeMessage successMessage = new CustomeMessage(
                        "Stock entry save ho gayi! (#" + savedSid + ")", "Success");
                    successMessage.ShowDialog();
                }
                catch { }

                id = 0;
                // Name + Date fix; halka clear — DB reload / double grid refresh nahi
                ResetPartyAfterSaveKeepNameDate(dealerId, specifiedDate);
                NotifyStockViewRefresh(specifiedDate);
                try { BeginInvoke(new Action(FocusPartyVehicle)); }
                catch { FocusPartyVehicle(); }
            }
            catch (Exception ex)
            {
                try
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage(
                        "Stock save error: " + ex.Message, "Error");
                    errorMessage.ShowDialog();
                }
                catch
                {
                    MessageBox.Show("Stock save error: " + ex.Message);
                }
            }
            return true;
        }

        /// <summary>Save ke baad Name+Date same; sirf Vehicle/Amount/Note clear — slow Populate/AutoComplete skip.</summary>
        private void ResetPartyAfterSaveKeepNameDate(int keepDealerId, DateTime keepDate)
        {
            try
            {
                string keepName = "";
                try
                {
                    if (cbDealer != null && cbDealer.SelectedItem is DataRowView drv
                        && drv.Row != null && drv.Row.Table.Columns.Contains("DealerName"))
                        keepName = Convert.ToString(drv["DealerName"]) ?? "";
                }
                catch { }
                if (string.IsNullOrWhiteSpace(keepName) && txtDealerName != null)
                    keepName = (txtDealerName.Text ?? "").Trim();

                if (txtvehicle != null) txtvehicle.Text = "";
                if (txtRate != null) txtRate.Text = "";
                if (txtNote != null) txtNote.Text = "";

                try { if (txtdate != null) txtdate.Value = keepDate; } catch { }

                if (keepDealerId > 0 && cbDealer != null && cbDealer.DataSource != null)
                {
                    try { cbDealer.SelectedValue = keepDealerId; } catch { }
                }
                if (txtDealerName != null && !string.IsNullOrWhiteSpace(keepName))
                    txtDealerName.Text = keepName;

                if (_partyFastTextMode)
                {
                    if (txtStockType != null) txtStockType.Text = "";
                    if (txtStockType2 != null) txtStockType2.Text = "";
                    if (txtStockType3 != null) txtStockType3.Text = "";
                }
                else
                {
                    if (cbStockType != null && cbStockType.Items.Count > 0)
                        cbStockType.SelectedIndex = 0;
                    if (cbStockType2 != null && cbStockType2.Items.Count > 0)
                        cbStockType2.SelectedIndex = 0;
                    if (cbStockType3 != null && cbStockType3.Items.Count > 0)
                        cbStockType3.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void NotifyStockViewRefresh(DateTime? entryDate = null)
        {
            try
            {
                DateTime refreshDate = entryDate ?? DateTime.Now;
                ZaibPetroleumService.View.frmStockDieselView stockView =
                    Owner as ZaibPetroleumService.View.frmStockDieselView;

                if (stockView == null || stockView.IsDisposed)
                {
                    foreach (Form f in Application.OpenForms)
                    {
                        stockView = f as ZaibPetroleumService.View.frmStockDieselView;
                        if (stockView != null && !stockView.IsDisposed) break;
                        try
                        {
                            foreach (Control c in f.Controls)
                            {
                                if (c is ZaibPetroleumService.View.frmStockDieselView sv && !sv.IsDisposed)
                                {
                                    stockView = sv;
                                    break;
                                }
                            }
                        }
                        catch { }
                        if (stockView != null) break;
                    }
                }

                if (stockView == null || stockView.IsDisposed) return;

                Action refresh = () =>
                {
                    try { stockView.RefreshPartyStockGrid(refreshDate); } catch { }
                };

                // Async — save turant complete; grid peeche refresh
                if (stockView.IsHandleCreated)
                    stockView.BeginInvoke(refresh);
                else
                    refresh();
            }
            catch { }
        }
    }
}
