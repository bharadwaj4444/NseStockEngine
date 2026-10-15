
namespace NseStock.Infrastructure.Nse;

public sealed class NseFileDownloaderOptions
{
    public required string RawDataDirectory { get; init; }

    public int MaxRetries { get; init; } = 4;

    public TimeSpan RetryDelay { get; init; } =
        TimeSpan.FromSeconds(2);

    public string EquityArchiveBaseUrl { get; init; } =
        "https://nsearchives.nseindia.com/content/cm/";

    public string NiftyHistoryUrl { get; init; } =
        "https://www.niftyindices.com/Backpage.aspx/getHistoricaldatatabletoString";
}
