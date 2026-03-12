using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Service.Application.Csv;
using Service.Domain.Models;

namespace Service.Infrastructure.Csv;

/// <summary>
/// CSV parser for TRX data (day number, hour, trx value).
/// Supports both the simple 3-column format and the extended initial CSV format.
/// </summary>
public sealed class CsvParserService : ICsvParserService
{
    private const double InsufficientPromotersDeltaThreshold = -3;
    private const double IdleHoursDeltaThreshold = 5;

    /// <inheritdoc />
    public async Task<CsvParseResult> ParseAsync(
        Stream csvStream,
        IReadOnlySet<int>? selectedDays = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<TrxHourData>();
        var warnings = new List<string>();

        using var reader = new StreamReader(csvStream, leaveOpen: true);
        var lineNumber = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(';');
            if (!TryExtractRow(parts, out var dayText, out var hourText, out var trxText, out var trxDeltaText))
            {
                if (lineNumber == 1)
                {
                    continue;
                }

                warnings.Add($"Linea {lineNumber}: columnas insuficientes.");
                continue;
            }

            if (IsHeaderRow(dayText, hourText, trxText))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(dayText) && string.IsNullOrWhiteSpace(hourText))
            {
                continue;
            }

            if (!TryParseDay(dayText, out var dayNumber))
            {
                warnings.Add($"Linea {lineNumber}: dia invalido '{dayText}'.");
                continue;
            }

            if (selectedDays is not null && selectedDays.Count > 0 && !selectedDays.Contains(dayNumber))
            {
                warnings.Add($"Linea {lineNumber}: dia fuera de rango seleccionado '{dayNumber}'.");
                continue;
            }

            if (!TryParseHour(hourText, out var hour))
            {
                warnings.Add($"Linea {lineNumber}: hora invalida '{hourText}'.");
                continue;
            }

            if (!TryParseDouble(trxText, out var trxValue))
            {
                warnings.Add($"Linea {lineNumber}: trx invalido '{trxText}'.");
                continue;
            }

            double? trxDelta = null;
            if (!string.IsNullOrWhiteSpace(trxDeltaText) && TryParseDouble(trxDeltaText, out var delta))
            {
                trxDelta = delta;
                if (delta <= InsufficientPromotersDeltaThreshold)
                {
                    warnings.Add(
                        $"Linea {lineNumber}: promotores insuficientes (delta {delta.ToString("0.##", CultureInfo.InvariantCulture)}).");
                }
                else if (delta > IdleHoursDeltaThreshold)
                {
                    warnings.Add(
                        $"Linea {lineNumber}: horas de ocio altas (delta {delta.ToString("0.##", CultureInfo.InvariantCulture)}).");
                }
            }

            rows.Add(new TrxHourData(dayNumber, hour, trxValue, trxDelta));
        }

        return new CsvParseResult(rows, warnings);
    }

    private static bool TryParseDay(string value, out int dayNumber)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dayNumber))
        {
            return dayNumber is >= 1 and <= 7;
        }

        var normalized = NormalizeDayText(value);
        dayNumber = normalized switch
        {
            "monday" or "lunes" => 1,
            "tuesday" or "martes" => 2,
            "wednesday" or "miercoles" => 3,
            "thursday" or "jueves" => 4,
            "friday" or "viernes" => 5,
            "saturday" or "sabado" => 6,
            "sunday" or "domingo" => 7,
            _ => 0,
        };

        return dayNumber != 0;
    }

    private static bool TryParseHour(string value, out int hour)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour))
        {
            return hour is >= 0 and <= 23;
        }

        var normalized = value.Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var hourDouble))
        {
            var rounded = (int)Math.Round(hourDouble);
            if (Math.Abs(hourDouble - rounded) < 0.001 && rounded is >= 0 and <= 23)
            {
                hour = rounded;
                return true;
            }
        }

        var match = Regex.Match(value, @"\b(\d{1,2})\s*:");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out hour))
        {
            return false;
        }

        hour = NormalizeHourWithMeridiem(hour, value);
        return hour is >= 0 and <= 23;
    }

    private static bool TryParseDouble(string value, out double result)
    {
        var normalized = value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool IsHeaderRow(string dayText, string hourText, string trxText)
    {
        var day = dayText.ToLowerInvariant();
        var hour = hourText.ToLowerInvariant();
        var trx = trxText.ToLowerInvariant();

        return day.Contains("day") || day.Contains("dia")
            || hour.Contains("hour") || hour.Contains("hora")
            || trx.Contains("trx");
    }

    private static bool TryExtractRow(
        string[] parts,
        out string dayText,
        out string hourText,
        out string trxText,
        out string? trxDeltaText)
    {
        dayText = string.Empty;
        hourText = string.Empty;
        trxText = string.Empty;
        trxDeltaText = null;

        if (parts.Length < 3)
        {
            return false;
        }

        var simpleDay = parts[0].Trim();
        var simpleHour = parts[1].Trim();
        var simpleTrx = parts[2].Trim();

        if (LooksLikeData(simpleDay, simpleHour, simpleTrx) || parts.Length < 6)
        {
            dayText = simpleDay;
            hourText = simpleHour;
            trxText = simpleTrx;
            return true;
        }

        dayText = parts[3].Trim();
        hourText = parts[4].Trim();
        trxText = parts[5].Trim();

        if (parts.Length > 23)
        {
            trxDeltaText = parts[23].Trim();
        }

        return true;
    }

    private static bool LooksLikeData(string dayText, string hourText, string trxText)
    {
        var dayOk = TryParseDay(dayText, out _);
        var hourOk = TryParseHour(hourText, out _);
        var trxOk = TryParseDouble(trxText, out _);
        return dayOk && hourOk && trxOk;
    }

    private static string NormalizeDayText(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int NormalizeHourWithMeridiem(int hour, string rawText)
    {
        var cleaned = new string(rawText
            .ToLowerInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '.')
            .ToArray());

        var isPm = cleaned.Contains("pm") || cleaned.Contains("p.m");
        var isAm = cleaned.Contains("am") || cleaned.Contains("a.m");

        if (isPm && hour < 12)
        {
            return hour + 12;
        }

        if (isAm && hour == 12)
        {
            return 0;
        }

        return hour;
    }
}
