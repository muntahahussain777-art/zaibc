using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZaibPetroleumService.Services;

namespace ZaibPetroleumService.Model
{
    public partial class DealerAdd : SampleAdd
    {
        public DealerAdd()
        {
            InitializeComponent();
        }

        public int id = 0;
        private decimal DAmount = 0;  // Variable to store DAmount from database
        private bool _savedInSession;

        private void DealerAdd_Load(object sender, EventArgs e)
        {
            txtmobile.TextChanged += new EventHandler(txtmobile_TextChanged); // Bind event

            if (id > 0) // Agar id zero se zyada hai to existing customer data ko load karo
            {
                LoadCustomerData(); // Naya method call karte hain
            }
            else
            {
                txtdate.Value = DateTime.Now;
            }

            this.FormClosing -= DealerAdd_FormClosing;
            this.FormClosing += DealerAdd_FormClosing;
        }

        private void DealerAdd_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_savedInSession && DialogResult == DialogResult.None)
                DialogResult = DialogResult.OK;
        }

        private void LoadCustomerData()
        {
            // Query to fetch dealer data by id
            string qry = "SELECT DealerName, DDAmount, DAmount, Date FROM AddDealer WHERE Did = @id";
            Hashtable ht = new Hashtable();
            ht.Add("@id", id);

            DataTable dt = MainClass.ExecuteSelectQuery(qry, ht); // Updated method name
            if (dt.Rows.Count > 0)
            {
                txtname.Text = dt.Rows[0]["DealerName"].ToString();
                txtmobile.Text = dt.Rows[0]["DDAmount"].ToString();
                txtdate.Text = dt.Rows[0]["Date"].ToString();

                // Fetch the DAmount and store it in the variable
                DAmount = Convert.ToDecimal(dt.Rows[0]["DAmount"]);
            }
        }

        private void txtmobile_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Allow only digits, backspace, and hyphen
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back && e.KeyChar != '-')
            {
                e.Handled = true;
            }

            // Check if input length is 11 characters
            if (txtmobile.Text.Length == 12 && e.KeyChar != (char)Keys.Back)
            {
                e.Handled = true;
            }

            // Automatically add hyphen after 4th digit
            if (txtmobile.Text.Length == 4 && e.KeyChar != (char)Keys.Back)
            {
                txtmobile.Text += "-";
                txtmobile.SelectionStart = txtmobile.Text.Length; // Set cursor to end
            }
        }

        public override void btnSave_Click(object sender, EventArgs e)
        {
            // Validation
            if (string.IsNullOrWhiteSpace(txtname.Text))
            {
                ErrorFormMessage customMessageBox = new ErrorFormMessage("Name is required.", "غلطی");
                customMessageBox.ShowDialog();
                return;
            }

            // Validate and parse the date
            DateTime dateValue;
            if (!DateTime.TryParse(txtdate.Text, out dateValue))
            {
                MessageBox.Show("Invalid date format. Please use yyyy-MM-dd format.");
                return;
            }

            decimal newDd = string.IsNullOrWhiteSpace(txtmobile.Text) ? 0 : Convert.ToDecimal(txtmobile.Text.Replace(",", ""));
            decimal newD = DAmount;
            string dateText = dateValue.ToString("yyyy-MM-dd");
            int saveDid = id;
            string qry = saveDid == 0
                ? "INSERT INTO AddDealer (DealerName, DDAmount, DAmount, Date) VALUES (@name, @Amount, @DAmount, @date)"
                : "UPDATE AddDealer SET DealerName = @name, DDAmount = @Amount, DAmount = @DAmount, Date = @date WHERE Did = @id";
            var ht = new Hashtable
            {
                { "@id", saveDid },
                { "@name", txtname.Text },
                { "@Amount", newDd },
                { "@DAmount", newD },
                { "@date", dateText }
            };

            bool ok = MainClass.RunInTransaction((conn, tx) =>
            {
                decimal oldDd = 0;
                decimal oldD = 0;
                if (saveDid > 0)
                {
                    DataRow old = LocalPersistence.ReadRow("AddDealer", "Did", saveDid, conn, tx);
                    if (old == null) return false;
                    oldDd = old["DDAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(old["DDAmount"]);
                    oldD = old["DAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(old["DAmount"]);
                }
                if (MainClass.ExecInTx(qry, ht, conn, tx) <= 0) return false;
                int savedId = saveDid;
                if (savedId == 0)
                {
                    object rid = LocalPersistence.Scalar("SELECT CAST(last_insert_rowid() AS INTEGER)", null, conn, tx);
                    if (rid == null || rid == DBNull.Value) return false;
                    savedId = Convert.ToInt32(rid);
                }
                DataRow dealer = LocalPersistence.ReadRow("AddDealer", "Did", savedId, conn, tx);
                if (dealer == null) return false;
                string dealerSyncId = dealer["SyncId"]?.ToString();
                if (string.IsNullOrWhiteSpace(dealerSyncId)) return false;

                if (saveDid > 0)
                {
                    double ddDelta = (double)(newDd - oldDd);
                    double dDelta = (double)(newD - oldD);
                    if (Math.Abs(ddDelta) > 1e-9 || Math.Abs(dDelta) > 1e-9)
                    {
                        if (!SupabaseSyncService.EnqueueDealerBalanceOpInTx(
                                conn, tx, savedId, dealerSyncId, ddDelta, dDelta, dateText, "manual", null))
                            return false;
                    }
                }
                else if (Math.Abs((double)newDd) > 1e-9 || Math.Abs((double)newD) > 1e-9)
                {
                    if (!SupabaseSyncService.EnqueueDealerBalanceOpInTx(
                            conn, tx, savedId, dealerSyncId, (double)newDd, (double)newD, dateText,
                            "opening", "opening:" + dealerSyncId))
                        return false;
                }
                return true;
            });

            if (ok)
            {
                CustomeMessage customMessageBox = new CustomeMessage("Saved Successfully", "Save");
                customMessageBox.ShowDialog();
                MainClass.Enable_reset_keep_date(this, txtdate);
                id = 0;
                _savedInSession = true;
            }
            else
            {
                MessageBox.Show("Error saving data.");
            }
        }

        public override void btnDel_Click(object sender, EventArgs e)
        {
            if (id > 0)
            {
                // Pehle check karo ke DieselLedgerCredit mein entry hai ya nahi
                string checkQuery = "SELECT COUNT(*) FROM DieselLedgerCredit WHERE Did = @id";
                Hashtable checkHt = new Hashtable();
                checkHt.Add("@id", id);

                int count = Convert.ToInt32(MainClass.ExecuteScalar(checkQuery, checkHt)); // Count nikalne ke liye

                if (count > 0) // Agar entry mojood hai
                {
                    ErrorFormMessage errorMessage = new ErrorFormMessage("یہ ڈیلر ڈیلر پے آؤٹ میں موجود ہے، اسے ڈیلیٹ نہیں کیا جا سکتا۔", "غلطی");
                    errorMessage.ShowDialog();
                    return; // Delete nahi hoga
                }

                // Agar DieselLedgerCredit mein entry nahi hai to delete karo
                int r = MainClass.DeleteWithTombstone("AddDealer", "Did", id, "zaib_dealers");
                if (r > 0)
                {
                    CustomeMessage customMessageBox = new CustomeMessage("ڈیلیٹ کامیابی سے ہو گیا۔", "ڈیلیٹ");
                    customMessageBox.ShowDialog();
                    MainClass.Enable_reset_keep_date(this, txtdate);
                    id = 0;
                }
                else
                {
                    MessageBox.Show("ڈیٹا ڈیلیٹ کرنے میں خرابی ہوئی۔");
                }
            }
        }
        private void txtmobile_TextChanged(object sender, EventArgs e)
        {
            // Remove any existing commas
            string currentText = txtmobile.Text.Replace(",", "");

            // Check if the current text is a valid number
            if (decimal.TryParse(currentText, out decimal value))
            {
                // Format the value with commas and set it back to the TextBox
                txtmobile.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", value);

                // Set the caret position to the end of the text
                txtmobile.SelectionStart = txtmobile.Text.Length;
            }
        }

        private void txtmobile_KeyPress_1(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
            {
                e.Handled = true;
                CustomeMessage customMessageBox = new CustomeMessage("آپ یہاں صرف نمبر درج کر سکتے ہیں۔", "غلطی");
                customMessageBox.ShowDialog();
            }
            if (e.KeyChar == '.' && (sender as TextBox).Text.Contains("."))
            {
                e.Handled = true;
            }
        }
    }
}
