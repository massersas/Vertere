using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Service.Application.Common;
using Service.Application.Csv;
using Service.Application.Scheduling;
using Wolverine;

namespace Service.Api.Controllers;

/// <summary>
/// CSV endpoints for TRX input.
/// </summary>
[ApiController]
[Route("api/csv")]
public sealed class CsvController(IMessageBus messageBus, IScheduleGenerator scheduleGenerator) : ControllerBase
{
    private const double DefaultMinDelta = -3;
    private const double DefaultMaxDelta = 30;
    private const int MaxWeeklyHours = 44;
    /// <summary>
    /// Parses a CSV file and generates the schedule preview or CSV output.
    /// </summary>
    /// <param name="request">CSV request with file and optional day filters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Schedule preview or CSV output.</returns>
    [HttpPost("parse")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Parse(
        [FromForm] CsvParseRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new SchedulePreviewResponse(false, Array.Empty<ScheduleRowDto>(), new[] { "Archivo CSV vacio." }, 0));
        }

        await using var stream = request.File.OpenReadStream();
        var daysSet = ParseSelectedDaysFlexible(request.SelectedDays);
        var query = new ParseCsvQuery(stream, null);
        var result = await messageBus.InvokeAsync<Result<CsvParseResult>>(query, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return BadRequest(new SchedulePreviewResponse(false, Array.Empty<ScheduleRowDto>(), new[] { result.ErrorMessage ?? "Error al procesar CSV." }, 0));
        }

        var errors = ValidatePdvRequest(request, out var config, out var minDelta, out var maxDelta);
        if (errors.Count > 0)
        {
            return BadRequest(new SchedulePreviewResponse(false, Array.Empty<ScheduleRowDto>(), errors, 0));
        }

        var schedule = await scheduleGenerator.GenerateAsync(result.Value.Rows, config!, cancellationToken);
        var combinedWarnings = result.Value.Warnings.Concat(schedule.Warnings).ToList();
        var outputRows = FilterRows(schedule.Rows, daysSet);
        var optimizationPercent = CalculateOptimization(schedule.Rows, config!.PromoterCount);

        if (WantsCsv())
        {
            var csv = BuildScheduleCsv(
                outputRows,
                config!.PromoterCount,
                config.TrxAverage,
                minDelta,
                maxDelta,
                combinedWarnings);
            var bytes = Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", $"schedule_{config.PdvCode}.csv");
        }

        var previewRows = outputRows
            .Select(row =>
            {
                var assignedCount = row.AssignedPromoters.Count;
                var computedDelta = ComputeDelta(row, config!.TrxAverage, assignedCount);
                var comment = ComputeDeltaComment(computedDelta, minDelta, maxDelta);
                AppendDeltaWarning(combinedWarnings, row.DayNumber, row.Hour, computedDelta, comment, minDelta, maxDelta);
                return new ScheduleRowDto(
                    row.DayNumber,
                    row.Hour,
                    row.TrxValue,
                    row.TrxDelta,
                    computedDelta,
                    comment,
                    row.RequiredStaff,
                    row.AssignedPromoters.Select(id => $"Promotor {id}").ToArray());
            })
            .ToArray();

