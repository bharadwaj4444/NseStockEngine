
namespace NseStock.Analysis.Calculators;

public static class TechnicalIndicatorCalculator
{
    public static double?[] CalculateSma(
    IReadOnlyList<double> values,
    int period)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new double?[values.Count];
        double sum = 0;

        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];

            if (i >= period)
                sum -= values[i - period];

            if (i >= period - 1)
                result[i] = sum / period;
        }

        return result;
    }

    public static double?[] CalculateEma(
        IReadOnlyList<double> values,
        int period)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new double?[values.Count];

        if (values.Count == 0)
            return result;

        double alpha = 2.0 / (period + 1);
        double ema = values[0];

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                ema = alpha * values[i] + (1 - alpha) * ema;

            // Match pandas ewm(..., adjust=False, min_periods=period).
            if (i >= period - 1)
                result[i] = ema;
        }

        return result;
    }

    public static double?[] CalculateRsi(
        IReadOnlyList<double> closePrices,
        int period = 14)
    {
        ArgumentNullException.ThrowIfNull(closePrices);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new double?[closePrices.Count];

        if (closePrices.Count <= period)
            return result;

        double alpha = 1.0 / period;
        double averageGain = 0;
        double averageLoss = 0;

        for (int i = 1; i < closePrices.Count; i++)
        {
            double delta = closePrices[i] - closePrices[i - 1];
            double gain = Math.Max(delta, 0);
            double loss = Math.Max(-delta, 0);

            if (i == 1)
            {
                averageGain = gain;
                averageLoss = loss;
            }
            else
            {
                averageGain =
                    (1 - alpha) * averageGain + alpha * gain;

                averageLoss =
                    (1 - alpha) * averageLoss + alpha * loss;
            }

            // RSI needs period valid price changes, so the first
            // possible result is at index period.
            if (i < period)
                continue;

            result[i] = averageLoss == 0
                ? 100.0
                : 100.0 - 100.0 /
                    (1.0 + averageGain / averageLoss);
        }

        return result;
    }

    public static (double?[] Macd, double?[] Signal, double?[] Histogram)
        CalculateMacd(IReadOnlyList<double> closePrices)
    {
        ArgumentNullException.ThrowIfNull(closePrices);

        var ema12 = CalculateEma(closePrices, 12);
        var ema26 = CalculateEma(closePrices, 26);

        var macd = new double?[closePrices.Count];

        for (int i = 0; i < closePrices.Count; i++)
        {
            if (ema12[i].HasValue && ema26[i].HasValue)
                macd[i] = ema12[i]!.Value - ema26[i]!.Value;
        }

        // Python's ema() requires 9 valid MACD values before
        // exposing the first signal value.
        var validMacd = new List<double>();
        var validIndexes = new List<int>();

        for (int i = 0; i < macd.Length; i++)
        {
            if (macd[i].HasValue)
            {
                validMacd.Add(macd[i]!.Value);
                validIndexes.Add(i);
            }
        }

        var signal = new double?[closePrices.Count];
        var histogram = new double?[closePrices.Count];

        var validSignal = CalculateEma(validMacd, 9);

        for (int i = 0; i < validIndexes.Count; i++)
        {
            int index = validIndexes[i];

            if (!validSignal[i].HasValue)
                continue;

            signal[index] = validSignal[i]!.Value;
            histogram[index] = macd[index]!.Value - signal[index]!.Value;
        }

        return (macd, signal, histogram);
    }


    public static double?[] CalculateAtr(
        IReadOnlyList<double> high,
        IReadOnlyList<double> low,
        IReadOnlyList<double> close,
        int period = 14)
    {
        ArgumentNullException.ThrowIfNull(high);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(close);

        if (high.Count != low.Count || high.Count != close.Count)
            throw new ArgumentException(
                "High, low, and close must have the same length.");

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        int count = close.Count;
        var trueRange = new double[count];
        var result = new double?[count];

        for (int i = 0; i < count; i++)
        {
            double previousClose = i == 0 ? double.NaN : close[i - 1];

            double range1 = high[i] - low[i];
            double range2 = double.IsNaN(previousClose)
                ? double.NaN
                : Math.Abs(high[i] - previousClose);
            double range3 = double.IsNaN(previousClose)
                ? double.NaN
                : Math.Abs(low[i] - previousClose);

            // Python's row-wise max skips NaN values.
            trueRange[i] = i == 0
                ? range1
                : Math.Max(range1, Math.Max(range2, range3));
        }

        if (count == 0)
            return result;

        double alpha = 1.0 / period;
        double atr = trueRange[0];

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                atr = (1 - alpha) * atr + alpha * trueRange[i];

            if (i >= period - 1)
                result[i] = atr;
        }

        return result;
    }


    public static (
        double?[] Upper,
        double?[] Middle,
        double?[] Lower)
        CalculateBollingerBands(
            IReadOnlyList<double> closePrices,
            int period = 20,
            double standardDeviations = 2.0)
    {
        ArgumentNullException.ThrowIfNull(closePrices);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        if (standardDeviations < 0 ||
            !double.IsFinite(standardDeviations))
        {
            throw new ArgumentOutOfRangeException(
                nameof(standardDeviations));
        }

        int count = closePrices.Count;

        var upper = new double?[count];
        var lower = new double?[count];

        // Middle band uses the same SMA calculation
        // as the Python implementation.
        var middle = CalculateSma(closePrices, period);

        for (int i = period - 1; i < count; i++)
        {
            double mean = middle[i]!.Value;
            double sumSquaredDifferences = 0.0;

            for (int j = i - period + 1; j <= i; j++)
            {
                double difference = closePrices[j] - mean;
                sumSquaredDifferences += difference * difference;
            }

            // Python uses rolling.std(ddof=0):
            // population standard deviation.
            double standardDeviation = Math.Sqrt(
                sumSquaredDifferences / period);

            upper[i] = mean +
                standardDeviations * standardDeviation;

            lower[i] = mean -
                standardDeviations * standardDeviation;
        }

        return (upper, middle, lower);
    }


    public static double?[] CalculateAdx(
        IReadOnlyList<double> high,
        IReadOnlyList<double> low,
        IReadOnlyList<double> close,
        int period = 14)
    {
        ArgumentNullException.ThrowIfNull(high);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(close);

        if (high.Count != low.Count || high.Count != close.Count)
            throw new ArgumentException(
                "High, low, and close must have the same length.");

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        int count = close.Count;
        var plusDm = new double[count];
        var minusDm = new double[count];
        var trueRange = new double[count];

        var plusDi = new double?[count];
        var minusDi = new double?[count];
        var dx = new double?[count];
        var adx = new double?[count];

        if (count == 0)
            return adx;

        // Calculate directional movement and True Range.
        // The first directional movements are zero, matching
        // Python's diff().where(condition, 0.0) behavior.
        for (int i = 0; i < count; i++)
        {
            double range = high[i] - low[i];

            if (i == 0)
            {
                trueRange[i] = range;
                continue;
            }

            double up = high[i] - high[i - 1];
            double down = low[i - 1] - low[i];

            plusDm[i] = up > down && up > 0
                ? up
                : 0.0;

            minusDm[i] = down > up && down > 0
                ? down
                : 0.0;

            trueRange[i] = Math.Max(
                range,
                Math.Max(
                    Math.Abs(high[i] - close[i - 1]),
                    Math.Abs(low[i] - close[i - 1])));
        }

        var smoothedTr = CalculateWilderEwm(trueRange, period);
        var smoothedPlusDm = CalculateWilderEwm(plusDm, period);
        var smoothedMinusDm = CalculateWilderEwm(minusDm, period);

        // Calculate +DI, -DI, and DX.
        for (int i = 0; i < count; i++)
        {
            if (!smoothedTr[i].HasValue ||
                !smoothedPlusDm[i].HasValue ||
                !smoothedMinusDm[i].HasValue)
            {
                continue;
            }

            double atr = smoothedTr[i]!.Value;

            if (atr == 0)
                continue;

            plusDi[i] = 100.0 * smoothedPlusDm[i]!.Value / atr;
            minusDi[i] = 100.0 * smoothedMinusDm[i]!.Value / atr;

            double denominator = plusDi[i]!.Value + minusDi[i]!.Value;

            // Python replaces a zero denominator with NaN.
            if (denominator == 0)
                continue;

            dx[i] = 100.0 *
                Math.Abs(plusDi[i]!.Value - minusDi[i]!.Value) /
                denominator;
        }

        // Apply Wilder smoothing to DX. Only valid DX values
        // contribute to the warm-up count.
        var validDx = new List<double>();
        var validIndexes = new List<int>();

        for (int i = 0; i < count; i++)
        {
            if (dx[i].HasValue)
            {
                validDx.Add(dx[i]!.Value);
                validIndexes.Add(i);
            }
        }

        var smoothedAdx = CalculateWilderEwm(
            validDx.ToArray(), period);

        for (int i = 0; i < validIndexes.Count; i++)
        {
            if (smoothedAdx[i].HasValue)
                adx[validIndexes[i]] = smoothedAdx[i]!.Value;
        }

        return adx;
    }

    private static double?[] CalculateWilderEwm(
        IReadOnlyList<double> values,
        int period)
    {
        var result = new double?[values.Count];

        if (values.Count == 0)
            return result;

        double alpha = 1.0 / period;
        double value = values[0];

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                value = (1.0 - alpha) * value
                    + alpha * values[i];
            }

            // Equivalent to min_periods=period when all
            // observations in this series are valid.
            if (i >= period - 1)
                result[i] = value;
        }

        return result;
    }


    public static double?[] CalculateReturns(
        IReadOnlyList<double> closePrices,
        int period = 1)
    {
        ArgumentNullException.ThrowIfNull(closePrices);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new double?[closePrices.Count];

        for (int i = period; i < closePrices.Count; i++)
        {
            double previousClose = closePrices[i - period];

            if (previousClose == 0)
                continue;

            result[i] = closePrices[i] / previousClose - 1.0;
        }

        return result;
    }

    public static double?[] CalculateVolatility(
        IReadOnlyList<double> closePrices,
        int period = 20)
    {
        ArgumentNullException.ThrowIfNull(closePrices);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        int count = closePrices.Count;
        var result = new double?[count];

        if (count <= period)
            return result;

        // Daily percentage changes, expressed as fractions.
        var dailyReturns = CalculateReturns(closePrices, 1);

        for (int i = period; i < count; i++)
        {
            double sum = 0.0;

            // At index i, use the preceding 'period' daily
            // returns, including the return ending at i.
            for (int j = i - period + 1; j <= i; j++)
            {
                if (!dailyReturns[j].HasValue)
                    break;

                sum += dailyReturns[j]!.Value;
            }

            // The first daily return is unavailable, so the
            // first full window can occur at index 'period'.
            if (i < period)
                continue;

            double mean = sum / period;
            double sumSquaredDifferences = 0.0;
            bool complete = true;

            for (int j = i - period + 1; j <= i; j++)
            {
                if (!dailyReturns[j].HasValue)
                {
                    complete = false;
                    break;
                }

                double difference = dailyReturns[j]!.Value - mean;
                sumSquaredDifferences += difference * difference;
            }

            if (!complete)
                continue;

            // pandas rolling.std() defaults to ddof=1:
            // sample standard deviation.
            double standardDeviation = Math.Sqrt(
                sumSquaredDifferences / (period - 1));

            result[i] = standardDeviation * Math.Sqrt(252.0);
        }

        return result;
    }

    public static double?[] CalculateRelativeVolume(
        IReadOnlyList<double> volumes,
        int period = 20)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new double?[volumes.Count];
        var averageVolume = CalculateSma(volumes, period);

        for (int i = period - 1; i < volumes.Count; i++)
        {
            double average = averageVolume[i]!.Value;

            if (average == 0)
                continue;

            result[i] = volumes[i] / average;
        }

        return result;
    }


    public static (
        double?[] Relative20d,
        double?[] Relative60d)
        CalculateNiftyRelativeReturns(
            IReadOnlyList<DateOnly> stockDates,
            IReadOnlyList<double> stockClose,
            IReadOnlyList<DateOnly> niftyDates,
            IReadOnlyList<double> niftyClose)
    {
        ArgumentNullException.ThrowIfNull(stockDates);
        ArgumentNullException.ThrowIfNull(stockClose);
        ArgumentNullException.ThrowIfNull(niftyDates);
        ArgumentNullException.ThrowIfNull(niftyClose);

        if (stockDates.Count != stockClose.Count)
            throw new ArgumentException(
                "Stock dates and closes must have the same length.");

        if (niftyDates.Count != niftyClose.Count)
            throw new ArgumentException(
                "NIFTY dates and closes must have the same length.");

        var relative20d = new double?[stockDates.Count];
        var relative60d = new double?[stockDates.Count];

        // Map each NIFTY date to its closing price.
        var niftyByDate = new Dictionary<DateOnly, double>();

        for (int i = 0; i < niftyDates.Count; i++)
        {
            if (!niftyByDate.TryAdd(niftyDates[i], niftyClose[i]))
                throw new ArgumentException(
                    $"Duplicate NIFTY date: {niftyDates[i]}");
        }

        // Keep only dates present in both series, then sort
        // chronologically, matching Python's inner join.
        var aligned = new List<(
            DateOnly Date,
            int StockIndex,
            double StockClose,
            double NiftyClose)>();

        var stockDateSet = new HashSet<DateOnly>();

        for (int i = 0; i < stockDates.Count; i++)
        {
            if (!stockDateSet.Add(stockDates[i]))
                throw new ArgumentException(
                    $"Duplicate stock date: {stockDates[i]}");

            if (niftyByDate.TryGetValue(stockDates[i], out double niftyPrice))
            {
                aligned.Add((
                    stockDates[i],
                    i,
                    stockClose[i],
                    niftyPrice));
            }
        }

        aligned.Sort((a, b) => a.Date.CompareTo(b.Date));

        for (int i = 0; i < aligned.Count; i++)
        {
            // A 20-day return needs the price 20 aligned
            // observations earlier, not 20 calendar days earlier.
            CalculateRelativeReturn(aligned, i, 20, relative20d);
            CalculateRelativeReturn(aligned, i, 60, relative60d);
        }

        return (relative20d, relative60d);
    }

    private static void CalculateRelativeReturn(
        List<(
            DateOnly Date,
            int StockIndex,
            double StockClose,
            double NiftyClose)> aligned,
        int currentIndex,
        int period,
        double?[] output)
    {
        if (currentIndex < period)
            return;

        var current = aligned[currentIndex];
        var previous = aligned[currentIndex - period];

        if (previous.StockClose == 0 || previous.NiftyClose == 0)
            return;

        double stockReturn =
            current.StockClose / previous.StockClose - 1.0;

        double niftyReturn =
            current.NiftyClose / previous.NiftyClose - 1.0;

        output[current.StockIndex] = stockReturn - niftyReturn;
    }

}
