using System;
using System.Collections;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using ZaibPetroleumService.ProjectConnection;

namespace ZaibPetroleumService.View
{
    /// <summary>
    /// DealerAmount: grid pe sirf Amount + Credit edit (stock rows).
    /// Dealer / Vehicle / Location — touch nahi. Row highlight Daily Diesel jaisa.
    /// </summary>
    public partial class frmStockView
    {
        private bool _quickDealerAmountEventsReady;
        private bool _savingQuickDealerAmount;
        private int _dealerHighlightRow = -1;
        private bool _dealerFormattingMoneyEdit;
        private TextBox _dealerMoneyEditBox;

        private void EnableQuickDealerAmountCreditEdit()
        {
            if (guna2DataGridView1 == null) return;

            try
            {
                guna2DataGridView1.ReadOnly = false;
                try { guna2DataGridView1.ThemeStyle.ReadOnly = false; } catch { }

                foreach (DataGridViewColumn col in guna2DataGridView1.Columns)
                {
                    bool editable = col.Name == "Amount" || col.Name == "Credit";
                    col.ReadOnly = !editable;
                }

                if (guna2DataGridView1.Columns.Contains("Amount"))
                {
                    var a = guna2DataGridView1.Columns["Amount"];
                    a.ReadOnly = false;
                    a.DefaultCellStyle.Format = "N0";
                    a.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    a.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    a.DefaultCellStyle.BackColor = Color.FromArgb(40, 48, 80);
                    a.HeaderText = "Amount ✎";
                }
                if (guna2DataGridView1.Columns.Contains("Credit"))
                {
                    var c = guna2DataGridView1.Columns["Credit"];
                    c.ReadOnly = false;
                    c.DefaultCellStyle.Format = "N0";
                    c.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    c.DefaultCellStyle.ForeColor = Color.Red;
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    c.DefaultCellStyle.BackColor = Color.FromArgb(55, 35, 45);
                    c.HeaderText = "Credit ✎";
                }

                guna2DataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                guna2DataGridView1.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
                guna2DataGridView1.MultiSelect = false;
                guna2DataGridView1.DefaultCellStyle.SelectionBackColor = Color.FromArgb(88, 60, 160);
                guna2DataGridView1.DefaultCellStyle.SelectionForeColor = Color.White;
                try
                {
                    guna2DataGridView1.ThemeStyle.RowsStyle.SelectionBackColor = Color.FromArgb(88, 60, 160);
                    guna2DataGridView1.ThemeStyle.RowsStyle.SelectionForeColor = Color.White;
                }
                catch { }

                if (_quickDealerAmountEventsReady) return;
                _quickDealerAmountEventsReady = true;

                guna2DataGridView1.CellEndEdit -= DealerQuick_CellEndEdit;
                guna2DataGridView1.CellEndEdit += DealerQuick_CellEndEdit;
                guna2DataGridView1.CellBeginEdit -= DealerQuick_CellBeginEdit;
                guna2DataGridView1.CellBeginEdit += DealerQuick_CellBeginEdit;
                guna2DataGridView1.KeyDown -= DealerQuick_KeyDown;
                guna2DataGridView1.KeyDown += DealerQuick_KeyDown;
                guna2DataGridView1.CellEnter -= DealerQuick_CellEnter;
                guna2DataGridView1.CellEnter += DealerQuick_CellEnter;
                guna2DataGridView1.RowPrePaint -= DealerQuick_RowPrePaint;
                guna2DataGridView1.RowPrePaint += DealerQuick_RowPrePaint;
                guna2DataGridView1.EditingControlShowing -= DealerQuick_EditingControlShowing;
                guna2DataGridView1.EditingControlShowing += DealerQuick_EditingControlShowing;
            }
            catch { }
        }

        private void DealerQuick_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (_dealerMoneyEditBox != null)
            {
                _dealerMoneyEditBox.TextChanged -= DealerMoneyEdit_TextChanged;
                _dealerMoneyEditBox.KeyPress -= DealerMoneyEdit_KeyPress;
                _dealerMoneyEditBox = null;
            }

