using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Daily Diesel: grid pe sirf Amount + Credit edit (har row alag).
    /// Name / Vehicle / Location / Item — touch nahi.
    /// Running Balance VIP (sale + simple credit) — full history cumulative.
    /// </summary>
    public partial class frmDiselView
    {
        private bool _quickAmountReady;
        private bool _savingQuickAmount;
        private int _highlightRow = -1;
        private bool _formattingMoneyEdit;
        private TextBox _moneyEditBox;

        private void EnableQuickAmountCreditEdit()
        {
            if (_quickAmountReady || guna2DataGridView1 == null) return;
            _quickAmountReady = true;

            try
            {
                guna2DataGridView1.ReadOnly = false;
                try { guna2DataGridView1.ThemeStyle.ReadOnly = false; } catch { }

                foreach (DataGridViewColumn col in guna2DataGridView1.Columns)
                {
                    bool editable = col.Name == "dgvAmount" || col.Name == "dgvCredit";
                    col.ReadOnly = !editable;
                }

                if (guna2DataGridView1.Columns.Contains("dgvAmount"))
                {
                    var a = guna2DataGridView1.Columns["dgvAmount"];
                    a.ReadOnly = false;
                    a.DefaultCellStyle.Format = "N0";
                    a.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    a.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    a.DefaultCellStyle.BackColor = Color.FromArgb(40, 48, 80);
                    a.HeaderText = "Amount ✎";
                }
                if (guna2DataGridView1.Columns.Contains("dgvCredit"))
                {
                    var c = guna2DataGridView1.Columns["dgvCredit"];
                    c.ReadOnly = false;
                    c.DefaultCellStyle.Format = "N0";
                    c.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
                    c.DefaultCellStyle.ForeColor = Color.Red;
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    c.DefaultCellStyle.BackColor = Color.FromArgb(55, 35, 45);
                    c.HeaderText = "Credit ✎";
                }

                // Poori row highlight — Amount/Credit pe kaunsi row clear dikhe
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

                guna2DataGridView1.CellEndEdit -= QuickAmount_CellEndEdit;
                guna2DataGridView1.CellEndEdit += QuickAmount_CellEndEdit;
                guna2DataGridView1.CellBeginEdit -= QuickAmount_CellBeginEdit;
                guna2DataGridView1.CellBeginEdit += QuickAmount_CellBeginEdit;
                guna2DataGridView1.KeyDown -= QuickAmount_KeyDown;
                guna2DataGridView1.KeyDown += QuickAmount_KeyDown;
                guna2DataGridView1.CellEnter -= QuickAmount_CellEnter;
                guna2DataGridView1.CellEnter += QuickAmount_CellEnter;
                guna2DataGridView1.RowPrePaint -= QuickAmount_RowPrePaint;
                guna2DataGridView1.RowPrePaint += QuickAmount_RowPrePaint;
                guna2DataGridView1.EditingControlShowing -= QuickAmount_EditingControlShowing;
                guna2DataGridView1.EditingControlShowing += QuickAmount_EditingControlShowing;
            }
            catch { }
        }

        /// <summary>Amount/Credit type karte waqt live commas (3,00,000) — font bhi Amount jaisa.</summary>
        private void QuickAmount_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (_moneyEditBox != null)
            {
                _moneyEditBox.TextChanged -= MoneyEdit_TextChanged;
                _moneyEditBox.KeyPress -= MoneyEdit_KeyPress;
                _moneyEditBox = null;
            }

            if (guna2DataGridView1.CurrentCell == null) return;
            string col = guna2DataGridView1.Columns[guna2DataGridView1.CurrentCell.ColumnIndex].Name;
            if (col != "dgvAmount" && col != "dgvCredit") return;

            var tb = e.Control as TextBox;
            if (tb == null) return;

            _moneyEditBox = tb;
            tb.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold);
            tb.ForeColor = col == "dgvCredit" ? Color.Red : Color.White;
            tb.TextAlign = HorizontalAlignment.Right;
            tb.KeyPress -= MoneyEdit_KeyPress;
            tb.KeyPress += MoneyEdit_KeyPress;
            tb.TextChanged -= MoneyEdit_TextChanged;
            tb.TextChanged += MoneyEdit_TextChanged;

            // pehle se value ho to comma format
            try { ApplyLiveCommaFormat(tb); } catch { }
        }

        private void MoneyEdit_KeyPress(object sender, KeyPressEventArgs e)
        {
            // digits + control keys; comma auto
            if (char.IsControl(e.KeyChar)) return;
            if (char.IsDigit(e.KeyChar)) return;
            e.Handled = true;
        }

        private void MoneyEdit_TextChanged(object sender, EventArgs e)
        {
            if (_formattingMoneyEdit) return;
            var tb = sender as TextBox;
            if (tb == null) return;
            try { ApplyLiveCommaFormat(tb); } catch { }
        }

        private void ApplyLiveCommaFormat(TextBox tb)
        {
            if (tb == null || tb.IsDisposed) return;
            if (_formattingMoneyEdit) return;

            string raw = tb.Text ?? "";
            int sel = tb.SelectionStart;
            int digitsBeforeCursor = 0;
            for (int i = 0; i < raw.Length && i < sel; i++)
            {
                if (char.IsDigit(raw[i])) digitsBeforeCursor++;
            }

            var digits = new System.Text.StringBuilder();
            foreach (char ch in raw)
            {
                if (char.IsDigit(ch)) digits.Append(ch);
            }

            string digitStr = digits.ToString();
            string formatted;
            if (string.IsNullOrEmpty(digitStr))
                formatted = "";
            else if (decimal.TryParse(digitStr, NumberStyles.None, CultureInfo.InvariantCulture, out decimal val))
                // 3,00,000 (lac) style — type karte waqt clear
                formatted = val.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
            else
                formatted = digitStr;

            if (string.Equals(tb.Text, formatted, StringComparison.Ordinal)) return;

            _formattingMoneyEdit = true;
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
                _formattingMoneyEdit = false;
            }
        }

        private void QuickAmount_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            _highlightRow = e.RowIndex;
            try
            {
                guna2DataGridView1.Rows[e.RowIndex].Selected = true;
                guna2DataGridView1.InvalidateRow(e.RowIndex);
            }
            catch { }
        }

        private void QuickAmount_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= guna2DataGridView1.Rows.Count) return;
            if (e.RowIndex != _highlightRow) return;
            try
            {
                // VIP gold border strip — current Amount/Credit row
                Rectangle bounds = e.RowBounds;
                using (var pen = new Pen(Color.FromArgb(212, 175, 55), 2))
                    e.Graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            }
            catch { }
        }

        private void QuickAmount_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) { e.Cancel = true; return; }
            string name = guna2DataGridView1.Columns[e.ColumnIndex].Name;
            if (name != "dgvAmount" && name != "dgvCredit")
            {
                e.Cancel = true;
                return;
            }
            _highlightRow = e.RowIndex;
            try { guna2DataGridView1.Rows[e.RowIndex].Selected = true; } catch { }
        }

        private void QuickAmount_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_savingQuickAmount) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string colName = guna2DataGridView1.Columns[e.ColumnIndex].Name;
            if (colName != "dgvAmount" && colName != "dgvCredit") return;

            try
            {
                SaveQuickAmountCreditRow(e.RowIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Amount/Credit save nahi hua:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Enter: Amount → Credit same row; Credit → next row Amount.
        /// F2: edit current Amount/Credit cell.
        /// </summary>
        private void QuickAmount_KeyDown(object sender, KeyEventArgs e)
        {
            if (guna2DataGridView1 == null || guna2DataGridView1.CurrentCell == null) return;
            if (guna2DataGridView1.IsCurrentCellInEditMode) return;

            string col = guna2DataGridView1.Columns[guna2DataGridView1.CurrentCell.ColumnIndex].Name;

            if (e.KeyCode == Keys.F2 && (col == "dgvAmount" || col == "dgvCredit"))
            {
                e.Handled = true;
                try { guna2DataGridView1.BeginEdit(true); } catch { }
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            if (col != "dgvAmount" && col != "dgvCredit") return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            int row = guna2DataGridView1.CurrentCell.RowIndex;
            _highlightRow = row;
            try
            {
                if (col == "dgvAmount" && guna2DataGridView1.Columns.Contains("dgvCredit"))
                {
                    guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[row].Cells["dgvCredit"];
                    guna2DataGridView1.Rows[row].Selected = true;
                    guna2DataGridView1.BeginEdit(true);
                }
                else
                {
                    int next = row + 1;
                    if (next < guna2DataGridView1.Rows.Count && !guna2DataGridView1.Rows[next].IsNewRow
                        && guna2DataGridView1.Columns.Contains("dgvAmount"))
                    {
                        _highlightRow = next;
                        guna2DataGridView1.CurrentCell = guna2DataGridView1.Rows[next].Cells["dgvAmount"];
                        guna2DataGridView1.Rows[next].Selected = true;
                        guna2DataGridView1.BeginEdit(true);
                    }
                }
            }
            catch { }
        }

        private void SaveQuickAmountCreditRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= guna2DataGridView1.Rows.Count) return;
            DataGridViewRow row = guna2DataGridView1.Rows[rowIndex];
            if (row.IsNewRow) return;

            if (row.Cells["dgvid"].Value == null || row.Cells["dgvid"].Value == DBNull.Value)
                return;

            int pid = Convert.ToInt32(row.Cells["dgvid"].Value);
            if (pid <= 0) return;

            int customerId = 0;
            if (row.DataBoundItem is DataRowView drvOld
                && drvOld.Row.Table.Columns.Contains("CustomerId")
                && drvOld["CustomerId"] != DBNull.Value)
                customerId = Convert.ToInt32(drvOld["CustomerId"]);

            decimal amount = ParseMoneyCell(row.Cells["dgvAmount"].Value);
            decimal credit = ParseMoneyCell(row.Cells["dgvCredit"].Value);
            decimal rowBalance = amount - credit; // DB PetrolAdd.Balance (row snapshot)

            _savingQuickAmount = true;
            try
            {
                // Sirf Amount + Credit + row Balance — Name/Vehicle/Location/Item touch nahi
                string qry = @"
UPDATE PetrolAdd SET
  Amount = @amount,
  Credit = @credit,
  Balance = @balance,
  SyncDirty = 1,
  UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ','now')
WHERE pid = @pid";

                var ht = new Hashtable
                {
                    { "@amount", amount },
                    { "@credit", credit },
                    { "@balance", rowBalance },
                    { "@pid", pid }
                };

                int r = MainClass.DataInsertUpdateDelete(qry, ht);
                if (r <= 0) return;

                // Bound DataRow — Amount/Credit only (cumulative Balance mat mitao)
                if (row.DataBoundItem is DataRowView drv)
                {
                    if (drv.Row.Table.Columns.Contains("Amount"))
                        drv["Amount"] = amount;
                    if (drv.Row.Table.Columns.Contains("CreditVal"))
                        drv["CreditVal"] = credit;
                    else if (drv.Row.Table.Columns.Contains("Credit"))
                        drv["Credit"] = credit;
                }

                row.Cells["dgvAmount"].Value = amount;
                row.Cells["dgvCredit"].Value = credit;

                // VIP Running Balance — DB se full-history cumulative (credit rows bhi)
                if (customerId > 0)
                    RefreshVipRunningBalancesForCustomer(customerId);
                else
                    RefreshVipRunningBalanceForPid(pid, rowIndex);

                try { CalculateTotalLitter(); } catch { }
            }
            finally
            {
                _savingQuickAmount = false;
            }
        }

        /// <summary>
        /// Visible grid rows for this customer — Opening (SQL full history before first row)
        /// + grid Amount − Credit chain. Edit ke baad jo Amount/Credit dikhe wahi RB mein.
        /// </summary>
        private void RefreshVipRunningBalancesForCustomer(int customerId)
        {
            if (customerId <= 0 || guna2DataGridView1 == null) return;

            var rows = new List<int>();
            for (int i = 0; i < guna2DataGridView1.Rows.Count; i++)
            {
                DataGridViewRow gr = guna2DataGridView1.Rows[i];
                if (gr.IsNewRow) continue;
                if (!(gr.DataBoundItem is DataRowView drv)) continue;
                if (!drv.Row.Table.Columns.Contains("CustomerId") || drv["CustomerId"] == DBNull.Value)
                    continue;
                if (Convert.ToInt32(drv["CustomerId"]) != customerId) continue;
                if (gr.Cells["dgvid"].Value == null || gr.Cells["dgvid"].Value == DBNull.Value) continue;
                if (Convert.ToInt32(gr.Cells["dgvid"].Value) <= 0) continue;
                rows.Add(i);
            }

            if (rows.Count == 0) return;

            DataGridViewRow first = guna2DataGridView1.Rows[rows[0]];
            int firstPid = Convert.ToInt32(first.Cells["dgvid"].Value);
            DateTime firstDate = DateTime.Today;
            if (first.DataBoundItem is DataRowView drvFirst
                && drvFirst.Row.Table.Columns.Contains("Date")
                && drvFirst["Date"] != DBNull.Value)
            {
                DateTime.TryParse(drvFirst["Date"].ToString(), out firstDate);
            }

            // Opening = VIP running BEFORE first visible row (full history, is row include nahi)
            decimal run = FetchVipRunningBalanceBefore(customerId, firstDate, firstPid);

            foreach (int rowIdx in rows)
            {
                DataGridViewRow gr = guna2DataGridView1.Rows[rowIdx];
                decimal amount = ParseMoneyCell(gr.Cells["dgvAmount"].Value);
                decimal credit = ParseMoneyCell(gr.Cells["dgvCredit"].Value);
                run += amount - credit;

                if (gr.DataBoundItem is DataRowView drv)
                {
                    if (drv.Row.Table.Columns.Contains("Balance"))
                        drv["Balance"] = run;
                    if (drv.Row.Table.Columns.Contains("TotalBalance"))
                        drv["TotalBalance"] = run;
                }
                if (guna2DataGridView1.Columns.Contains("dgvTotalBalance"))
                    gr.Cells["dgvTotalBalance"].Value = run;
                if (guna2DataGridView1.Columns.Contains("dgvBalance"))
                    gr.Cells["dgvBalance"].Value = run;
            }
        }

        private void RefreshVipRunningBalanceForPid(int pid, int rowIndex)
        {
            int customerId = 0;
            if (rowIndex >= 0 && rowIndex < guna2DataGridView1.Rows.Count
                && guna2DataGridView1.Rows[rowIndex].DataBoundItem is DataRowView drv
                && drv.Row.Table.Columns.Contains("CustomerId")
                && drv["CustomerId"] != DBNull.Value)
                customerId = Convert.ToInt32(drv["CustomerId"]);

            if (customerId > 0)
                RefreshVipRunningBalancesForCustomer(customerId);
            else
            {
                // Fallback: single-pid SQL (Litter×Rate = Amount column)
                var map = FetchVipRunningBalances(new List<int> { pid });
                if (!map.TryGetValue(pid, out decimal run)) return;
                if (rowIndex < 0 || rowIndex >= guna2DataGridView1.Rows.Count) return;

                DataGridViewRow gr = guna2DataGridView1.Rows[rowIndex];
                if (gr.DataBoundItem is DataRowView drv2)
                {
                    if (drv2.Row.Table.Columns.Contains("Balance"))
                        drv2["Balance"] = run;
                    if (drv2.Row.Table.Columns.Contains("TotalBalance"))
                        drv2["TotalBalance"] = run;
                }
                if (guna2DataGridView1.Columns.Contains("dgvTotalBalance"))
                    gr.Cells["dgvTotalBalance"].Value = run;
            }
        }

        /// <summary>VIP running balance BEFORE (date,pid) — same formula as Amount/Credit columns.</summary>
        private static decimal FetchVipRunningBalanceBefore(int customerId, DateTime beforeDate, int beforePid)
        {
            const string sql = @"
SELECT IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN 0
             WHEN IFNULL(p2.Litter, 0) > 0 AND IFNULL(p2.Rate, 0) > 0
               THEN CAST(IFNULL(p2.Litter, 0) AS REAL) * CAST(IFNULL(p2.Rate, 0) AS REAL) + IFNULL(p2.Advance, 0)
             ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
           END
         ), 0)
       - IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN
               CASE
                 WHEN IFNULL(p2.Credit, 0) <> 0 THEN IFNULL(p2.Credit, 0)
                 ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
               END
             ELSE IFNULL(p2.Credit, 0)
           END
         ), 0)
