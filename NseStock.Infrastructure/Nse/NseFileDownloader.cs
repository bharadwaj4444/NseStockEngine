
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NseStock.Infrastructure.Nse;

public sealed class NseFileDownloader(
    HttpClient httpClient,
    NseFileDownloaderOptions options)
{
    private static readonly Uri NiftyHistoryPage =
    new("https://www.niftyindices.com/reports/historical-data");

    private static readonly Uri NiftyHistoryApi =
        new("https://www.niftyindices.com/Backpage.aspx/getHistoricaldatatabletoString");

    private static readonly Dictionary<string, string> NiftyHeaders = new()
    {
        ["User-Agent"] =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/153.0.0.0 Safari/537.36",

        ["Accept"] =
            "application/json, text/javascript, */*; q=0.01",

        ["Accept-Language"] = "en-US,en;q=0.9",

        ["Content-Type"] = "application/json; charset=UTF-8",

        ["X-Requested-With"] = "XMLHttpRequest",

        ["Referer"] = "https://www.niftyindices.com/reports/historical-data"
    };

    private bool _niftySessionInitialized;

    private readonly HttpClient _httpClient =
        httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly NseFileDownloaderOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private string EquityDirectory =>
        Path.Combine(_options.RawDataDirectory, "equity");

    private string BenchmarkDirectory =>
        Path.Combine(_options.RawDataDirectory, "benchmark");

    private string UnavailableDatesFile =>
        Path.Combine(EquityDirectory, "unavailable-dates.txt");

    public async Task<NseDownloadReport> DownloadRangeAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
            throw new ArgumentException("fromDate must be <= toDate.");

        Directory.CreateDirectory(EquityDirectory);
        Directory.CreateDirectory(BenchmarkDirectory);

        int equityDownloaded = 0;
        int equitySkipped = 0;
        int equityUnavailable = 0;
        int benchmarkDownloaded = 0;
        int benchmarkSkipped = 0;

        var unavailableDates = await ReadUnavailableDatesAsync(
            cancellationToken);

        // Equity archive: one ZIP file per trading date.
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // NSE does not publish daily equity bhavcopy on weekends.
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                equitySkipped++;
                continue;
            }

            var fileName =
                $"BhavCopy_NSE_CM_0_0_0_{date:yyyyMMdd}_F_0000.csv.zip";

            var destination = Path.Combine(EquityDirectory, fileName);

            if (File.Exists(destination))
            {
                equitySkipped++;
                continue;
            }

            if (unavailableDates.Contains(date))
            {
                equityUnavailable++;
                continue;
            }

            var url = new Uri(
                new Uri(_options.EquityArchiveBaseUrl),
                fileName);

            var result = await DownloadEquityFileAsync(
                url, destination, date, cancellationToken);

            switch (result)
            {
                case EquityDownloadResult.Downloaded:
                    equityDownloaded++;
                    break;

                case EquityDownloadResult.Unavailable:
                    equityUnavailable++;
                    break;
            }
        }

        // NIFTY historical data: one cached JSON response per calendar month.
        // A complete month is cached so later runs can skip it reliably.
        var monthStart = new DateOnly(fromDate.Year, fromDate.Month, 1);
        var lastMonth = new DateOnly(toDate.Year, toDate.Month, 1);

        for (var month = monthStart; month <= lastMonth;
             month = month.AddMonths(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = month;
            var end = month.AddMonths(1).AddDays(-1);

            // Avoid requesting dates beyond the requested end or today.
            if (start < fromDate)
            {
                // Keep a complete calendar-month cache for consistency.
                // This may include dates earlier than fromDate.
            }

            if (end > toDate)
            {
                // Keep a complete calendar-month cache for consistency.
                // This may include dates later than toDate.
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (end > today)
                end = today;

            if (start > end)
                continue;

            var fileName = $"NIFTY50_{month:yyyyMM}.json";
            var destination = Path.Combine(BenchmarkDirectory, fileName);

            if (File.Exists(destination))
            {
                benchmarkSkipped++;
                continue;
            }

            await DownloadNiftyMonthAsync(
                start, end, destination, cancellationToken);

            benchmarkDownloaded++;
        }

        return new NseDownloadReport(
            equityDownloaded,
            equitySkipped,
            equityUnavailable,
            benchmarkDownloaded,
            benchmarkSkipped);
    }

    private async Task<EquityDownloadResult> DownloadEquityFileAsync(
        Uri url,
        string destination,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode is HttpStatusCode.NotFound
                    or HttpStatusCode.Gone)
                {
                    // Do not permanently cache a recent 404: NSE files can
                    // appear later in the day.
                    var age = DateOnly.FromDateTime(DateTime.Today)
                        .DayNumber - date.DayNumber;

                    if (age > 7)
                        await RecordUnavailableDateAsync(
                            date, cancellationToken);

                    return EquityDownloadResult.Unavailable;
                }

                if ((int)response.StatusCode >= 500 && attempt < _options.MaxRetries)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(
                    cancellationToken);

                ValidateZip(bytes);

                await WriteAtomicallyAsync(
                    destination, bytes, cancellationToken);

                return EquityDownloadResult.Downloaded;
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested
                      && attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Failed to download NSE bhavcopy for {date:yyyy-MM-dd} " +
            $"after {_options.MaxRetries} attempts: {url}");
    }

    private async Task DownloadNiftyMonthAsync(
        DateOnly start,
        DateOnly end,
        string destination,
        CancellationToken cancellationToken)
    {
        var startText = start.ToString(
            "dd-MMM-yyyy", CultureInfo.InvariantCulture);

        var endText = end.ToString(
            "dd-MMM-yyyy", CultureInfo.InvariantCulture);

        // This endpoint expects cinfo to contain a JSON-like string.
        var cinfo =
            $"{{'name':'NIFTY 50'," +
            $"'startDate':'{start.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}'," +
            $"'endDate':'{end.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}'," +
            $"'indexName':'NIFTY 50'}}";

        var payload = JsonSerializer.Serialize(new
        {
            cinfo
        });

        var uri = new Uri(_options.NiftyHistoryUrl);

        await EnsureNiftySessionAsync(cancellationToken);

        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, uri);

                request.Content = new StringContent(
                    payload, System.Text.Encoding.UTF8, "application/json");

                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                request.Headers.TryAddWithoutValidation(
                    "X-Requested-With", "XMLHttpRequest");

                request.Headers.Referrer = new Uri(
                    "https://www.niftyindices.com/reports/historical-data");

                using var response = await _httpClient.SendAsync(
                    request, cancellationToken);

                if ((int)response.StatusCode >= 500
                    && attempt < _options.MaxRetries)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                var contentType = response.Content.Headers.ContentType?.MediaType;

                Console.WriteLine($"NIFTY response URL: {response.RequestMessage?.RequestUri}");
                Console.WriteLine($"NIFTY status: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"NIFTY content type: {contentType}");
                Console.WriteLine($"NIFTY response preview:\n{json[..Math.Min(json.Length, 1500)]}");

                if (contentType is not null &&
                    !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    var preview = json.Length > 250
                        ? json[..250]
                        : json;

                    throw new InvalidDataException(
                        $"Expected JSON from NIFTY endpoint, received '{contentType}'. " +
                        $"Response preview: {preview}");
                }

                if (!json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    var preview = json.Length > 250
                        ? json[..250]
                        : json;

                    throw new InvalidDataException(
                        $"NIFTY endpoint did not return a JSON object. " +
                        $"Response preview: {preview}");
                }

                ValidateNiftyResponse(json, start, end);

                await WriteAtomicallyAsync(
                    destination,
                    System.Text.Encoding.UTF8.GetBytes(json),
                    cancellationToken);

                return;
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested
                      && attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Failed to download NIFTY 50 history for " +
            $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd} after " +
            $"{_options.MaxRetries} attempts.");
    }

    private static void ValidateZip(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(
            stream, ZipArchiveMode.Read);

        if (archive.Entries.Count == 0)
            throw new InvalidDataException("NSE ZIP contains no entries.");
    }

    private static void ValidateNiftyResponse(
        string json,
        DateOnly start,
        DateOnly end)
    {
        using var outer = JsonDocument.Parse(json);

        if (!outer.RootElement.TryGetProperty("d", out var dataElement))
            throw new InvalidDataException(
                "NIFTY response does not contain the expected 'd' field.");

        var dataText = dataElement.GetString();

        if (string.IsNullOrWhiteSpace(dataText) || dataText == "false")
            throw new InvalidDataException(
                "NIFTY endpoint returned no usable data.");

        using var inner = JsonDocument.Parse(dataText);

        if (inner.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "NIFTY response data is not a JSON array.");

        if (inner.RootElement.GetArrayLength() == 0)
            throw new InvalidDataException(
                $"NIFTY endpoint returned no rows for {start} to {end}.");

        var hasClose = inner.RootElement.EnumerateArray().Any(
            item => item.TryGetProperty("CLOSE", out _));

        if (!hasClose)
            throw new InvalidDataException(
                "NIFTY response rows do not contain CLOSE values.");
    }

    private async Task<HashSet<DateOnly>> ReadUnavailableDatesAsync(
        CancellationToken cancellationToken)
    {
        var dates = new HashSet<DateOnly>();

        if (!File.Exists(UnavailableDatesFile))
            return dates;

        foreach (var line in await File.ReadAllLinesAsync(
                     UnavailableDatesFile, cancellationToken))
        {
            if (DateOnly.TryParseExact(
                    line, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    private async Task RecordUnavailableDateAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await File.AppendAllTextAsync(
            UnavailableDatesFile,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + Environment.NewLine,
            cancellationToken);
    }

    private async Task DelayBeforeRetryAsync(
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(
            _options.RetryDelay.TotalMilliseconds * attempt);

        await Task.Delay(delay, cancellationToken);
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        var temporary = destination + ".download";

        await File.WriteAllBytesAsync(
            temporary, bytes, cancellationToken);

        File.Move(temporary, destination, overwrite: true);
    }

    private enum EquityDownloadResult
    {
        Downloaded,
        Unavailable
    }

    private async Task EnsureNiftySessionAsync(
    CancellationToken cancellationToken)
    {
        if (_niftySessionInitialized)
            return;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            NiftyHistoryPage);

        AddNiftyHeaders(request);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        _niftySessionInitialized = true;
    }

    private static void AddNiftyHeaders(
        HttpRequestMessage request)
    {
        foreach (var header in NiftyHeaders)
        {
            if (header.Key.Equals(
                    "Content-Type",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value);
        }
    }

}

public sealed record NseDownloadReport(
    int EquityFilesDownloaded,
    int EquityFilesSkipped,
    int EquityDatesUnavailable,
    int BenchmarkFilesDownloaded,
    int BenchmarkFilesSkipped
    );
