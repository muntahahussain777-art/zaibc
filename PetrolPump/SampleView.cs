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
    public partial class SampleView : Sample
    {
        public SampleView()
        {
            InitializeComponent();
            KeyPreview = true;
            Shown += SampleView_AttachNameSuggest;
        }

        private void SampleView_AttachNameSuggest(object sender, EventArgs e)
        {
            try
            {
                if (txtSearch != null && !txtSearch.IsDisposed)
                    NameSuggestHelper.AttachSearchBox(this, txtSearch);
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (NameSuggestHelper.TryHandleKey(keyData))
                return true;

            if ((keyData == (Keys.Control | Keys.N) || keyData == (Keys.Alt | Keys.N)) &&
                btnAdd != null && btnAdd.Enabled && btnAdd.Visible)
            {
                btnAdd_Click(btnAdd, EventArgs.Empty);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

      

        public virtual void btnAdd_Click(object sender, EventArgs e)
        {

        }

        public virtual void txtSearch_TextChanged_1(object sender, EventArgs e)
        {

        }
    }
}
