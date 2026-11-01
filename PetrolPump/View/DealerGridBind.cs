using System;
using System.Collections.Generic;
using System.Data;
using System.Windows.Forms;
using Guna.UI2.WinForms;

namespace ZaibPetroleumService.View
{
    /// <summary>
    /// Guna2DataGridView duplicates columns/rows when AutoGenerate re-runs on Show.
    /// After save the form is already shown so it looks correct; leave/return creates a
    /// new form and Theme/TextChanged bind again. Keep designer dgv* columns only.
    /// </summary>
    internal static class DealerGridBind
    {
        public static void AttachDgvOnly(Guna2DataGridView gv)
        {
            if (gv == null) return;
            ForceNoAuto(gv);
            gv.DataBindingComplete -= DgvOnlyComplete;
            gv.DataBindingComplete += DgvOnlyComplete;
            gv.VisibleChanged -= DgvOnlyVisible;
            gv.VisibleChanged += DgvOnlyVisible;
        }

        public static void AttachKeep(Guna2DataGridView gv, HashSet<string> keep)
        {
            if (gv == null) return;
            ForceNoAuto(gv);
            gv.Tag = keep;
            gv.DataBindingComplete -= KeepComplete;
            gv.DataBindingComplete += KeepComplete;
            gv.VisibleChanged -= KeepVisible;
            gv.VisibleChanged += KeepVisible;
        }

        public static void BindDgvOnly(Guna2DataGridView gv, DataTable dt)
        {
            if (gv == null) return;
            gv.SuspendLayout();
            try
            {
                ForceNoAuto(gv);
                gv.DataSource = null;
                StripDgvOnly(gv);
                gv.DataSource = dt ?? new DataTable();
                StripDgvOnly(gv);
                MainClass.SrNo(gv);
            }
            finally
            {
                gv.ResumeLayout(true);
            }
        }

        public static void ForceNoAuto(Guna2DataGridView gv)
        {
            if (gv == null || gv.IsDisposed) return;
            gv.AutoGenerateColumns = false;
            gv.AllowUserToAddRows = false;
        }

        public static void StripDgvOnly(Guna2DataGridView gv)
        {
            ForceNoAuto(gv);
            if (gv == null || gv.IsDisposed) return;
            for (int i = gv.Columns.Count - 1; i >= 0; i--)
            {
                string name = gv.Columns[i].Name ?? "";
                if (!name.StartsWith("dgv", StringComparison.OrdinalIgnoreCase))
                    gv.Columns.RemoveAt(i);
            }
        }

        public static void StripKeep(Guna2DataGridView gv, HashSet<string> keep)
        {
            ForceNoAuto(gv);
            if (gv == null || gv.IsDisposed || keep == null) return;
            for (int i = gv.Columns.Count - 1; i >= 0; i--)
            {
                string name = gv.Columns[i].Name ?? "";
                if (!keep.Contains(name))
                    gv.Columns.RemoveAt(i);
            }
        }

        public static void Map(Guna2DataGridView gv, DataTable dt, string gridCol, string dataCol, bool visible = true)
        {
            if (gv == null || !gv.Columns.Contains(gridCol)) return;
            var col = gv.Columns[gridCol];
            col.DataPropertyName = dt != null && dt.Columns.Contains(dataCol) ? dataCol : string.Empty;
            col.Visible = visible;
        }

        private static void DgvOnlyComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            StripDgvOnly(sender as Guna2DataGridView);
        }

        private static void DgvOnlyVisible(object sender, EventArgs e)
        {
            var gv = sender as Guna2DataGridView;
            if (gv != null && gv.Visible)
                StripDgvOnly(gv);
        }

        private static void KeepComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            var gv = sender as Guna2DataGridView;
            if (gv != null)
                StripKeep(gv, gv.Tag as HashSet<string>);
        }

        private static void KeepVisible(object sender, EventArgs e)
        {
            var gv = sender as Guna2DataGridView;
            if (gv != null && gv.Visible)
                StripKeep(gv, gv.Tag as HashSet<string>);
        }
    }
}
