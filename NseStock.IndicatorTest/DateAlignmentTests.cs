
using NseStock.Analysis.Calculators;
using Xunit;

public class DateAlignmentTests
{
    [Fact]
    public void RelativeReturns_UseCommonTradingDates_For20DayLookback()
    {
        var start = new DateOnly(2026, 1, 1);

        // Create 21 common dates. Insert one stock-only date
        // and one NIFTY-only date to test date alignment.
        var commonDates = Enumerable.Range(0, 21)
            .Select(i => start.AddDays(i * 2))
            .ToArray();

        var stockDates = new List<DateOnly>();
        var stockClose = new List<double>();

        var niftyDates = new List<DateOnly>();
        var niftyClose = new List<double>();

        for (int i = 0; i < commonDates.Length; i++)
        {
            stockDates.Add(commonDates[i]);
            stockClose.Add(100.0 + i);

            niftyDates.Add(commonDates[i]);
            niftyClose.Add(200.0 + i);
        }

        // Extra dates must not count as aligned observations.
        stockDates.Add(start.AddDays(1));
        stockClose.Add(999.0);

        niftyDates.Add(start.AddDays(3));
        niftyClose.Add(9999.0);

        // The inputs need not be pre-sorted; the production
        // method is responsible for sorting the aligned dates.
        var stock = stockDates
            .Select((date, i) => (date, close: stockClose[i]))
            .OrderBy(x => x.date)
            .ToArray();

        var nifty = niftyDates
            .Select((date, i) => (date, close: niftyClose[i]))
            .OrderBy(x => x.date)
            .ToArray();

        var result =
            TechnicalIndicatorCalculator.CalculateNiftyRelativeReturns(
                stock.Select(x => x.date).ToArray(),
                stock.Select(x => x.close).ToArray(),
                nifty.Select(x => x.date).ToArray(),
                nifty.Select(x => x.close).ToArray());

        // The 20th aligned observation is the first one
        // that can have a 20-observation return.
        var firstEligibleDate = commonDates[20];
        var firstEligibleStockIndex =
            Array.IndexOf(stock.Select(x => x.date).ToArray(),
                firstEligibleDate);

        Assert.Null(result.Relative20d[
            Array.IndexOf(stock.Select(x => x.date).ToArray(),
                commonDates[19])]);

        double expected =
            (120.0 / 100.0 - 1.0)
            - (220.0 / 200.0 - 1.0);

        Assert.Equal(
            expected,
            result.Relative20d[firstEligibleStockIndex]!.Value,
            10);

        // The inserted stock-only date is not used as a
        // benchmark-aligned observation.
        var stockOnlyIndex =
            Array.IndexOf(stock.Select(x => x.date).ToArray(),
                start.AddDays(1));

        Assert.Null(result.Relative20d[stockOnlyIndex]);
    }
}