using System;

namespace ZaibPetroleumService.ReportForm
{
    public partial class frmStockDieselForm
    {
        private int _partyNameId;
        private string _partyType1 = "";
        private string _partyType2 = "";
        private string _partyType3 = "";
        private bool _usePartyStockQuery;

        public void SetStockReportFilters(DateTime fromDate, DateTime toDate, string searchText)
        {
            Load += (s, e) =>
            {
                fromdate.Value = fromDate;
                todate.Value = toDate;
                txtSearch.Text = searchText ?? string.Empty;
                btnSearch.PerformClick();
            };
        }

        /// <summary>Stock VIP report — StockPartyName + Type/Type2/Type3 filters.</summary>
        public void SetStockPartyReportFilters(
            DateTime fromDate, DateTime toDate, string searchText,
            int partyNameId, string type1, string type2, string type3 = "")
        {
            _usePartyStockQuery = true;
            _partyNameId = partyNameId;
            _partyType1 = type1 ?? "";
            _partyType2 = type2 ?? "";
            _partyType3 = type3 ?? "";
            Load += (s, e) =>
            {
                fromdate.Value = fromDate;
                todate.Value = toDate;
                txtSearch.Text = searchText ?? string.Empty;
                btnSearch.PerformClick();
            };
        }
    }
}