            if (guna2DataGridView1.CurrentCell == null) return;
            string col = guna2DataGridView1.Columns[guna2DataGridView1.CurrentCell.ColumnIndex].Name;
            if (col != "Amount" && col != "Credit") return;

            var tb = e.Control as TextBox;
            if (tb == null) return;

            _dealerMoneyEditBox = tb;
            tb.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
            tb.ForeColor = col == "Credit" ? Color.Red : Color.White;
            tb.TextAlign = HorizontalAlignment.Right;
            tb.KeyPress -= DealerMoneyEdit_KeyPress;
            tb.KeyPress += DealerMoneyEdit_KeyPress;
            tb.TextChanged -= DealerMoneyEdit_TextChanged;
            tb.TextChanged += DealerMoneyEdit_TextChanged;

            try { ApplyDealerLiveCommaFormat(tb); } catch { }
        }

        private void DealerMoneyEdit_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar)) return;
            if (char.IsDigit(e.KeyChar)) return;
            e.Handled = true;
        }

        private void DealerMoneyEdit_TextChanged(object sender, EventArgs e)
        {
            if (_dealerFormattingMoneyEdit) return;
            var tb = sender as TextBox;
            if (tb == null) return;
            try { ApplyDealerLiveCommaFormat(tb); } catch { }
        }

        private void ApplyDealerLiveCommaFormat(TextBox tb)
        {
            if (tb == null || tb.IsDisposed) return;
            if (_dealerFormattingMoneyEdit) return;

            string raw = tb.Text ?? "";
            int sel = tb.SelectionStart;
            int digitsBeforeCursor = 0;
            for (int i = 0; i < raw.Length && i < sel; i++)
            {
                if (char.IsDigit(raw[i])) digitsBeforeCursor++;
            }

            var digits = new StringBuilder();
            foreach (char ch in raw)
            {
                if (char.IsDigit(ch)) digits.Append(ch);
            }

            string digitStr = digits.ToString();
            string formatted;
            if (string.IsNullOrEmpty(digitStr))
                formatted = "";
            else if (decimal.TryParse(digitStr, NumberStyles.None, CultureInfo.InvariantCulture, out decimal val))
                formatted = val.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
            else
                formatted = digitStr;

            if (string.Equals(tb.Text, formatted, StringComparison.Ordinal)) return;

            _dealerFormattingMoneyEdit = true;
            try
            {
                tb.Text = formatted;
                int newPos = 0, seen = 0;
                for (int i = 0; i < formatted.Length; i++)
                {
                    if (char.IsDigit(formatted[i]))
                    {
                        seen++;
                        if (seen >= digitsBeforeCursor)
                        {
                            newPos = i + 1;
                            break;
                        }
                    }
                    if (i == formatted.Length - 1)
                        newPos = formatted.Length;
                }
                if (digitsBeforeCursor <= 0) newPos = formatted.Length;
                tb.SelectionStart = Math.Min(Math.Max(newPos, 0), tb.Text.Length);
                tb.SelectionLength = 0;
            }
            finally
            {
                _dealerFormattingMoneyEdit = false;
            }
        }

        private void DealerQuick_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            _dealerHighlightRow = e.RowIndex;
            try
            {
                guna2DataGridView1.Rows[e.RowIndex].Selected = true;
                guna2DataGridView1.InvalidateRow(e.RowIndex);
            }
            catch { }
        }

        private void DealerQuick_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= guna2DataGridView1.Rows.Count) return;
            if (e.RowIndex != _dealerHighlightRow) return;
            try
            {
                Rectangle bounds = e.RowBounds;
                using (var pen = new Pen(Color.FromArgb(212, 175, 55), 2))
                    e.Graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            }
            catch { }
        }

        private bool IsDealerStockEditableRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= guna2DataGridView1.Rows.Count) return false;
            DataGridViewRow row = guna2DataGridView1.Rows[rowIndex];
            if (row.IsNewRow) return false;
            if (!(row.DataBoundItem is DataRowView drv)) return false;
            if (!drv.Row.Table.Columns.Contains("Src")) return true;
            string src = (drv["Src"]?.ToString() ?? "").Trim();
            return string.Equals(src, "stock", StringComparison.OrdinalIgnoreCase);
        }

        private void DealerQuick_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) { e.Cancel = true; return; }
            string name = guna2DataGridView1.Columns[e.ColumnIndex].Name;
            if (name != "Amount" && name != "Credit")
            {
                e.Cancel = true;
                return;
            }
            // Payout rows — Amount/Credit yahan se edit nahi (Dealer Payout form)
            if (!IsDealerStockEditableRow(e.RowIndex))
            {
                e.Cancel = true;
                return;
            }
            _dealerHighlightRow = e.RowIndex;
            try { guna2DataGridView1.Rows[e.RowIndex].Selected = true; } catch { }
        }

        private void DealerQuick_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_savingQuickDealerAmount) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string colName = guna2DataGridView1.Columns[e.ColumnIndex].Name;
            if (colName != "Amount" && colName != "Credit") return;

            try
            {
                SaveQuickDealerAmountCreditRow(e.RowIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Amount/Credit save nahi hua:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Enter: Amount → Credit same row; Credit → next stock Amount.
        /// F2: edit current Amount/Credit.
        /// </summary>
        private void DealerQuick_KeyDown(object sender, KeyEventArgs e)
        {
            if (guna2DataGridView1 == null || guna2DataGridView1.CurrentCell == null) return;
            if (guna2DataGridView1.IsCurrentCellInEditMode) return;

            string col = guna2DataGridView1.Columns[guna2DataGridView1.CurrentCell.ColumnIndex].Name;

            if (e.KeyCode == Keys.F2 && (col == "Amount" || col == "Credit"))
            {
                if (!IsDealerStockEditableRow(guna2DataGridView1.CurrentCell.RowIndex)) return;
                e.Handled = true;
                try { guna2DataGridView1.BeginEdit(true); } catch { }
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            if (col != "Amount" && col != "Credit") return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            int row = guna2DataGridView1.CurrentCell.RowIndex;
            _dealerHighlightRow = row;
            try
            {
                if (col == "Amount" && guna2DataGridView1.Columns.Contains("Credit"))
                {
                    guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[row].Cells["Credit"];
                    guna2DataGridView1.Rows[row].Selected = true;
                    guna2DataGridView1.BeginEdit(true);
                }
                else
                {
                    int next = row + 1;
                    while (next < guna2DataGridView1.Rows.Count && !guna2DataGridView1.Rows[next].IsNewRow
                           && !IsDealerStockEditableRow(next))
                        next++;

                    if (next < guna2DataGridView1.Rows.Count && !guna2DataGridView1.Rows[next].IsNewRow
                        && guna2DataGridView1.Columns.Contains("Amount")
                        && IsDealerStockEditableRow(next))
                    {
                        _dealerHighlightRow = next;
                        guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[next].Cells["Amount"];
                        guna2DataGridView1.Rows[next].Selected = true;
                        guna2DataGridView1.BeginEdit(true);
                    }
                }
            }
            catch { }
        }

        private void SaveQuickDealerAmountCreditRow(int rowIndex)
        {
            if (!IsDealerStockEditableRow(rowIndex)) return;

            DataGridViewRow row = guna2DataGridView1.Rows[rowIndex];
            if (!guna2DataGridView1.Columns.Contains("Sid")
                || row.Cells["Sid"].Value == null || row.Cells["Sid"].Value == DBNull.Value)
                return;

            int sid = Convert.ToInt32(row.Cells["Sid"].Value);
            if (sid <= 0) return;

            int dealerId = 0;
            if (row.DataBoundItem is DataRowView drvOld
                && drvOld.Row.Table.Columns.Contains("DealerId")
                && drvOld["DealerId"] != DBNull.Value)
                dealerId = Convert.ToInt32(drvOld["DealerId"]);

            decimal amount = ParseDealerMoneyCell(row.Cells["Amount"].Value);
            decimal credit = ParseDealerMoneyCell(row.Cells["Credit"].Value);

            _savingQuickDealerAmount = true;
            try
            {
                decimal oldNet = 0m;
                int dbDealerId = dealerId;

                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    using (var tx = con.BeginTransaction())
                    {
                        using (var cmdOld = new SQLiteCommand(@"
SELECT IFNULL(Amount,0) AS Amount, IFNULL(Credit,0) AS Credit,
       IFNULL(AddDisel,0) AS AddDisel, IFNULL(Rate,0) AS Rate,
       IFNULL(DealerId,0) AS DealerId
FROM AddStock WHERE Sid=@sid LIMIT 1", con, tx))
                        {
                            cmdOld.Parameters.AddWithValue("@sid", sid);
                            using (var r = cmdOld.ExecuteReader())
                            {
                                if (!r.Read()) return;
                                decimal oa = Convert.ToDecimal(r["Amount"]);
                                decimal oc = Convert.ToDecimal(r["Credit"]);
                                decimal litter = Convert.ToDecimal(r["AddDisel"]);
                                decimal rate = Convert.ToDecimal(r["Rate"]);
                                dbDealerId = Convert.ToInt32(r["DealerId"]);
                                if (oa == 0m && litter != 0m && rate != 0m)
                                    oa = litter * rate;
                                oldNet = oa - oc;
                            }
                        }

                        decimal newNet = amount - credit;

                        using (var cmdUp = new SQLiteCommand(@"
UPDATE AddStock SET
  Amount = @amount,
  Credit = @credit,
  SyncDirty = 1,
  UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ','now')
WHERE Sid = @sid", con, tx))
                        {
                            cmdUp.Parameters.AddWithValue("@amount", amount);
                            cmdUp.Parameters.AddWithValue("@credit", credit);
                            cmdUp.Parameters.AddWithValue("@sid", sid);
                            if (cmdUp.ExecuteNonQuery() <= 0) return;
                        }

                        if (dbDealerId > 0 && oldNet != newNet)
                        {
                            using (var cmdDd = new SQLiteCommand(
                                "UPDATE AddDealer SET DDAmount = DDAmount - @oldNet + @newNet WHERE Did = @Did",
                                con, tx))
                            {
                                cmdDd.Parameters.AddWithValue("@oldNet", oldNet);
                                cmdDd.Parameters.AddWithValue("@newNet", newNet);
                                cmdDd.Parameters.AddWithValue("@Did", dbDealerId);
                                cmdDd.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                    }
                }

                if (row.DataBoundItem is DataRowView drv)
                {
                    if (drv.Row.Table.Columns.Contains("Amount"))
                        drv["Amount"] = amount;
                    if (drv.Row.Table.Columns.Contains("Credit"))
                        drv["Credit"] = credit;
                }

                row.Cells["Amount"].Value = amount;
                row.Cells["Credit"].Value = credit;

                try { RefreshDealerRunningBalancesFromGrid(); } catch { }
                try { CalculateTotals(); } catch { }
            }
            finally
            {
                _savingQuickDealerAmount = false;
            }
        }

        /// <summary>Bound table pe Running Balance dubara — Amount − Credit chain.</summary>
        private void RefreshDealerRunningBalancesFromGrid()
        {
            if (guna2DataGridView1?.DataSource is DataTable dt)
            {
                ApplyDealerRunningBalance(dt);
                return;
            }

            if (guna2DataGridView1?.DataSource is DataView dv && dv.Table != null)
            {
                ApplyDealerRunningBalance(dv.Table);
                return;
            }
        }

        private static decimal ParseDealerMoneyCell(object value)
        {
            if (value == null || value == DBNull.Value) return 0m;
            string s = value.ToString().Replace(",", "").Trim();
            if (string.IsNullOrEmpty(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                return d;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("en-IN"), out d))
                return d;
            return 0m;
        }
    }
}
