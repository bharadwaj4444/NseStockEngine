using NseStock.Analysis.Models;

namespace NseStock.Analysis.Contracts;

public interface ITechnicalIndicatorRepository
{
    Task<TechnicalIndicatorSnapshot?> GetLatestAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}