
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NseStock.Infrastructure.Nse;

public sealed class NseFileDownloader(
    HttpClient httpClient,
    NseFileDownloaderOptions options)
{
    private static readonly Uri NiftyHistoryPage =
        new("https://niftyindices.com/reports/historical-data");

    private static readonly Dictionary<string, string> NiftyHeaders = new()
    {
        ["User-Agent"] =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/153.0.0.0 Safari/537.36",

        ["Accept"] =
            "application/json, text/javascript, */*; q=0.01",

        ["Accept-Language"] = "en-US,en;q=0.9",

        ["Origin"] = "https://niftyindices.com",

        ["Content-Type"] = "application/json; charset=UTF-8",

        ["X-Requested-With"] = "XMLHttpRequest",

        ["Referer"] =
            "https://niftyindices.com/reports/historical-data"
    };

    private readonly HttpClient _httpClient =
        httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly NseFileDownloaderOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private bool _niftySessionInitialized;

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

        var unavailableDates =
            await ReadUnavailableDatesAsync(cancellationToken);

        // Equity archive: one ZIP file per weekday.
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

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

        // NIFTY history: cache one response per calendar month.
        // Requests cover the complete month, capped at today's date.
        var firstMonth = new DateOnly(fromDate.Year, fromDate.Month, 1);
        var lastMonth = new DateOnly(toDate.Year, toDate.Month, 1);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var month = firstMonth; month <= lastMonth;
             month = month.AddMonths(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = month;
            var end = month.AddMonths(1).AddDays(-1);

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
                    var age = DateOnly.FromDateTime(DateTime.Today)
                        .DayNumber - date.DayNumber;

                    // Recent files may appear later, so don't permanently
                    // mark recent 404/410 responses as unavailable.
                    if (age > 7)
                    {
                        await RecordUnavailableDateAsync(
                            date, cancellationToken);
                    }

                    return EquityDownloadResult.Unavailable;
                }

                if ((int)response.StatusCode >= 500
                    && attempt < _options.MaxRetries)
                {
                    await DelayBeforeRetryAsync(
                        attempt, cancellationToken);
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
            catch (HttpRequestException)
                when (attempt < _options.MaxRetries)
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
        await EnsureNiftySessionAsync(cancellationToken);

        var startText = start.ToString(
            "dd-MMM-yyyy", CultureInfo.InvariantCulture);

        var endText = end.ToString(
            "dd-MMM-yyyy", CultureInfo.InvariantCulture);

        // Match the Python payload: cinfo is a JSON string containing
        // the single-quoted date parameters expected by this endpoint.
        var cinfo =
            $"{{'name':'NIFTY 50'," +
            $"'startDate':'{startText}'," +
            $"'endDate':'{endText}'," +
            $"'indexName':'NIFTY 50'}}";

        var payload = JsonSerializer.Serialize(new { cinfo });
        var uri = new Uri(_options.NiftyHistoryUrl);

        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, uri);

                request.Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json");

                // Apply the same headers used by the Python request.
                AddNiftyHeaders(request);

                using var response = await _httpClient.SendAsync(
                    request, cancellationToken);

                if ((int)response.StatusCode >= 500
                    && attempt < _options.MaxRetries)
                {
                    await DelayBeforeRetryAsync(
                        attempt, cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                var contentType =
                    response.Content.Headers.ContentType?.MediaType;

                Console.WriteLine(
                    $"NIFTY response URL: {response.RequestMessage?.RequestUri}");

                Console.WriteLine(
                    $"NIFTY status: {(int)response.StatusCode} {response.StatusCode}");

                Console.WriteLine(
                    $"NIFTY content type: {contentType ?? "(missing)"}");

                Console.WriteLine(
                    $"NIFTY response preview:\n" +
                    json[..Math.Min(json.Length, 1000)]);

                if (contentType is null ||
                    !contentType.Contains(
                        "json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Expected JSON from NIFTY endpoint, received " +
                        $"'{contentType ?? "(missing)"}'.");
                }

                ValidateNiftyResponse(json, start, end);

                await WriteAtomicallyAsync(
                    destination,
                    Encoding.UTF8.GetBytes(json),
                    cancellationToken);

                return;
            }
            catch (HttpRequestException)
                when (attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested
                      && attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
            catch (InvalidDataException)
                when (attempt < _options.MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Failed to download NIFTY 50 history for " +
            $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd} after " +
            $"{_options.MaxRetries} attempts.");
    }

    private async Task EnsureNiftySessionAsync(
        CancellationToken cancellationToken)
    {
        if (_niftySessionInitialized)
            return;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, NiftyHistoryPage);

        AddNiftyHeaders(request);

        using var response = await _httpClient.SendAsync(
            request, cancellationToken);

        response.EnsureSuccessStatusCode();

        _niftySessionInitialized = true;
    }

    private static void AddNiftyHeaders(HttpRequestMessage request)
    {
        foreach (var header in NiftyHeaders)
        {
            // StringContent sets Content-Type on the content headers.
            if (header.Key.Equals(
                "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(
                header.Key, header.Value);
        }
    }

    private static void ValidateZip(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

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
        {
            throw new InvalidDataException(
                "NIFTY response does not contain the expected 'd' field.");
        }

        // Python accepts either a JSON-encoded string or already-parsed data.
        JsonDocument? innerDocument = null;

        try
        {
            JsonElement rows;

            if (dataElement.ValueKind == JsonValueKind.String)
            {
                var dataText = dataElement.GetString();

                if (string.IsNullOrWhiteSpace(dataText) ||
                    dataText.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "NIFTY endpoint returned no usable data.");
                }

                innerDocument = JsonDocument.Parse(dataText);
                rows = innerDocument.RootElement;
            }
            else
            {
                rows = dataElement;
            }

            if (rows.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "NIFTY response data is not a JSON array.");
            }

            if (rows.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    $"NIFTY endpoint returned no rows for {start} to {end}.");
            }

            bool hasClose = rows.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("CLOSE", out _));

            if (!hasClose)
            {
                throw new InvalidDataException(
                    "NIFTY response rows do not contain CLOSE values.");
            }
        }
        finally
        {
            innerDocument?.Dispose();
        }
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
                line,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
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
            date.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture)
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
}

public sealed record NseDownloadReport(
    int EquityFilesDownloaded,
    int EquityFilesSkipped,
    int EquityDatesUnavailable,
    int BenchmarkFilesDownloaded,
    int BenchmarkFilesSkipped);
