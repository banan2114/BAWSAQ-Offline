
using BawsaqOffline.Core.Models;

namespace BawsaqOffline.Core.Catalog
{
    public sealed class CompanyDefinition
    {
        public string Symbol { get; private set; }
        public string Name { get; private set; }
        public StockExchange Exchange { get; private set; }

        public decimal InitialPrice { get; private set; }
        public decimal Volatility { get; private set; }
        public decimal Drift { get; private set; }

        public CompanyDefinition(
            string symbol,
            string name,
            StockExchange exchange,
            decimal initialPrice,
            decimal volatility,
            decimal drift)
        {
            Symbol = symbol;
            Name = name;
            Exchange = exchange;
            InitialPrice = initialPrice;
            Volatility = volatility;
            Drift = drift;
        }

        public Stock CreateStock()
        {
            return new Stock(
                Symbol,
                Name,
                InitialPrice,
                Volatility,
                Drift,
                Exchange);
        }
    }
}
