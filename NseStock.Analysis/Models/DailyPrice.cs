namespace NseStock.Analysis.Models;

public sealed record DailyPrice(
    string Symbol,
    DateOnly TradeDate,
    decimal? OpenPrice,
    decimal? HighPrice,
    decimal? LowPrice,
    decimal? ClosePrice,
    decimal? PreviousClose,
    long? Volume,
    decimal? TradedValue,
    long? TradesCount,
    long? DeliveryQuantity,
    decimal? DeliveryPercentage,
    decimal? Vwap
);