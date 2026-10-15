using NseStock.Infrastructure;
using NseStock.Analysis.Contracts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("NseStocks")
    ?? Environment.GetEnvironmentVariable(
        "NSE_STOCKS_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure the NseStocks connection string.");
}

builder.Services.AddMemoryCache();

builder.Services.AddNseStockInfrastructure(connectionString);
builder.Services.AddNpgsqlDataSource(connectionString);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapGet(
    "/api/stocks/{symbol}/indicators/latest",
    async (
        string symbol,
        ITechnicalIndicatorRepository repository,
        CancellationToken cancellationToken) =>
    {
        var result = await repository.GetLatestAsync(
            symbol,
            cancellationToken);

        return result is null
            ? Results.NotFound(new
            {
                message = $"No technical indicators found for {symbol}."
            })
            : Results.Ok(result);
    })
    .WithName("GetLatestTechnicalIndicators")
    .WithSummary("Get the latest stored technical indicators for an NSE stock");

app.MapGet(
    "/api/stocks/{symbol}/prices",
    async (
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        IHistoricalPriceRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (fromDate > toDate)
        {
            return Results.BadRequest(new
            {
                message = "fromDate must be on or before toDate."
            });
        }

        var prices = await repository.GetHistoryAsync(
            symbol,
            fromDate,
            toDate,
            cancellationToken);

        return Results.Ok(prices);
    })
    .WithName("GetHistoricalPrices")
    .WithSummary("Get historical daily prices for an NSE stock");

app.Run();

