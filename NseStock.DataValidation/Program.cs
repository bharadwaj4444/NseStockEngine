
using Npgsql;
using NseStock.Analysis.Calculators;

var connectionString =
    Environment.GetEnvironmentVariable("NSE_STOCKS_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("NSE_STOCKS_CONNECTION_STRING is not set.");
    return;
}

var symbol = args.Length > 0 ? args[0].ToUpperInvariant() : "RELIANCE";
const string benchmark = "NIFTY50";
const double tolerance = 1e-6;

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();


const string dateCoverageSql = """
    SELECT
        trade_date,
        COUNT(*) AS indicator_rows,
        COUNT(nifty_relative_20d) AS relative20_count,
        COUNT(nifty_relative_60d) AS relative60_count
    FROM technical_indicators
    GROUP BY trade_date
    HAVING COUNT(nifty_relative_20d) > 0
        OR COUNT(nifty_relative_60d) > 0
    ORDER BY trade_date;
    """;

await using (var command = new NpgsqlCommand(dateCoverageSql, connection))
await using (var reader = await command.ExecuteReaderAsync())
{
    Console.WriteLine("--- Dates with stored NIFTY relative returns ---");

    while (await reader.ReadAsync())
    {
        Console.WriteLine(
            $"{reader.GetFieldValue<DateOnly>(0):yyyy-MM-dd} | " +
            $"rows={reader.GetInt64(1)} | " +
            $"20d={reader.GetInt64(2)} | " +
            $"60d={reader.GetInt64(3)}");
    }

    Console.WriteLine();
}

const string coverageSql = """
    SELECT
        COUNT(*) AS total_rows,
        COUNT(nifty_relative_20d) AS relative20_values,
        COUNT(nifty_relative_60d) AS relative60_values,
        COUNT(rsi14) AS rsi_values,
        COUNT(sma20) AS sma20_values,
        COUNT(DISTINCT company_id)
            FILTER (WHERE nifty_relative_20d IS NOT NULL) AS companies_with_relative20,
        COUNT(DISTINCT company_id)
            FILTER (WHERE nifty_relative_60d IS NOT NULL) AS companies_with_relative60
    FROM technical_indicators;
    """;

await using (var command = new NpgsqlCommand(coverageSql, connection))
await using (var reader = await command.ExecuteReaderAsync())
{
    await reader.ReadAsync();

    Console.WriteLine("--- Entire database: indicator coverage ---");
    Console.WriteLine($"Total indicator rows: {reader.GetInt64(0)}");
    Console.WriteLine($"Non-null NIFTY relative 20d: {reader.GetInt64(1)}");
    Console.WriteLine($"Non-null NIFTY relative 60d: {reader.GetInt64(2)}");
    Console.WriteLine($"Non-null RSI14: {reader.GetInt64(3)}");
    Console.WriteLine($"Non-null SMA20: {reader.GetInt64(4)}");
    Console.WriteLine($"Companies with relative 20d: {reader.GetInt64(5)}");
    Console.WriteLine($"Companies with relative 60d: {reader.GetInt64(6)}");
    Console.WriteLine();
}

var stockRows = new List<StockRow>();

const string stockSql = """
    SELECT dp.trade_date,
           dp.close_price,
           ti.nifty_relative_20d,
           ti.nifty_relative_60d
    FROM companies c
    JOIN daily_prices dp ON dp.company_id = c.id
    LEFT JOIN technical_indicators ti
           ON ti.company_id = dp.company_id
          AND ti.trade_date = dp.trade_date
    WHERE UPPER(c.nse_symbol) = @symbol
    ORDER BY dp.trade_date;
    """;

await using (var command = new NpgsqlCommand(stockSql, connection))
{
    command.Parameters.AddWithValue("symbol", symbol);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        if (reader.IsDBNull(1))
        {
            throw new InvalidOperationException(
                $"Missing close price for {symbol} on {reader.GetFieldValue<DateOnly>(0)}.");
        }

        stockRows.Add(new StockRow(
            reader.GetFieldValue<DateOnly>(0),
            Convert.ToDouble(reader.GetValue(1)),
            ReadNullableDouble(reader, 2),
            ReadNullableDouble(reader, 3)));
    }
}

var niftyRows = new List<(DateOnly Date, double Close)>();

const string niftySql = """
    SELECT trade_date, close_price
    FROM benchmark_prices
    WHERE benchmark = @benchmark
      AND close_price IS NOT NULL
    ORDER BY trade_date;
    """;

await using (var command = new NpgsqlCommand(niftySql, connection))
{
    command.Parameters.AddWithValue("benchmark", benchmark);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        niftyRows.Add((
            reader.GetFieldValue<DateOnly>(0),
            Convert.ToDouble(reader.GetValue(1))));
    }
}

if (stockRows.Count == 0)
{
    Console.Error.WriteLine($"No daily prices found for {symbol}.");
    return;
}

if (niftyRows.Count == 0)
{
    Console.Error.WriteLine(
        $"No benchmark prices found for '{benchmark}'. " +
        "Check the benchmark values in benchmark_prices.");
    return;
}

