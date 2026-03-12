namespace Service.Application.Scheduling;

/// <summary>
/// Configuration for schedule generation.
/// </summary>
/// <param name="PdvName">PDV name.</param>
/// <param name="PdvCode">PDV code.</param>
/// <param name="TrxAverage">Average TRX per promoter/hour.</param>
/// <param name="PromoterCount">Number of promoters available.</param>
/// <param name="SelectedDays">Optional day numbers (1-7).</param>
/// <param name="MinDelta">Minimum acceptable TRX delta per hour.</param>
/// <param name="MaxDelta">Maximum acceptable TRX delta per hour.</param>
/// <param name="WeekSeed">Deterministic seed for reproducible schedules (ej: año-semana ISO).</param>
public sealed record ScheduleConfig(
    string PdvName,
    string PdvCode,
    double TrxAverage,
    int PromoterCount,
    IReadOnlyList<int>? SelectedDays,
    double MinDelta,
    double MaxDelta,
    int WeekSeed);
