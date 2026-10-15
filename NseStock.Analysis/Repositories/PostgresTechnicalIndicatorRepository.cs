using System.Globalization;
using Npgsql;
using NseStock.Analysis.Contracts;
using NseStock.Analysis.Models;

namespace NseStock.Infrastructure.Repositories;

public sealed class PostgresTechnicalIndicatorRepository
    : ITechnicalIndicatorRepository
{
    private readonly string _connectionString;

    public PostgresTechnicalIndicatorRepository(
        string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<TechnicalIndicatorSnapshot?> GetLatestAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "Stock symbol is required.",
                nameof(symbol));
        }

        symbol = symbol.Trim().ToUpperInvariant();

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
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("symbol", symbol);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        double? ReadNullableDouble(string column)
        {
            var value = reader[column];

            return value is DBNull
                ? null
                : Convert.ToDouble(
                    value, CultureInfo.InvariantCulture);
        }

        return new TechnicalIndicatorSnapshot(
            Symbol: reader.GetString(
                reader.GetOrdinal("nse_symbol")),
            TradeDate: reader.GetDateTime(
                reader.GetOrdinal("trade_date")),
            Rsi14: ReadNullableDouble("rsi14"),
            Macd: ReadNullableDouble("macd"),
            Sma20: ReadNullableDouble("sma20"),
            Sma50: ReadNullableDouble("sma50"),
            Sma200: ReadNullableDouble("sma200")
        );
    }
}