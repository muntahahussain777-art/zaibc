using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService
{
    public static class ReportWindowHelper
    {
        private static readonly Dictionary<string, Form> OpenReports = new Dictionary<string, Form>();

        public static void ShowOnce<T>(Func<T> createForm) where T : Form
        {
            ShowInternal(typeof(T).FullName, typeof(T).Name, createForm, refreshIfOpen: false);
        }

        /// <summary>
        /// Same as ShowOnce but with an explicit window key (Daily VIP vs Dealer VIP).
        /// reopenIfOpen=true loads fresh filter data each click.
        /// </summary>
        public static void ShowOnceKeyed<T>(string windowKey, Func<T> createForm, bool reopenIfOpen = true) where T : Form
        {
            ShowInternal(
                string.IsNullOrWhiteSpace(windowKey) ? typeof(T).FullName : windowKey.Trim(),
                typeof(T).Name,
                createForm,
                refreshIfOpen: reopenIfOpen);
        }

        private static void ShowInternal<T>(string key, string accessFormKey, Func<T> createForm, bool refreshIfOpen)
            where T : Form
        {
            if (!RoleAccessService.CanAccess(accessFormKey ?? typeof(T).Name))
            {
                MessageBox.Show("Aap ke role ki ijazat nahi hai is report par.", "Access Denied",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (OpenReports.TryGetValue(key, out Form existing) && existing != null && !existing.IsDisposed)
            {
                if (!refreshIfOpen)
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                        existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                    existing.Activate();
                    return;
                }

                try { existing.Close(); } catch { }
                OpenReports.Remove(key);
            }

            OpenReports.Remove(key);

            try
            {
                T frm = createForm();
                OpenReports[key] = frm;
                AppWindowHelper.EnableTopLevelWindow(frm);

                frm.FormClosed += (s, e) =>
                {
                    if (OpenReports.TryGetValue(key, out Form f) && ReferenceEquals(f, frm))
                        OpenReports.Remove(key);
                };

                Form owner = frmMain.instance;
                if (owner != null && !owner.IsDisposed)
                    frm.Show(owner);
                else
                    frm.Show();
            }
            catch (Exception ex)
            {
                OpenReports.Remove(key);
                MessageBox.Show("Report open nahi ho saki:\n" + ex.Message, "Report Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
