namespace Service.Application.Csv;

/// <summary>
/// Query for parsing CSV input.
/// </summary>
/// <param name="CsvStream">CSV stream.</param>
/// <param name="SelectedDays">Optional allowed day numbers (1-7).</param>
public sealed record ParseCsvQuery(Stream CsvStream, IReadOnlySet<int>? SelectedDays);
