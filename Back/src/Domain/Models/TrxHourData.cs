namespace Service.Domain.Models;

/// <summary>
/// TRX value for a specific day and hour.
/// </summary>
/// <param name="DayNumber">Day of week number (1=Monday, 7=Sunday).</param>
/// <param name="Hour">Hour of day (0-23).</param>
/// <param name="TrxValue">TRX value for the hour.</param>
/// <param name="TrxDelta">
/// Difference between expected TRX per person and actual TRX value.
/// Positive means idle time, negative means pending work.
/// </param>
public sealed record TrxHourData(int DayNumber, int Hour, double TrxValue, double? TrxDelta = null);
