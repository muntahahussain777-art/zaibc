using System;

namespace ZaibPetroleumService.Services
{
    internal static class StockAssetsCalcEngine
    {
        public static decimal BaqayaValue(decimal baqayaLitter, decimal rate)
        {
            return baqayaLitter * rate;
        }

        /// <summary>
        /// Positive baqaya → (Baqaya × latest sale/minus rate) + Sale − Purchase.
        /// Negative baqaya → (Baqaya × latest add rate) + Purchase − Sale.
        /// </summary>
        public static decimal ComputeAssets(
            decimal baqayaLitter,
            decimal rate,
            decimal saleAmount,
            decimal purchaseAmount,
            decimal sourceAddRate,
            decimal sourceSaleRate)
        {
            decimal baqayaValue = BaqayaValue(baqayaLitter, rate);
            if (baqayaLitter > 0m)
                return baqayaValue + saleAmount - purchaseAmount;
            if (baqayaLitter < 0m)
                return baqayaValue + purchaseAmount - saleAmount;
            return saleAmount - purchaseAmount;
        }

        public static bool RatesAreSame(decimal addRate, decimal saleRate)
        {
            return Math.Abs(addRate - saleRate) < 0.0001m;
        }
    }
}
