
using System.ComponentModel;
using System.Globalization;
using Npgsql;

public sealed class StockTools
{
    [Description(
        "Retrieves the latest stored technical indicators for an NSE stock. " +
        "Returns the indicator date, RSI, MACD, SMA20, SMA50 and SMA200. " +
        "Use this function when asked about a stock's technical indicators.")]
    public async Task<string> GetTechnicalIndicatorsAsync(
        [Description("NSE stock symbol, such as RELIANCE, TCS or INFY")]
        string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();

        var connectionString =
            Environment.GetEnvironmentVariable(
                "NSE_STOCKS_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set NSE_STOCKS_CONNECTION_STRING before running the application.");
        }

        const string sql = """
            SELECT
                c.nse_symbol,
                ti.trade_date,
                ti.rsi14,
                ti.macd,
                ti.sma20,
                ti.sma50,
                ti.sma200
            FROM companies AS c
            JOIN technical_indicators AS ti
                ON ti.company_id = c.id
            WHERE UPPER(c.nse_symbol) = @symbol
            ORDER BY ti.trade_date DESC
            LIMIT 1;
            """;

        await using var connection =
            new NpgsqlConnection(connectionString);

        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("symbol", symbol);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return $"No stored technical indicator record was found for {symbol}.";
        }

        var actualSymbol = reader.GetString(
            reader.GetOrdinal("nse_symbol"));

        var tradeDate = reader.GetDateTime(
            reader.GetOrdinal("trade_date"));

        string Value(string column)
        {
            var value = reader[column];

            return value is DBNull
                ? "N/A"
                : Convert.ToString(
                    value, CultureInfo.InvariantCulture) ?? "N/A";
        }

        return $"""
            Source: PostgreSQL technical_indicators table
            Symbol: {actualSymbol}
            Indicator date: {tradeDate:yyyy-MM-dd}
            RSI (14): {Value("rsi14")}
            MACD: {Value("macd")}
            SMA (20): {Value("sma20")}
            SMA (50): {Value("sma50")}
            SMA (200): {Value("sma200")}
            """;
    }
}