        return Ok(new SchedulePreviewResponse(true, previewRows, combinedWarnings.ToArray(), optimizationPercent));
    }

    /// <summary>
    /// Accepts PDV metadata without a CSV file.
    /// </summary>
    /// <param name="request">PDV metadata payload.</param>
    /// <returns>Accepted PDV metadata.</returns>
    [HttpPost("parse")]
    [Consumes("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PdvParseResponse> Parse([FromBody] PdvParseRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.PdvName))
        {
            errors.Add("PdvName es requerido.");
        }

        if (string.IsNullOrWhiteSpace(request.PdvCode))
        {
            errors.Add("PdvCode es requerido.");
        }

        if (request.TrxAverage <= 0)
        {
            errors.Add("TrxAverage debe ser mayor a 0.");
        }

        if (request.PromoterCount < 0)
        {
            errors.Add("PromoterCount no puede ser negativo.");
        }

        if (request.SelectedDays is { Count: > 0 } &&
            request.SelectedDays.Any(day => day is < 1 or > 7))
        {
            errors.Add("SelectedDays solo puede contener valores entre 1 y 7.");
        }

        if (errors.Count > 0)
        {
            return BadRequest(new PdvParseResponse(false, request, errors));
        }

        var daysSet = ParseSelectedDays(request.SelectedDays);
        var normalized = request with { SelectedDays = daysSet?.ToArray() };
        return Ok(new PdvParseResponse(true, normalized, Array.Empty<string>()));
    }

    private static List<string> ValidatePdvRequest(
        CsvParseRequest request,
        out ScheduleConfig? config,
        out double minDelta,
        out double maxDelta)
    {
        var errors = new List<string>();
        config = null;
        minDelta = DefaultMinDelta;
        maxDelta = DefaultMaxDelta;
        var weekSeed = GetIsoWeekSeed(DateTime.UtcNow);

        if (string.IsNullOrWhiteSpace(request.PdvName))
        {
            errors.Add("PdvName es requerido.");
        }

        if (string.IsNullOrWhiteSpace(request.PdvCode))
        {
            errors.Add("PdvCode es requerido.");
        }

        if (!TryParseDouble(request.TrxAverage, out var trxAverage) || trxAverage <= 0)
        {
            errors.Add("TrxAverage debe ser mayor a 0.");
        }

        if (request.PromoterCount is null || request.PromoterCount < 1)
        {
            errors.Add("PromoterCount debe ser mayor o igual a 1.");
        }

        if (!string.IsNullOrWhiteSpace(request.MinDelta))
        {
            if (!TryParseDouble(request.MinDelta, out minDelta))
            {
                errors.Add("MinDelta inválido.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.MaxDelta))
        {
            if (!TryParseDouble(request.MaxDelta, out maxDelta))
            {
                errors.Add("MaxDelta inválido.");
            }
        }

        if (minDelta > maxDelta)
        {
            errors.Add("MinDelta no puede ser mayor que MaxDelta.");
        }

        if (!string.IsNullOrWhiteSpace(request.WeekSeed) &&
            !int.TryParse(request.WeekSeed, out weekSeed))
        {
            errors.Add("WeekSeed inválido.");
        }

        var selectedDays = ParseSelectedDaysFlexible(request.SelectedDays);
        if (selectedDays is { Count: > 0 } &&
            selectedDays.Any(day => day is < 1 or > 7))
        {
            errors.Add("SelectedDays solo puede contener valores entre 1 y 7.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        config = new ScheduleConfig(
            request.PdvName!,
            request.PdvCode!,
            trxAverage,
            request.PromoterCount!.Value,
            null,
            minDelta,
            maxDelta,
            weekSeed);

        return errors;
    }

    private bool WantsCsv()
    {
        if (Request.Query.TryGetValue("format", out var format) &&
            string.Equals(format.ToString(), "csv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = Request.Headers.Accept.ToString();
        return accept.Contains("text/csv", StringComparison.OrdinalIgnoreCase)
            || accept.Contains("application/csv", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildScheduleCsv(
        IReadOnlyList<ScheduleRow> rows,
        int promoterCount,
        double trxAverage,
        double minDelta,
        double maxDelta,
        IList<string> warnings)
    {
        var sb = new StringBuilder();
        var suggestionNotes = BuildCoverageSuggestions(rows);
        if (!string.IsNullOrWhiteSpace(suggestionNotes))
        {
            sb.AppendLine($"Sugerencia: {suggestionNotes}");
        }

        sb.Append("DayNumber;Hour;Trx");
        for (var i = 1; i <= promoterCount; i++)
        {
            sb.Append($";Promotor {i}");
        }
        sb.Append(";TrxDelta;Comentario;No Promotores por hora");
        sb.AppendLine();

        foreach (var row in rows.OrderBy(r => r.DayNumber).ThenBy(r => r.Hour))
        {
            sb.Append(row.DayNumber);
            sb.Append(';');
            sb.Append(row.Hour);
            sb.Append(';');
            sb.Append(FormatNumber(row.TrxValue));

            var assigned = row.AssignedPromoters.ToHashSet();
            for (var i = 1; i <= promoterCount; i++)
            {
                sb.Append(';');
                if (assigned.Contains(i))
                {
                    sb.Append('X');
                }
            }

            var assignedCount = row.AssignedPromoters.Count;
            var computedDelta = ComputeDelta(row, trxAverage, assignedCount);
            var comment = ComputeDeltaComment(computedDelta, minDelta, maxDelta);
            AppendDeltaWarning(warnings, row.DayNumber, row.Hour, computedDelta, comment, minDelta, maxDelta);

            sb.Append(';');
            sb.Append(FormatNumber(computedDelta));
            sb.Append(';');
            sb.Append(comment);
            sb.Append(';');
            sb.Append(assignedCount);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IReadOnlyList<ScheduleRow> FilterRows(
        IReadOnlyList<ScheduleRow> rows,
        IReadOnlyCollection<int>? selectedDays)
    {
        if (selectedDays is null || selectedDays.Count == 0)
        {
            return rows;
        }

        var allowed = selectedDays.ToHashSet();
        return rows.Where(row => allowed.Contains(row.DayNumber)).ToArray();
    }

    private static double CalculateOptimization(IReadOnlyList<ScheduleRow> rows, int promoterCount)
    {
        if (promoterCount <= 0)
        {
            return 0;
        }

        var totalAssigned = rows.Sum(row => row.AssignedPromoters.Count);
        var capacity = promoterCount * MaxWeeklyHours;
        if (capacity <= 0)
        {
            return 0;
        }

        return Math.Round((totalAssigned / (double)capacity) * 100, 2);
    }

    private static double ComputeDelta(ScheduleRow row, double trxAverage, int promoterCount)
        => (promoterCount * trxAverage) - row.TrxValue;

    private static string ComputeDeltaComment(double delta, double minDelta, double maxDelta)
    {
        if (delta <= minDelta)
        {
            return "Insuficiente";
        }

        if (delta <= 0)
        {
            return "Normalizado";
        }

        if (delta <= maxDelta)
        {
            return "Ocio bajo";
        }

        return "Ocio alto";
    }

    private static void AppendDeltaWarning(
        IList<string> warnings,
        int day,
        int hour,
        double delta,
        string comment,
        double minDelta,
        double maxDelta)
    {
        if (delta <= minDelta || delta > maxDelta)
        {
            warnings.Add($"Dia {day} hora {hour}: {comment.ToLowerInvariant()} (delta {delta.ToString("0.##", CultureInfo.InvariantCulture)}).");
        }
    }

    private static string BuildCoverageSuggestions(IReadOnlyList<ScheduleRow> rows)
    {
        var shortages = rows
            .Where(row => row.TrxValue > 0 && row.AssignedPromoters.Count == 0)
            .OrderBy(row => row.DayNumber)
            .ThenBy(row => row.Hour)
            .ToArray();

        if (shortages.Length == 0)
        {
            return string.Empty;
        }

        return $"revisar cobertura en {string.Join(", ", shortages.Select(row => $"dia {row.DayNumber} hora {row.Hour}"))}";
    }

    private static string FormatNumber(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static bool TryParseDouble(string? value, out double result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0;
            return false;
        }

        var normalized = value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static IReadOnlySet<int>? ParseSelectedDays(string? selectedDays)
    {
        if (string.IsNullOrWhiteSpace(selectedDays))
        {
            return null;
        }

        var values = selectedDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var day) ? day : -1)
            .Where(day => day is >= 1 and <= 7)
            .Distinct()
            .ToHashSet();

        return values.Count == 0 ? null : values;
    }

    private static int GetIsoWeekSeed(DateTime date)
    {
        var cal = CultureInfo.InvariantCulture.Calendar;
        var week = cal.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return (date.Year * 100) + week;
    }

    private static IReadOnlySet<int>? ParseSelectedDays(IReadOnlyCollection<int>? selectedDays)
    {
        if (selectedDays is null || selectedDays.Count == 0)
        {
            return null;
        }

        var values = selectedDays
            .Where(day => day is >= 1 and <= 7)
            .Distinct()
            .ToHashSet();

        return values.Count == 0 ? null : values;
    }

    private static IReadOnlyCollection<int>? ParseSelectedDaysFlexible(string? selectedDays)
    {
        if (string.IsNullOrWhiteSpace(selectedDays))
        {
            return null;
        }

        var trimmed = selectedDays.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var values = JsonSerializer.Deserialize<int[]>(trimmed);
                return values is null || values.Length == 0 ? null : values;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return ParseSelectedDays(trimmed);
    }
}

/// <summary>
/// CSV parse request payload.
/// </summary>
public sealed class CsvParseRequest
{
    /// <summary>
    /// CSV file to parse.
    /// </summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>
    /// Optional comma-separated day numbers (e.g. 1,2,3,4).
    /// </summary>
    [FromForm(Name = "selectedDays")]
    public string? SelectedDays { get; init; }

    /// <summary>
    /// PDV name.
    /// </summary>
    [FromForm(Name = "pdvName")]
    public string? PdvName { get; init; }

    /// <summary>
    /// PDV code.
    /// </summary>
    [FromForm(Name = "pdvCode")]
    public string? PdvCode { get; init; }

    /// <summary>
    /// Average TRX per promoter/hour.
    /// </summary>
    [FromForm(Name = "trxAverage")]
    public string? TrxAverage { get; init; }

    /// <summary>
    /// Number of promoters.
    /// </summary>
    [FromForm(Name = "promoterCount")]
    public int? PromoterCount { get; init; }

    /// <summary>
    /// Optional note.
    /// </summary>
    [FromForm(Name = "note")]
    public string? Note { get; init; }

    /// <summary>
    /// Optional minimum delta threshold.
    /// </summary>
    [FromForm(Name = "minDelta")]
    public string? MinDelta { get; init; }

    /// <summary>
    /// Optional maximum delta threshold.
    /// </summary>
    [FromForm(Name = "maxDelta")]
    public string? MaxDelta { get; init; }

    /// <summary>
    /// Optional deterministic seed (ISO week) for reproducible schedules.
    /// </summary>
    [FromForm(Name = "weekSeed")]
    public string? WeekSeed { get; init; }
}

/// <summary>
/// PDV metadata request payload.
/// </summary>
/// <param name="PdvName">PDV name.</param>
/// <param name="PdvCode">PDV code.</param>
/// <param name="TrxAverage">Average TRX.</param>
/// <param name="PromoterCount">Promoter count.</param>
/// <param name="SelectedDays">Optional day numbers (1-7).</param>
/// <param name="Note">Optional note.</param>
public sealed record PdvParseRequest(
    string PdvName,
    string PdvCode,
    double TrxAverage,
    int PromoterCount,
    IReadOnlyList<int>? SelectedDays,
    string? Note);

/// <summary>
/// PDV parse response payload.
/// </summary>
/// <param name="Success">Indicates if the request was accepted.</param>
/// <param name="Data">Normalized PDV metadata.</param>
/// <param name="Warnings">Validation warnings.</param>
public sealed record PdvParseResponse(bool Success, PdvParseRequest Data, IReadOnlyList<string> Warnings);

/// <summary>
/// Schedule preview response payload.
/// </summary>
/// <param name="Success">Indicates if scheduling succeeded.</param>
/// <param name="Rows">Preview rows.</param>
/// <param name="Warnings">Scheduling warnings.</param>
/// <param name="OptimizationPercent">Optimization percent (assigned hours vs capacity).</param>
public sealed record SchedulePreviewResponse(
    bool Success,
    IReadOnlyList<ScheduleRowDto> Rows,
    IReadOnlyList<string> Warnings,
    double OptimizationPercent);

/// <summary>
/// Schedule row DTO.
/// </summary>
/// <param name="DayNumber">Day of week number (1=Monday, 7=Sunday).</param>
/// <param name="Hour">Hour of day (0-23).</param>
/// <param name="TrxValue">TRX value for the hour.</param>
/// <param name="TrxDelta">Difference between expected TRX per person and actual TRX value.</param>
/// <param name="ComputedDelta">Delta computed from assigned promoters and TRX average.</param>
/// <param name="Comment">Delta classification comment.</param>
/// <param name="RequiredStaff">Promoters required for this hour.</param>
/// <param name="AssignedPromoters">Promoters assigned for this hour.</param>
public sealed record ScheduleRowDto(
    int DayNumber,
    int Hour,
    double TrxValue,
    double? TrxDelta,
    double ComputedDelta,
    string Comment,
    int RequiredStaff,
    IReadOnlyList<string> AssignedPromoters);
