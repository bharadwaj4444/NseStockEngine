using NseStock.Analysis.Models;

namespace NseStock.Analysis.Contracts;

public interface IHistoricalPriceRepository
{
    Task<IReadOnlyList<DailyPrice>> GetHistoryAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);
}