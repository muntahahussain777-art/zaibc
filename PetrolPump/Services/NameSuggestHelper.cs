using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using ZaibPetroleumService.ProjectConnection;

namespace ZaibPetroleumService.Services
{
    /// <summary>
    /// VIP word-match suggest. Form Controls pe kuch add NAHI.
    /// Type "hussain" → "Irtaza Hussain" + "Irtaza Hussain Jamot" until select / full type.
    /// Sync/pull/UI layout unchanged.
    /// </summary>
    public static class NameSuggestHelper
    {
        private enum NameKind
        {
            None,
            Dealer,
            Customer,
            Both,
            Litter,
            Rate,
            /// <summary>Stock Add2 names only (StockPartyName) — AddDealer nahi.</summary>
            StockParty
        }

        private sealed class SuggestPopup : Form
        {
            public readonly ListBox List;
            public bool IsOpen;

            public SuggestPopup()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                Opacity = 1D;
                BackColor = Color.White;
                Size = new Size(280, 160);

                List = new ListBox
                {
                    IntegralHeight = false,
                    TabStop = false,
                    Font = new Font("Segoe UI", 11F, FontStyle.Regular),
                    BackColor = Color.White,
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle,
                    DrawMode = DrawMode.Normal,
                    SelectionMode = SelectionMode.One,
                    HorizontalScrollbar = false
                };
                Controls.Add(List);
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEACTIVATE = 0x0021;
                const int MA_NOACTIVATE = 3;
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return;
                }
                base.WndProc(ref m);
            }

            public void Fill(IList<string> matches)
            {
                List.BeginUpdate();
                try
                {
                    List.Items.Clear();
                    if (matches != null)
                    {
                        foreach (string m in matches)
                        {
                            if (!string.IsNullOrWhiteSpace(m))
                                List.Items.Add(m);
                        }
                    }
                    if (List.Items.Count > 0)
                        List.SelectedIndex = 0;
                }
                finally
                {
                    List.EndUpdate();
                }
            }

            public void ShowNames(IList<string> matches, int x, int y, int w, int h)
            {
                w = Math.Max(w, 220);
                h = Math.Max(h, 48);
                if (!IsHandleCreated)
                    CreateHandle();

                Bounds = new Rectangle(x, y, w, h);
                List.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
                Fill(matches);

                SetVisibleCore(true);
                SetWindowPos(Handle, HWND_TOPMOST, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
                ShowWindow(Handle, SW_SHOWNOACTIVATE);

                List.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
                List.BringToFront();
                List.Refresh();
                IsOpen = true;
            }

            public void HidePopup()
            {
                IsOpen = false;
                SetVisibleCore(false);
            }
        }

        private sealed class SuggestState
        {
            public Control Box;
            public Control Input;
            public NameKind Kind;
            public bool Suppress;
            public SuggestPopup Popup;
        }

        private static readonly ConditionalWeakTable<Control, SuggestState> _states =
            new ConditionalWeakTable<Control, SuggestState>();

        private static SuggestState _active;
        private static readonly object _lock = new object();
        private static List<string> _dealers;
        private static List<string> _customers;
        private static List<string> _stockParties;
        private static List<string> _litters;
        private static List<string> _rates;
        private static DateTime _cacheUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        private const int MaxShow = 18;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;

        public static void AttachSearchBox(Form form, Control searchBox)
        {
            if (form == null || searchBox == null || searchBox.IsDisposed)
                return;
            NameKind kind = DetectKind(form.GetType().Name);
            if (kind == NameKind.None)
                return;
            Attach(searchBox, kind);
        }

        public static void AttachNameBoxes(Form form)
        {
            if (form == null || form.IsDisposed) return;
            NameKind kind = DetectKind(form.GetType().Name);
            if (kind == NameKind.None && !IsLitterRateForm(form.GetType().Name))
                return;
            WalkBoxes(form, kind == NameKind.None ? NameKind.Both : kind);
        }

