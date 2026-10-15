using Npgsql;
using NseStock.Analysis.Contracts;
using NseStock.Analysis.Models;

namespace NseStock.Infrastructure.Repositories;

public sealed class PostgresHistoricalPriceRepository
    : IHistoricalPriceRepository
{
    private readonly string _connectionString;

    public PostgresHistoricalPriceRepository(
        string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DailyPrice>> GetHistoryAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "Stock symbol is required.", nameof(symbol));
        }

        if (fromDate > toDate)
        {
            throw new ArgumentException(
                "The start date must not be after the end date.");
        }

        const string sql = """
            SELECT
                c.nse_symbol,
                dp.trade_date,
                dp.open_price,
                dp.high_price,
                dp.low_price,
                dp.close_price,
                dp.prev_close,
                dp.volume,
                dp.traded_value,
                dp.trades_count,
                dp.delivery_qty,
                dp.delivery_pct,
                dp.vwap
            FROM companies AS c
            JOIN daily_prices AS dp
                ON dp.company_id = c.id
            WHERE UPPER(c.nse_symbol) = @symbol
              AND dp.trade_date >= @from_date
              AND dp.trade_date <= @to_date
            ORDER BY dp.trade_date;
            """;

        var prices = new List<DailyPrice>();

        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "symbol", symbol.Trim().ToUpperInvariant());

        command.Parameters.AddWithValue("from_date", fromDate);
        command.Parameters.AddWithValue("to_date", toDate);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        int Ordinal(string name) => reader.GetOrdinal(name);

        decimal? ReadDecimal(string name)
        {
            int index = Ordinal(name);

            return reader.IsDBNull(index)
                ? null
                : reader.GetDecimal(index);
        }

        long? ReadInt64(string name)
        {
            int index = Ordinal(name);

            return reader.IsDBNull(index)
                ? null
                : reader.GetInt64(index);
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            prices.Add(new DailyPrice(
                Symbol: reader.GetString(Ordinal("nse_symbol")),
                TradeDate: reader.GetFieldValue<DateOnly>(
                    Ordinal("trade_date")),
                OpenPrice: ReadDecimal("open_price"),
                HighPrice: ReadDecimal("high_price"),
                LowPrice: ReadDecimal("low_price"),
                ClosePrice: ReadDecimal("close_price"),
                PreviousClose: ReadDecimal("prev_close"),
                Volume: ReadInt64("volume"),
                TradedValue: ReadDecimal("traded_value"),
                TradesCount: ReadInt64("trades_count"),
                DeliveryQuantity: ReadInt64("delivery_qty"),
                DeliveryPercentage: ReadDecimal("delivery_pct"),
                Vwap: ReadDecimal("vwap")
            ));
        }

        return prices;
    }
}