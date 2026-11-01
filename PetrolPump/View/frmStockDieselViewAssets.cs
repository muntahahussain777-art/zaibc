using Guna.UI2.WinForms;
using System.Drawing;
using System.Windows.Forms;
using ZaibPetroleumService.Model;

namespace ZaibPetroleumService.View
{
    public partial class frmStockDieselView
    {
        private Guna2Button _btnStockAssets;

        private void SetupStockAssetsButton()
        {
            if (_btnStockAssets != null) return;

            _btnStockAssets = new Guna2Button
            {
                Text = "Assets",
                AutoRoundedCorners = true,
                BorderRadius = 15,
                Size = new Size(95, 33),
                FillColor = Color.FromArgb(0, 150, 136),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold)
            };
            _btnStockAssets.Click += (s, e) => OpenStockAssetsCalc();
            panel1.Controls.Add(_btnStockAssets);
            _btnStockAssets.BringToFront();
            PositionTopActionButtons();
        }

        private void OpenStockAssetsCalc()
        {
            using (var frm = new frmStockAssetsCalc(dtpStart.Value.Date, dtpEnd.Value.Date))
                frm.ShowDialog(FindForm());
        }
    }
}
