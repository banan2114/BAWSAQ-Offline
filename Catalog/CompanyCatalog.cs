
using System.Collections.Generic;
using System.Linq;
using BawsaqOffline.Core.Models;

namespace BawsaqOffline.Core.Catalog
{
    public static class CompanyCatalog
    {
        private static readonly List<CompanyDefinition> companies =
            new List<CompanyDefinition>
            {
                // Текущие акции проекта. Их тикеры пока сохраняем.
                new CompanyDefinition(
                    "AUG", "Augury Insurance",
                    StockExchange.LCN, 125.50m, 0.035m, 0.0002m),

                new CompanyDefinition(
                    "ECO", "eCola",
                    StockExchange.LCN, 82.30m, 0.045m, 0.0001m),

                new CompanyDefinition(
                    "FLY", "FlyUS",
                    StockExchange.LCN, 49.80m, 0.060m, -0.0001m),

                new CompanyDefinition(
                    "LFI", "Lifeinvader",
                    StockExchange.LCN, 96.40m, 0.055m, 0.0003m),

                new CompanyDefinition(
                    "MAZ", "Maze Bank",
                    StockExchange.LCN, 210.00m, 0.025m, 0.0002m),

                new CompanyDefinition(
                    "RED", "Redwood",
                    StockExchange.LCN, 64.20m, 0.070m, 0.0001m),

                // Акции, необходимые для миссий Лестера.
                new CompanyDefinition(
                    "BET", "Betta Pharmaceuticals",
                    StockExchange.BAWSAQ, 100.00m, 0.050m, 0.0001m),

                new CompanyDefinition(
                    "BIL", "Bilkinton Research",
                    StockExchange.LCN, 100.00m, 0.050m, 0.0001m),

                new CompanyDefinition(
                    "DEB", "Debonaire",
                    StockExchange.LCN, 100.00m, 0.045m, 0.0001m),

                new CompanyDefinition(
                    "RWC", "Redwood Cigarettes",
                    StockExchange.LCN, 100.00m, 0.060m, 0.0001m),

                new CompanyDefinition(
                    "FRT", "Fruit",
                    StockExchange.BAWSAQ, 100.00m, 0.050m, 0.0001m),

                new CompanyDefinition(
                    "FAC", "Facade",
                    StockExchange.BAWSAQ, 100.00m, 0.050m, 0.0001m),

                new CompanyDefinition(
                    "VAP", "Vapid",
                    StockExchange.BAWSAQ, 100.00m, 0.055m, 0.0001m),

                new CompanyDefinition(
                    "GCD", "GoldCoast",
                    StockExchange.LCN, 100.00m, 0.040m, 0.0001m)
            };

        public static IReadOnlyList<CompanyDefinition> GetAll()
        {
            return companies.AsReadOnly();
        }

        public static CompanyDefinition GetBySymbol(string symbol)
        {
            return companies.FirstOrDefault(
                x => x.Symbol == symbol);
        }

        public static List<Stock> CreateStocks()
        {
            return companies
                .Select(x => x.CreateStock())
                .ToList();
        }

        public static List<Stock> CreateStocks(StockExchange exchange)
        {
            return companies
                .Where(x => x.Exchange == exchange)
                .Select(x => x.CreateStock())
                .ToList();
        }
    }
}
