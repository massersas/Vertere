namespace Service.Application.Scheduling;

/// <summary>
/// Schedule row for a specific day/hour.
/// </summary>
/// <param name="DayNumber">Day of week number (1=Monday, 7=Sunday).</param>
/// <param name="Hour">Hour of day (0-23).</param>
/// <param name="TrxValue">TRX value for the hour.</param>
/// <param name="TrxDelta">Difference between expected TRX per person and actual TRX value.</param>
/// <param name="RequiredStaff">Promoters required for this hour.</param>
/// <param name="AssignedPromoters">Promoter ids assigned to this hour.</param>
public sealed record ScheduleRow(
    int DayNumber,
    int Hour,
    double TrxValue,
    double? TrxDelta,
    int RequiredStaff,
    IReadOnlyList<int> AssignedPromoters);
