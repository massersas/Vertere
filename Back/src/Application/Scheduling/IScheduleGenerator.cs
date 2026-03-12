using Service.Domain.Models;

namespace Service.Application.Scheduling;

/// <summary>
/// Generates schedules based on TRX data and configuration.
/// </summary>
public interface IScheduleGenerator
{
    /// <summary>
    /// Generates a schedule for the provided TRX rows.
    /// </summary>
    /// <param name="rows">TRX input rows.</param>
    /// <param name="config">Schedule configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Generated schedule result.</returns>
    Task<ScheduleResult> GenerateAsync(
        IReadOnlyList<TrxHourData> rows,
        ScheduleConfig config,
        CancellationToken cancellationToken = default);
}