FROM PetrolAdd p2
WHERE p2.CustomerId = @cid
  AND (
        date(p2.Date) < date(@d)
        OR (date(p2.Date) = date(@d) AND p2.pid < @pid)
      )";
            try
            {
                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@cid", customerId);
                        cmd.Parameters.AddWithValue("@d", beforeDate.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@pid", beforePid);
                        object o = cmd.ExecuteScalar();
                        if (o == null || o == DBNull.Value) return 0m;
                        return Convert.ToDecimal(o);
                    }
                }
            }
            catch { return 0m; }
        }

        private static Dictionary<int, decimal> FetchVipRunningBalances(List<int> pids)
        {
            var map = new Dictionary<int, decimal>();
            if (pids == null || pids.Count == 0) return map;

            // Same VIP formula as FetchPetrolData / Amount column — Litter×Rate, else Amount; Credit minus
            const string runSql = @"
(SELECT IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN 0
             WHEN IFNULL(p2.Litter, 0) > 0 AND IFNULL(p2.Rate, 0) > 0
               THEN CAST(IFNULL(p2.Litter, 0) AS REAL) * CAST(IFNULL(p2.Rate, 0) AS REAL) + IFNULL(p2.Advance, 0)
             ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
           END
         ), 0)
       - IFNULL(SUM(
           CASE
             WHEN IFNULL(p2.IsInitialEntry, 1) = 0 THEN
               CASE
                 WHEN IFNULL(p2.Credit, 0) <> 0 THEN IFNULL(p2.Credit, 0)
                 ELSE IFNULL(p2.Amount, 0) + IFNULL(p2.Advance, 0)
               END
             ELSE IFNULL(p2.Credit, 0)
           END
         ), 0)
 FROM PetrolAdd p2
 WHERE p2.CustomerId = p.CustomerId
   AND (
         date(p2.Date) < date(p.Date)
         OR (date(p2.Date) = date(p.Date) AND p2.pid <= p.pid)
       ))";

            var sb = new StringBuilder();
            sb.Append("SELECT p.pid, CAST(").Append(runSql).Append(" AS DECIMAL(18,3)) AS RunBal FROM PetrolAdd p WHERE p.pid IN (");
            for (int i = 0; i < pids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("@p").Append(i);
            }
            sb.Append(')');

            try
            {
                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(sb.ToString(), con))
                    {
                        for (int i = 0; i < pids.Count; i++)
                            cmd.Parameters.AddWithValue("@p" + i, pids[i]);

                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                int pid = Convert.ToInt32(r["pid"]);
                                decimal bal = r["RunBal"] == DBNull.Value ? 0m : Convert.ToDecimal(r["RunBal"]);
                                map[pid] = bal;
                            }
                        }
                    }
                }
            }
            catch { }

            return map;
        }

        private static decimal ParseMoneyCell(object value)
        {
            if (value == null || value == DBNull.Value) return 0m;
            string s = Convert.ToString(value) ?? "";
            s = s.Replace(",", "").Trim();
            if (string.IsNullOrEmpty(s)) return 0m;
            decimal d;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                return d;
            return 0m;
        }
    }
}
