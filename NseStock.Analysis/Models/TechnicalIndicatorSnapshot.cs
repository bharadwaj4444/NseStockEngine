namespace NseStock.Analysis.Models;

public sealed record TechnicalIndicatorSnapshot(
    string Symbol,
    DateTime TradeDate,
    double? Rsi14,
    double? Macd,
    double? Sma20,
    double? Sma50,
    double? Sma200
);