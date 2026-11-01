using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZaibPetroleumService.View
{
    public partial class frmStockDieselView
    {
        private void SetupStockActionLayout()
        {
            if (btnAdd != null)
                btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            panel1.Resize -= Panel1_ResizeActions;
            panel1.Resize += Panel1_ResizeActions;
            PositionTopActionButtons();
        }

        private void Panel1_ResizeActions(object sender, EventArgs e)
        {
            PositionTopActionButtons();
            PositionSummaryLabels();
        }

        private void PositionAddButton(int rightPad)
        {
            if (btnAdd == null || panel1 == null)
                return;

            const int addTop = 37;
            const int gap = 8;
            int x = panel1.ClientSize.Width - rightPad - btnAdd.Width;
            btnAdd.Location = new Point(Math.Max(12, x), addTop);

            // Report left of Add2, Add2 left of Add
            x = btnAdd.Left;
            if (_btnStockAdd2 != null)
            {
                _btnStockAdd2.Visible = true;
                x = Math.Max(12, x - gap - _btnStockAdd2.Width);
                _btnStockAdd2.Location = new Point(x, addTop);
                _btnStockAdd2.BringToFront();
            }
            if (_btnStockVipReport != null)
            {
                _btnStockVipReport.Visible = true;
                x = Math.Max(12, x - gap - _btnStockVipReport.Width);
                _btnStockVipReport.Location = new Point(x, addTop);
                _btnStockVipReport.BringToFront();
            }
        }

        private void PositionTopActionButtons()
        {
            if (panel1 == null || dtpStart == null || dtpEnd == null)
                return;

            const int top = 12;
            const int gap = 8;
            const int rightPad = 12;
            const int minDateWidth = 120;

            PositionAddButton(rightPad);

            // Fazool hide; VIP Report alag button se
            if (_btnStockReport != null) _btnStockReport.Visible = false;
            if (_btnStockAssets != null) _btnStockAssets.Visible = false;
            if (_btnStockPdf != null) _btnStockPdf.Visible = false;
            if (btnAverage != null) btnAverage.Visible = false;
            if (comboBox1 != null) comboBox1.Visible = false;

            int rowRight = panel1.ClientSize.Width - rightPad;
            if (_btnStockVipReport != null && _btnStockVipReport.Visible)
                rowRight = _btnStockVipReport.Left - gap;
            else if (_btnStockAdd2 != null)
                rowRight = _btnStockAdd2.Left - gap;
            else if (btnAdd != null)
                rowRight = btnAdd.Left - gap;

            // VIP filters row (y=52): Name / Type / Type2 / Type3 — dates top row
            int comboRight = 560;
            if (cbStockType3Filter != null)
                comboRight = Math.Max(comboRight, cbStockType3Filter.Right + gap);
            else if (cbStockType2Filter != null)
                comboRight = Math.Max(comboRight, cbStockType2Filter.Right + gap);

            int dateRight = Math.Max(comboRight + minDateWidth * 2 + gap, rowRight);
            int availableForDates = dateRight - comboRight;
            int dateWidth = Math.Max(minDateWidth, (availableForDates - gap) / 2);

            dtpStart.Width = dateWidth;
            dtpEnd.Width = dateWidth;
            dtpStart.Location = new Point(comboRight, top);
            dtpEnd.Location = new Point(dtpStart.Right + gap, top);

            if (dtpEnd.Right > rowRight && rowRight > comboRight)
            {
                int overflow = dtpEnd.Right - rowRight;
                dtpEnd.Width = Math.Max(minDateWidth, dtpEnd.Width - overflow);
                dtpStart.Width = Math.Max(minDateWidth, dtpStart.Width - overflow / 2);
                dtpStart.Location = new Point(comboRight, top);
                dtpEnd.Location = new Point(dtpStart.Right + gap, top);
            }

            btnAdd?.BringToFront();
            _btnStockAdd2?.BringToFront();
            _btnStockVipReport?.BringToFront();
            cbStockNameFilter?.BringToFront();
            cbStockTypeFilter?.BringToFront();
            cbStockType2Filter?.BringToFront();
            cbStockType3Filter?.BringToFront();
        }
    }
}
