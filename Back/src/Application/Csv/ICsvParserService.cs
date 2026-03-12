namespace Service.Application.Csv;

/// <summary>
/// Parses CSV input for TRX data.
/// </summary>
public interface ICsvParserService
{
    /// <summary>
    /// Parses the CSV stream and returns structured TRX data.
    /// </summary>
    /// <param name="csvStream">CSV input stream.</param>
    /// <param name="selectedDays">Optional allowed day numbers (1-7).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Parsing result with rows and warnings.</returns>
    Task<CsvParseResult> ParseAsync(
        Stream csvStream,
        IReadOnlySet<int>? selectedDays = null,
        CancellationToken cancellationToken = default);
}