        public static bool TryHandleKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (_active == null || _active.Popup == null || !_active.Popup.IsOpen)
                return false;
            if (k == Keys.Down || k == Keys.Up || k == Keys.Enter || k == Keys.Escape)
            {
                OnKey(k);
                return true;
            }
            return false;
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _dealers = null;
                _customers = null;
                _stockParties = null;
                _litters = null;
                _rates = null;
                _cacheUtc = DateTime.MinValue;
            }
        }

        private static bool IsLitterRateForm(string formName)
        {
            return formName == "frmDiselAdd" || formName == "frmStockAdd" ||
                   formName == "frmStockDieselAdd";
        }

        private static void WalkBoxes(Control root, NameKind formKind)
        {
            if (root == null) return;
            foreach (Control c in root.Controls)
            {
                string n = c.Name ?? "";
                bool isBox = c is TextBox || c is Guna2TextBox;
                if (isBox)
                {
                    // Litter/rate pe suggest nahi — type karte cursor disturb hota hai.
                    if (LooksLikeLitterBox(n) || LooksLikeRateBox(n))
                    {
                        // skip litter/rate suggest
                    }
                    else if (LooksLikeNameBox(n))
                    {
                        NameKind boxKind = formKind;
                        if (n.IndexOf("Dealer", StringComparison.OrdinalIgnoreCase) >= 0)
                            boxKind = NameKind.Dealer;
                        else if (n.IndexOf("Customer", StringComparison.OrdinalIgnoreCase) >= 0)
                            boxKind = NameKind.Customer;
                        if (boxKind != NameKind.None)
                            Attach(c, boxKind);
                    }
                }
                if (c.HasChildren)
                    WalkBoxes(c, formKind);
            }
        }

        private static bool LooksLikeNameBox(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.Equals("txtname", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtName", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("DealerName", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("CustomerName", StringComparison.OrdinalIgnoreCase) >= 0
                || n.Equals("txtDealer", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtCustomer", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtDealerName1", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtDealerName2", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeLitterBox(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (n.IndexOf("Amount", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return n.Equals("txtlitter", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtLitter", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtAddDisel", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeRateBox(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.Equals("txtrate", StringComparison.OrdinalIgnoreCase)
                || n.Equals("txtRate", StringComparison.OrdinalIgnoreCase);
        }

        private static NameKind DetectKind(string formName)
        {
            if (string.IsNullOrEmpty(formName))
                return NameKind.None;

            switch (formName)
            {
                case "frmDealerNameView":
                case "frmStockView":
                case "frmDieselLedgerView":
                case "FrmDirectDealerPaymentAmountView":
                case "frmDealerAmountCombinedView":
                case "frmDealertoDealerView":
                case "DealerAdd":
                case "frmStockAdd":
                case "frmDieselLedgerDealerAdd":
                case "FrmDirectDealerPaymentAmountAdd":
                case "frmDealertoDealerAdd":
                    return NameKind.Dealer;

                // Stock menu: Add2 / StockPartyName only (AddDealer suggest band)
                case "frmStockDieselView":
                case "frmStockDieselAdd":
                    return NameKind.StockParty;

                case "frmCustomerView":
                case "frmCreditAdjust":
                case "frmDiselView":
                case "frmCustomerToCustomerView":
                case "frmCustomerAdd":
                case "frmCreditAdjustAdd":
                case "frmDiselAdd":
                case "frmCustomerToCustomerAdd":
                    return NameKind.Customer;

                case "frmDieselLedger":
                case "frmDieselLedgerAdd":
                case "frmBankAccountView":
                case "frmBankTransactionAdd":
                    return NameKind.Both;

                default:
                    if (formName.IndexOf("Dealer", StringComparison.OrdinalIgnoreCase) >= 0)
                        return NameKind.Dealer;
                    if (formName.IndexOf("Customer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        formName.IndexOf("Disel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        formName.IndexOf("Credit", StringComparison.OrdinalIgnoreCase) >= 0)
                        return NameKind.Customer;
                    return NameKind.None;
            }
        }

        private static Control FindInnerInput(Control box)
        {
            if (box is TextBox)
                return box;
            foreach (Control c in box.Controls)
            {
                Control inner = FindInnerInput(c);
                if (inner != null)
                    return inner;
            }
            return box;
        }

        private static void Attach(Control box, NameKind kind)
        {
            if (box == null || box.IsDisposed || kind == NameKind.None)
                return;

            SuggestState state;
            if (_states.TryGetValue(box, out state))
            {
                state.Kind = kind;
                return;
            }

            Control input = FindInnerInput(box);
            var popup = new SuggestPopup();

            state = new SuggestState
            {
                Box = box,
                Input = input,
                Kind = kind,
                Popup = popup
            };
            _states.Add(box, state);

            EventHandler textChanged = (s, e) => OnTextChanged(state);
            box.TextChanged += textChanged;
            if (input != null && !ReferenceEquals(input, box))
                input.TextChanged += textChanged;

            box.KeyDown += (s, e) =>
            {
                if (TryHandleKey(e.KeyCode | Control.ModifierKeys))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            if (input != null && !ReferenceEquals(input, box))
            {
                input.KeyDown += (s, e) =>
                {
                    if (TryHandleKey(e.KeyCode | Control.ModifierKeys))
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };
            }

            popup.Activated += (s, e) => KeepTypingFocus(state);
            popup.GotFocus += (s, e) => KeepTypingFocus(state);
            popup.List.GotFocus += (s, e) => KeepTypingFocus(state);

            popup.List.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    int idx = popup.List.IndexFromPoint(e.Location);
                    if (idx >= 0)
                    {
                        popup.List.SelectedIndex = idx;
                        Pick(state);
                    }
                }
            };

            box.Disposed += (s, e) => DisposeState(state);
            if (input != null)
                input.Disposed += (s, e) => DisposeState(state);

            Form host = box.FindForm();
            if (host != null)
            {
                host.Move += (s, e) => Hide(state);
                host.FormClosed += (s, e) => DisposeState(state);
            }
        }

        private static void DisposeState(SuggestState state)
        {
            try
            {
                if (state == null) return;
                if (_active == state) _active = null;
                if (state.Popup != null && !state.Popup.IsDisposed)
                {
                    state.Popup.Hide();
                    state.Popup.Dispose();
                }
            }
            catch { }
        }

        private static string GetText(SuggestState state)
        {
            if (state.Input is TextBox tb) return tb.Text ?? "";
            if (state.Box is Guna2TextBox g) return g.Text ?? "";
            return state.Box.Text ?? "";
        }

        private static void SetText(SuggestState state, string value)
        {
            value = value ?? "";
            if (state.Input is TextBox tb)
                tb.Text = value;
            else if (state.Box is Guna2TextBox g)
                g.Text = value;
            else
                state.Box.Text = value;
        }

        private static void OnTextChanged(SuggestState state)
        {
            if (state == null || state.Suppress || state.Box == null || state.Box.IsDisposed)
                return;

            string q = GetText(state).Trim();
            if (q.Length == 0)
            {
                Hide(state);
                return;
            }

            List<string> matches = FilterMatches(state.Kind, q);
            if (matches.Count == 0)
            {
                Hide(state);
                return;
            }

            // Poora naam type ho gaya — list band, value already wahan hai
            if (matches.Count == 1 &&
                string.Equals(matches[0], q, StringComparison.OrdinalIgnoreCase))
            {
                Hide(state);
                return;
            }

            Show(state, matches);
        }

        private static void OnKey(Keys k)
        {
            SuggestState state = _active;
            if (state == null || state.Popup == null || !state.Popup.IsOpen)
                return;
            ListBox list = state.Popup.List;
            if (list.Items.Count == 0) return;

            if (k == Keys.Down)
            {
                list.SelectedIndex = Math.Min(list.Items.Count - 1, Math.Max(0, list.SelectedIndex + 1));
            }
            else if (k == Keys.Up)
            {
                list.SelectedIndex = Math.Max(0, list.SelectedIndex <= 0 ? 0 : list.SelectedIndex - 1);
            }
            else if (k == Keys.Enter)
            {
                if (list.SelectedItem != null)
                    Pick(state);
            }
            else if (k == Keys.Escape)
            {
                Hide(state);
            }
        }

        private static void Pick(SuggestState state)
        {
            if (state?.Popup == null || state.Popup.List.SelectedItem == null)
                return;

            string value = Convert.ToString(state.Popup.List.SelectedItem) ?? "";
            state.Suppress = true;
            try
            {
                SetText(state, value);
                try { state.Box.Focus(); } catch { }
            }
            finally
            {
                state.Suppress = false;
                Hide(state);
            }
        }

        private static void KeepTypingFocus(SuggestState state)
        {
            if (state == null) return;
            Control c = state.Input ?? state.Box;
            if (c == null || c.IsDisposed) return;
            try
            {
                if (!c.Focused)
                    c.Focus();
                if (c is TextBox tb && tb.SelectionLength != 0)
                {
                    tb.SelectionLength = 0;
                    tb.SelectionStart = tb.Text.Length;
                }
            }
            catch { }
        }

        private static void Show(SuggestState state, List<string> matches)
        {
            if (state?.Box == null || !state.Box.IsHandleCreated || state.Popup == null || state.Popup.IsDisposed)
                return;
            if (matches == null || matches.Count == 0)
                return;

            int w = Math.Max(state.Box.Width, 260);
            int row = 24;
            int h = Math.Min(220, row * Math.Min(matches.Count, MaxShow) + 10);
            Point screen = state.Box.PointToScreen(new Point(0, state.Box.Height));
            _active = state;
            state.Popup.ShowNames(matches, screen.X, screen.Y, w, h);
            KeepTypingFocus(state);
        }

        private static void Hide(SuggestState state)
        {
            try
            {
                if (state == null) return;
                if (_active == state) _active = null;
                if (state.Popup != null && !state.Popup.IsDisposed)
                    state.Popup.HidePopup();
            }
            catch { }
        }

        private static List<string> FilterMatches(NameKind kind, string query)
        {
            EnsureCache();
            IList<string> source = GetSource(kind);
            var result = new List<string>();
            if (source == null || source.Count == 0 || string.IsNullOrWhiteSpace(query))
                return result;

            string q = query.Trim();
            var starts = new List<string>();
            var wordHits = new List<string>();
            var contains = new List<string>();

            foreach (string raw in source)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string s = raw.Trim();
                if (s.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    starts.Add(s);
                    continue;
                }
                if (AnyWordMatch(s, q))
                {
                    wordHits.Add(s);
                    continue;
                }
                if (s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains.Add(s);
            }

            void Take(List<string> list)
            {
                foreach (string x in list)
                {
                    if (result.Count >= MaxShow) return;
                    if (!result.Exists(r => string.Equals(r, x, StringComparison.OrdinalIgnoreCase)))
                        result.Add(x);
                }
            }

            Take(starts);
            Take(wordHits);
            Take(contains);
            return result;
        }

        private static bool AnyWordMatch(string text, string q)
        {
            char[] seps = { ' ', '\t', '-', '_', '.', ',', '/', '\\', '\'', '"', '(', ')' };
            string[] parts = text.Split(seps, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                if (p.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static IList<string> GetSource(NameKind kind)
        {
            switch (kind)
            {
                case NameKind.Dealer: return _dealers;
                case NameKind.Customer: return _customers;
                case NameKind.StockParty: return _stockParties;
                case NameKind.Litter: return _litters;
                case NameKind.Rate: return _rates;
                case NameKind.Both:
                    var both = new List<string>();
                    if (_dealers != null) both.AddRange(_dealers);
                    if (_customers != null)
                    {
                        foreach (string c in _customers)
                            if (!both.Exists(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
                                both.Add(c);
                    }
                    return both;
                default:
                    return null;
            }
        }

        private static void EnsureCache()
        {
            lock (_lock)
            {
                if (_dealers != null && _customers != null && _stockParties != null &&
                    _litters != null && _rates != null &&
                    DateTime.UtcNow - _cacheUtc < CacheTtl)
                    return;

                _dealers = LoadValues(
                    "SELECT DISTINCT TRIM(DealerName) AS N FROM AddDealer " +
                    "WHERE DealerName IS NOT NULL AND TRIM(DealerName)<>'' ORDER BY N COLLATE NOCASE");
                _customers = LoadValues(
                    "SELECT DISTINCT TRIM(Name) AS N FROM AddCustomer " +
                    "WHERE Name IS NOT NULL AND TRIM(Name)<>'' ORDER BY N COLLATE NOCASE");
                // Stock search/Add2 — AddDealer nahi
                _stockParties = LoadValues(
                    "SELECT DISTINCT TRIM(PartyName) AS N FROM StockPartyName " +
                    "WHERE PartyName IS NOT NULL AND TRIM(PartyName)<>'' ORDER BY N COLLATE NOCASE");
                _litters = LoadValues(
                    "SELECT DISTINCT TRIM(CAST(v AS TEXT)) AS N FROM (" +
                    " SELECT Litter AS v FROM PetrolAdd WHERE IFNULL(Litter,0)<>0" +
                    " UNION SELECT AddDisel FROM AddStock WHERE IFNULL(AddDisel,0)<>0" +
                    " UNION SELECT Litter FROM StockDiesel WHERE IFNULL(Litter,0)<>0" +
                    ") WHERE v IS NOT NULL ORDER BY CAST(v AS REAL)");
                _rates = LoadValues(
                    "SELECT DISTINCT TRIM(CAST(v AS TEXT)) AS N FROM (" +
                    " SELECT Rate AS v FROM PetrolAdd WHERE IFNULL(Rate,0)<>0" +
                    " UNION SELECT Rate FROM AddStock WHERE IFNULL(Rate,0)<>0" +
                    " UNION SELECT Rate FROM StockDiesel WHERE IFNULL(Rate,0)<>0" +
                    ") WHERE v IS NOT NULL ORDER BY CAST(v AS REAL)");
                _cacheUtc = DateTime.UtcNow;
            }
        }

        private static List<string> LoadValues(string sql)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var con = new SQLiteConnection(projectconnection.conReturn()))
                {
                    con.Open();
                    using (var cmd = new SQLiteCommand(sql, con))
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            string n = Convert.ToString(rd[0])?.Trim();
                            if (string.IsNullOrEmpty(n) || !seen.Add(n))
                                continue;
                            list.Add(n);
                        }
                    }
                }
            }
            catch
            {
                try
                {
                    DataTable dt = MainClass.GetData(sql);
                    if (dt != null)
                    {
                        foreach (DataRow row in dt.Rows)
                        {
                            string n = Convert.ToString(row[0])?.Trim();
                            if (string.IsNullOrEmpty(n) || !seen.Add(n))
                                continue;
                            list.Add(n);
                        }
                    }
                }
                catch { }
            }
            return list;
        }
    }
}
