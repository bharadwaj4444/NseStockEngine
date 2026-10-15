
using NseStock.Infrastructure.Nse;

if (args.Length < 2 ||
    !DateOnly.TryParse(args[0], out var fromDate) ||
    !DateOnly.TryParse(args[1], out var toDate))
{
    Console.WriteLine(
        "Usage: NseStock.DownloaderTest <from-date> <to-date>");
    Console.WriteLine(
        "Example: NseStock.DownloaderTest 2026-09-25 2026-09-25");
    return;
}

var rawDataDirectory = Path.GetFullPath(
    Path.Combine(Environment.CurrentDirectory, "data", "raw"));


using var handler = new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer(),
    AutomaticDecompression =
        System.Net.DecompressionMethods.GZip |
        System.Net.DecompressionMethods.Deflate |
        System.Net.DecompressionMethods.Brotli
};

using var httpClient = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(60)
};


httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
    "AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36");

var options = new NseFileDownloaderOptions
{
    RawDataDirectory = rawDataDirectory,
    MaxRetries = 4,
    RetryDelay = TimeSpan.FromSeconds(2)
};

var downloader = new NseFileDownloader(httpClient, options);

Console.WriteLine($"Raw data directory: {rawDataDirectory}");
Console.WriteLine($"Requested range: {fromDate} to {toDate}");

var report = await downloader.DownloadRangeAsync(
    fromDate, toDate);

Console.WriteLine();
Console.WriteLine("Download summary:");
Console.WriteLine($"Equity files downloaded: {report.EquityFilesDownloaded}");
Console.WriteLine($"Equity files skipped: {report.EquityFilesSkipped}");
Console.WriteLine($"Equity dates unavailable: {report.EquityDatesUnavailable}");
Console.WriteLine($"NIFTY files downloaded: {report.BenchmarkFilesDownloaded}");
Console.WriteLine($"NIFTY files skipped: {report.BenchmarkFilesSkipped}");