const string diagnosticSql = """
    SELECT
        COUNT(*) AS stock_rows,
        COUNT(ti.nifty_relative_20d) AS stored_20d_count,
        COUNT(ti.nifty_relative_60d) AS stored_60d_count,
        MIN(ti.trade_date) FILTER (
            WHERE ti.nifty_relative_20d IS NOT NULL
        ) AS first_20d_date,
        MAX(ti.trade_date) FILTER (
            WHERE ti.nifty_relative_20d IS NOT NULL
        ) AS last_20d_date,
        MIN(ti.trade_date) FILTER (
            WHERE ti.nifty_relative_60d IS NOT NULL
        ) AS first_60d_date,
        MAX(ti.trade_date) FILTER (
            WHERE ti.nifty_relative_60d IS NOT NULL
        ) AS last_60d_date
    FROM companies c
    JOIN daily_prices dp ON dp.company_id = c.id
    LEFT JOIN technical_indicators ti
           ON ti.company_id = dp.company_id
          AND ti.trade_date = dp.trade_date
    WHERE UPPER(c.nse_symbol) = @symbol;
    """;

await using (var command = new NpgsqlCommand(diagnosticSql, connection))
{
    command.Parameters.AddWithValue("symbol", symbol);

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();

    Console.WriteLine("--- Stored database coverage ---");
    Console.WriteLine($"Stock rows: {reader.GetInt64(0)}");
    Console.WriteLine($"Stored 20d non-null: {reader.GetInt64(1)}");
    Console.WriteLine($"Stored 60d non-null: {reader.GetInt64(2)}");
    Console.WriteLine($"20d date range: {FormatDate(reader, 3)} to {FormatDate(reader, 4)}");
    Console.WriteLine($"60d date range: {FormatDate(reader, 5)} to {FormatDate(reader, 6)}");
    Console.WriteLine();
}

static string FormatDate(NpgsqlDataReader reader, int ordinal) =>
    reader.IsDBNull(ordinal)
        ? "none"
        : reader.GetFieldValue<DateOnly>(ordinal).ToString("yyyy-MM-dd");

var calculated = TechnicalIndicatorCalculator.CalculateNiftyRelativeReturns(
    stockRows.Select(x => x.Date).ToArray(),
    stockRows.Select(x => x.Close).ToArray(),
    niftyRows.Select(x => x.Date).ToArray(),
    niftyRows.Select(x => x.Close).ToArray());

var relative20 = calculated.Relative20d;
var relative60 = calculated.Relative60d;

var commonDates = stockRows.Count(stock =>
    niftyRows.Any(nifty => nifty.Date == stock.Date));

Console.WriteLine($"Symbol: {symbol}");
Console.WriteLine($"Stock price rows: {stockRows.Count}");
Console.WriteLine($"NIFTY rows: {niftyRows.Count}");
Console.WriteLine($"Dates shared by stock and NIFTY: {commonDates}");
Console.WriteLine();

Compare("20-day", stockRows, relative20,
    row => row.Stored20, tolerance);

Compare("60-day", stockRows, relative60,
    row => row.Stored60, tolerance);

static double? ReadNullableDouble(NpgsqlDataReader reader, int ordinal) =>
    reader.IsDBNull(ordinal)
        ? null
        : Convert.ToDouble(reader.GetValue(ordinal));

static void Compare(
    string label,
    IReadOnlyList<StockRow> rows,
    double?[] calculated,
    Func<StockRow, double?> storedSelector,
    double tolerance)
{
    int compared = 0;
    int mismatches = 0;
    int nullMismatches = 0;
    double maxDifference = 0;

    Console.WriteLine($"--- {label} relative return ---");

    for (int i = 0; i < rows.Count; i++)
    {
        var stored = storedSelector(rows[i]);
        var actual = calculated[i];

        if (stored is null && actual is null)
            continue;

        if (stored is null || actual is null)
        {
            mismatches++;
            nullMismatches++;

            if (mismatches <= 10)
            {
                Console.WriteLine(
                    $"{rows[i].Date}: NULL mismatch; " +
                    $"C#={Format(actual)}, DB={Format(stored)}");
            }

            continue;
        }

        compared++;
        var difference = Math.Abs(actual.Value - stored.Value);
        maxDifference = Math.Max(maxDifference, difference);

        if (difference > tolerance)
        {
            mismatches++;

            if (mismatches <= 10)
            {
                Console.WriteLine(
                    $"{rows[i].Date}: C#={actual.Value:F8}, " +
                    $"DB={stored.Value:F8}, diff={difference:G8}");
            }
        }
    }

    Console.WriteLine($"Numeric comparisons: {compared}");
    Console.WriteLine($"Mismatches: {mismatches}");
    Console.WriteLine($"NULL mismatches: {nullMismatches}");
    Console.WriteLine($"Maximum absolute difference: {maxDifference:G8}");
    Console.WriteLine();
}

static string Format(double? value) =>
    value?.ToString("F8") ?? "NULL";

sealed record StockRow(
    DateOnly Date,
    double Close,
    double? Stored20,
    double? Stored60);