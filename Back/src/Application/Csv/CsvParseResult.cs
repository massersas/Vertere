using Service.Domain.Models;

namespace Service.Application.Csv;

/// <summary>
/// Result of CSV parsing.
/// </summary>
/// <param name="Rows">Parsed TRX rows.</param>
/// <param name="Warnings">Parsing warnings.</param>
public sealed record CsvParseResult(IReadOnlyList<TrxHourData> Rows, IReadOnlyList<string> Warnings);
