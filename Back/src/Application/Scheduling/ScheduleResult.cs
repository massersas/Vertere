namespace Service.Application.Scheduling;

/// <summary>
/// Result of schedule generation.
/// </summary>
/// <param name="Rows">Generated schedule rows.</param>
/// <param name="Warnings">Validation warnings.</param>
public sealed record ScheduleResult(IReadOnlyList<ScheduleRow> Rows, IReadOnlyList<string> Warnings);
