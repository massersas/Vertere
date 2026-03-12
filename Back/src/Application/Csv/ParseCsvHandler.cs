using Service.Application.Common;

namespace Service.Application.Csv;

/// <summary>
/// Handler for parsing CSV input.
/// </summary>
public sealed class ParseCsvHandler(ICsvParserService csvParserService)
{
    /// <summary>
    /// Handles the CSV parsing query.
    /// </summary>
    /// <param name="query">Query with CSV stream and optional days filter.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Result with parsed rows and warnings.</returns>
    public async Task<Result<CsvParseResult>> Handle(ParseCsvQuery query, CancellationToken cancellationToken)
    {
        var result = await csvParserService.ParseAsync(query.CsvStream, query.SelectedDays, cancellationToken);
        return Result<CsvParseResult>.Ok(result);
    }
}
