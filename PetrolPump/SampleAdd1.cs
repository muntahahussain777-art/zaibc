using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService
{
    public partial class SampleAdd1 : Sample
    {
        private bool _saveBusy;

        public SampleAdd1()
        {
            InitializeComponent();
            KeyPreview = true;
            if (btnSave != null)
            {
                btnSave.Click -= btnSave_Click;
                btnSave.Click += GuardedSaveClick;
            }
            Shown += SampleAdd1_AttachNameSuggest;
        }

        private void SampleAdd1_AttachNameSuggest(object sender, EventArgs e)
        {
            try { NameSuggestHelper.AttachNameBoxes(this); }
            catch { }
        }

        private void GuardedSaveClick(object sender, EventArgs e)
        {
            if (_saveBusy) return;
            _saveBusy = true;
            if (btnSave != null)
                btnSave.Enabled = false;
            try
            {
                btnSave_Click(sender, e);
            }
            finally
            {
                var t = new Timer { Interval = 800 };
                t.Tick += (s, ev) =>
                {
                    t.Stop();
                    t.Dispose();
                    _saveBusy = false;
                    if (btnSave != null && !btnSave.IsDisposed)
                        btnSave.Enabled = true;
                };
                t.Start();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            if ((keyData == (Keys.Control | Keys.S) || keyData == (Keys.Alt | Keys.S)) &&
                btnSave != null && btnSave.Enabled && btnSave.Visible)
            {
                GuardedSaveClick(btnSave, EventArgs.Empty);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public virtual void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        public virtual void btnDel_Click(object sender, EventArgs e)
        {

        }
        protected virtual void btnSave_Click(object sender, EventArgs e)
        {

        }
    }
}
