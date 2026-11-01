using System;
using System.Data;

namespace ZaibPetroleumService.Model
{
    public partial class frmDiselAdd
    {
        private void ResetFormKeepSelectedDate()
        {
            DateTime selectedDate = txtdate.Value;
            MainClass.Enable_reset(this);
            txtdate.Value = selectedDate;
        }

        /// <summary>
        /// Save/Delete ke baad Name + Date (+ In/Out) keep.
        /// Vehicle / Amount / Credit clear — next vehicle same customer pe; Name change nahi.
        /// </summary>
        private void ResetAfterSaveKeepNameAndDate()
        {
            DateTime keepDate = DateTime.Now;
            try { if (txtdate != null) keepDate = txtdate.Value; } catch { }

            string keepInOut = "In";
            try
            {
                if (cbSaleInOut != null && cbSaleInOut.SelectedItem != null)
                    keepInOut = (cbSaleInOut.SelectedItem.ToString() ?? "In").Trim();
            }
            catch { }

            // Name restore — Enable_reset Name clear kar sakta hai
            object keepNameId = null;
            string keepNameText = "";
            bool hadName = false;
            try
            {
                if (cbName != null && cbName.SelectedValue != null && cbName.SelectedValue != DBNull.Value
                    && !(cbName.SelectedValue is DataRowView)
                    && int.TryParse(cbName.SelectedValue.ToString(), out int kid) && kid > 0)
                {
                    keepNameId = cbName.SelectedValue;
                    keepNameText = (cbName.Text ?? "").Trim();
                    hadName = true;
                }
                else if (txtName != null && !string.IsNullOrWhiteSpace(txtName.Text))
                {
                    keepNameText = txtName.Text.Trim();
                    hadName = true;
                }
            }
            catch { }

            MainClass.Enable_reset(this);
            try { ResetVipFieldsAfterSave(); } catch { }

            try { if (txtdate != null) txtdate.Value = keepDate; } catch { }

            try
            {
                _suppressNameChange = true;
                if (hadName)
                {
                    if (txtName != null) txtName.Text = keepNameText;
                    if (cbName != null && keepNameId != null)
                    {
                        try { cbName.SelectedValue = keepNameId; }
                        catch
                        {
                            try
                            {
                                int idx = cbName.FindStringExact(keepNameText);
                                if (idx >= 0) cbName.SelectedIndex = idx;
                            }
                            catch { }
                        }
                    }
                    _namePickedByUser = true;
                }
                else
                {
                    ClearCustomerNameSelection();
                    _namePickedByUser = false;
                }

                _lastAppliedVehicle = "";
                if (cbSaleInOut != null)
                {
                    int idx = cbSaleInOut.FindStringExact(
                        string.Equals(keepInOut, "Out", StringComparison.OrdinalIgnoreCase) ? "Out" : "In");
                    if (idx >= 0) cbSaleInOut.SelectedIndex = idx;
                }
            }
            catch { }
            finally
            {
                _suppressNameChange = false;
            }

            if (txtadvance != null) txtadvance.Text = "0";
            if (txtrecipt != null) txtrecipt.Text = "";
            try
            {
                if (txtVehicle != null) txtVehicle.Clear();
                if (txtlitter != null) txtlitter.Clear();
                if (txtrate != null) txtrate.Clear();
                if (txtamount != null) txtamount.Clear();
                if (txtbalance != null) txtbalance.Clear();
                if (txtcredit != null) txtcredit.Clear();
                if (txtnote != null) txtnote.Clear();
            }
            catch { }

            try { RefreshCustomerCreditDisplay(); } catch { }
        }
    }
}
