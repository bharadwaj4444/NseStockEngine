
using NseStock.Analysis.Calculators;

static void Print(string name, double?[] values)
{
    Console.WriteLine(name + ": " +
        string.Join(", ", values.Select(
            value => value?.ToString("F3") ?? "null")));
}


double[] prices = Enumerable.Range(1, 40)
    .Select(x => (double)x * 10)
    .ToArray();

var macdResult =
    TechnicalIndicatorCalculator.CalculateMacd(prices);

Print("MACD", macdResult.Macd);
Print("MACD Signal", macdResult.Signal);
Print("MACD Histogram", macdResult.Histogram);


double[] highs = [12, 22, 32, 42, 52];
double[] lows = [8, 18, 28, 38, 48];
double[] closes = [10, 20, 30, 40, 50];

Print("ATR(3)",
    TechnicalIndicatorCalculator.CalculateAtr(
        highs, lows, closes, period: 3));


double[] bollingerPrices =
    [10, 20, 30, 40, 50];

var bands =
    TechnicalIndicatorCalculator.CalculateBollingerBands(
        bollingerPrices,
        period: 3);

Print("BB Upper", bands.Upper);
Print("BB Middle", bands.Middle);
Print("BB Lower", bands.Lower);


double[] adxClose = Enumerable.Range(1, 40)
    .Select(x => x * 10.0)
    .ToArray();

double[] adxHigh = adxClose
    .Select(x => x + 2.0)
    .ToArray();

double[] adxLow = adxClose
    .Select(x => x - 2.0)
    .ToArray();

var adxResult = TechnicalIndicatorCalculator.CalculateAdx(
    adxHigh,
    adxLow,
    adxClose,
    period: 14);

Print("ADX(14)", adxResult);


double[] returnPrices = [100, 110, 99, 108.9, 120];

Print(
    "Returns(1)",
    TechnicalIndicatorCalculator.CalculateReturns(
        returnPrices, 1));

Print(
    "Returns(2)",
    TechnicalIndicatorCalculator.CalculateReturns(
        returnPrices, 2));

Print(
    "Volatility(2)",
    TechnicalIndicatorCalculator.CalculateVolatility(
        returnPrices, 2));


double[] testVolumes = [100, 200, 300, 400, 500];

Print(
    "Relative Volume(3)",
    TechnicalIndicatorCalculator.CalculateRelativeVolume(
        testVolumes, 3));


DateOnly start = new(2026, 1, 1);

DateOnly[] stockDates = Enumerable.Range(0, 21)
    .Select(i => start.AddDays(i))
    .ToArray();

DateOnly[] niftyDates = stockDates.ToArray();

double[] stockClose = Enumerable.Range(0, 21)
    .Select(i => 100.0 + i)
    .ToArray();

double[] niftyClose = Enumerable.Range(0, 21)
    .Select(i => 100.0)
    .ToArray();

var relative =
    TechnicalIndicatorCalculator.CalculateNiftyRelativeReturns(
        stockDates,
        stockClose,
        niftyDates,
        niftyClose);

Print("NIFTY Relative 20d", relative.Relative20d);
Print("NIFTY Relative 60d", relative.Relative60d);