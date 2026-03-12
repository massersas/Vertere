using System.Linq;
using Service.Domain.Models;

namespace Service.Application.Scheduling;

/// <summary>
/// Generates schedules based on TRX data and business rules.
/// </summary>
public sealed class ScheduleGenerator : IScheduleGenerator
{
    private const int MinShiftHours = 4;
    private const int MaxShiftHours = 9;
    private const int MaxWeeklyHours = 44;
    private const int MinRestHours = 8;
    private const int MinPromotersPerHour = 1;
    private const int MinCoverageWeight = 200;
    private const int TargetCoverageWeight = 30;
    private const int ExtraCoverageWeight = 6;
    private const int OverCoveragePenalty = 25;

    /// <inheritdoc />
    public Task<ScheduleResult> GenerateAsync(
        IReadOnlyList<TrxHourData> rows,
        ScheduleConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);

        var baseSeed = HashCode.Combine(config.PdvCode, config.WeekSeed, config.PromoterCount);
        var warnings = new List<string>();
        var scheduleRows = new List<ScheduleRow>();

        if (rows.Count == 0)
        {
            return Task.FromResult(new ScheduleResult(Array.Empty<ScheduleRow>(), new[] { "CSV sin filas válidas." }));
        }

        var orderedRows = rows
            .OrderBy(row => row.DayNumber)
            .ThenBy(row => row.Hour)
            .ToArray();

        var selectedDays = config.SelectedDays is { Count: > 0 }
            ? new HashSet<int>(config.SelectedDays)
            : orderedRows.Select(r => r.DayNumber).ToHashSet();
        var enforceRestDay = true;

        var rowLookup = orderedRows.ToDictionary(row => (row.DayNumber, row.Hour));
        var requiredLookup = orderedRows.ToDictionary(
            row => (row.DayNumber, row.Hour),
            row => CalculateRequiredStaff(row.TrxValue, config.TrxAverage));
        var minCoverageLookup = orderedRows.ToDictionary(
            row => (row.DayNumber, row.Hour),
            row => CalculateMinCoverage(row.TrxValue, config.TrxAverage, config.MinDelta));
        var maxCoverageLookup = orderedRows.ToDictionary(
            row => (row.DayNumber, row.Hour),
            row => CalculateMaxCoverage(
                row.TrxValue,
                config.TrxAverage,
                config.MaxDelta,
                minCoverageLookup[(row.DayNumber, row.Hour)]));
        var restDays = AssignRestDays(
            config.PromoterCount,
            selectedDays,
            minCoverageLookup,
            baseSeed);
        var promoters = BuildPromoters(restDays);

        var assignments = new Dictionary<(int Day, int Hour), List<int>>();
        var targetTotalHours = orderedRows
            .Where(row => selectedDays.Contains(row.DayNumber))
            .Sum(row => requiredLookup[(row.DayNumber, row.Hour)]);
        var remainingBudget = targetTotalHours;

        var daysToPlan = selectedDays
            .OrderBy(day => day)
            .ToList();

        foreach (var day in daysToPlan)
        {
            var nextDayNeedsEarly = selectedDays.Contains(day + 1) &&
                                    HasEarlyCoverageNeed(minCoverageLookup, day + 1);
            PlanDay(
                day,
                requiredLookup,
                minCoverageLookup,
                maxCoverageLookup,
                enforceRestDay,
                nextDayNeedsEarly,
                baseSeed,
                promoters,
                warnings,
                assignments,
                ref remainingBudget);
        }

        foreach (var row in orderedRows)
        {
            if (!selectedDays.Contains(row.DayNumber))
            {
                continue;
            }

            var required = requiredLookup[(row.DayNumber, row.Hour)];
            assignments.TryGetValue((row.DayNumber, row.Hour), out var assignedPromoters);
            assignedPromoters ??= new List<int>();

            if (assignedPromoters.Count < required)
            {
                warnings.Add($"Dia {row.DayNumber} hora {row.Hour}: promotores insuficientes ({assignedPromoters.Count}/{required}).");
            }
            else if (assignedPromoters.Count > required)
            {
                warnings.Add($"Dia {row.DayNumber} hora {row.Hour}: sobrecupo ({assignedPromoters.Count}/{required}).");
            }

            scheduleRows.Add(new ScheduleRow(
                row.DayNumber,
                row.Hour,
                row.TrxValue,
                row.TrxDelta,
                required,
                assignedPromoters));
        }

        return Task.FromResult(new ScheduleResult(scheduleRows, warnings));
    }

    private static int CalculateRequiredStaff(double trxValue, double trxAverage)
    {
        if (trxAverage <= 0)
        {
            return MinPromotersPerHour;
        }

        var required = (int)Math.Ceiling(trxValue / trxAverage);
        if (required < MinPromotersPerHour)
        {
            required = MinPromotersPerHour;
        }

        return required;
    }

    private static int CalculateMinCoverage(double trxValue, double trxAverage, double minDelta)
    {
        if (trxAverage <= 0)
        {
            return MinPromotersPerHour;
        }

        var minCoverage = (int)Math.Ceiling((trxValue + minDelta) / trxAverage);
        if (minCoverage < MinPromotersPerHour)
        {
            minCoverage = MinPromotersPerHour;
        }

        return minCoverage;
    }

    private static int CalculateMaxCoverage(double trxValue, double trxAverage, double maxDelta, int minCoverage)
    {
        if (trxAverage <= 0)
        {
            return minCoverage;
        }

        var maxCoverage = (int)Math.Floor((trxValue + maxDelta) / trxAverage);
        if (maxCoverage < minCoverage)
        {
            maxCoverage = minCoverage;
        }

        return maxCoverage;
    }

    private static List<PromoterState> BuildPromoters(IReadOnlyList<int> restDays)
    {
        var promoters = new List<PromoterState>(restDays.Count);
        for (var i = 1; i <= restDays.Count; i++)
        {
            promoters.Add(new PromoterState(i, $"Promotor {i}", restDays[i - 1]));
        }

        return promoters;
    }

    private static IReadOnlyList<int> AssignRestDays(
        int promoterCount,
        IReadOnlyCollection<int> selectedDays,
        IReadOnlyDictionary<(int Day, int Hour), int> minCoverageLookup,
        int baseSeed)
    {
        var allDays = Enumerable.Range(1, 7).ToArray();
        if (selectedDays.Count < 7)
        {
            var nonSelected = allDays.Except(selectedDays).ToArray();
            if (nonSelected.Length > 0)
            {
                var restDays = new int[promoterCount];
                for (var i = 0; i < promoterCount; i++)
                {
                    restDays[i] = nonSelected[i % nonSelected.Length];
                }

                return restDays;
            }
        }

        var demandByDay = new List<(int Day, int Demand)>(7);
        foreach (var day in allDays)
        {
            var demand = 0;
            for (var hour = 0; hour < 24; hour++)
            {
                if (minCoverageLookup.TryGetValue((day, hour), out var min))
                {
                    demand += min;
                }
            }

            demandByDay.Add((day, demand));
        }

        var orderedDays = demandByDay
            .OrderBy(entry => entry.Demand)
            .ThenBy(entry => GetDeterministicTieBreaker(baseSeed, entry.Day, entry.Day))
            .Select(entry => entry.Day)
            .ToArray();

        var result = new int[promoterCount];
        for (var i = 0; i < promoterCount; i++)
        {
            result[i] = orderedDays[i % orderedDays.Length];
        }

        return result;
    }

    private static void PlanDay(
        int day,
        IReadOnlyDictionary<(int Day, int Hour), int> requiredLookup,
        IReadOnlyDictionary<(int Day, int Hour), int> minCoverageLookup,
        IReadOnlyDictionary<(int Day, int Hour), int> maxCoverageLookup,
        bool enforceRestDay,
        bool nextDayNeedsEarly,
        int baseSeed,
        List<PromoterState> promoters,
        List<string> warnings,
        Dictionary<(int Day, int Hour), List<int>> assignments,
        ref int remainingBudget)
    {
        var targetCoverage = new int[24];
        var minCoverage = new int[24];
        var maxCoverage = new int[24];
        var coverage = new int[24];
        for (var hour = 0; hour < 24; hour++)
        {
            if (requiredLookup.TryGetValue((day, hour), out var required))
            {
                targetCoverage[hour] = required;
            }

            if (minCoverageLookup.TryGetValue((day, hour), out var min))
            {
                minCoverage[hour] = min;
            }

            if (maxCoverageLookup.TryGetValue((day, hour), out var max))
            {
                maxCoverage[hour] = max;
            }
        }

        var eligible = promoters
            .Where(p => IsEligibleForDay(p, day, enforceRestDay))
            .Where(p => p.WeeklyHours + MinShiftHours <= MaxWeeklyHours)
            .OrderBy(p => p.WeeklyHours)
            .ThenBy(p => GetDeterministicTieBreaker(baseSeed, p.Id, day))
            .ToList();

        while (eligible.Count > 0 && NeedsCoverage(minCoverage, targetCoverage, coverage))
        {
            if (remainingBudget < MinShiftHours && !HasMinDeficit(minCoverage, coverage))
            {
                break;
            }

            var bestScore = 0;
            var bestIndex = -1;
            var bestStart = 0;
            var bestLength = MinShiftHours;

            for (var i = 0; i < eligible.Count; i++)
            {
                var promoter = eligible[i];
                var minStart = GetMinStartHour(promoter, day);
                if (minStart >= 24)
                {
                    continue;
                }

                var remainingWeekly = MaxWeeklyHours - promoter.WeeklyHours;
                var maxLength = Math.Min(MaxShiftHours, remainingWeekly);
                if (maxLength < MinShiftHours)
                {
                    continue;
                }

                var bestWindow = FindBestWindow(
                    minCoverage,
                    targetCoverage,
                    maxCoverage,
                    coverage,
                    minStart,
                    maxLength,
                    remainingWeekly,
                    nextDayNeedsEarly);

                if (remainingBudget < bestWindow.Length && !HasMinDeficit(minCoverage, coverage, bestWindow.Start, bestWindow.Length))
                {
                    continue;
                }

                if (bestWindow.Score > bestScore)
                {
                    bestScore = bestWindow.Score;
                    bestIndex = i;
                    bestStart = bestWindow.Start;
                    bestLength = bestWindow.Length;
                }
            }

            if (bestIndex < 0 || bestScore <= 0)
            {
                break;
            }

            var selectedPromoter = eligible[bestIndex];
            eligible.RemoveAt(bestIndex);
            AssignWindow(selectedPromoter, day, bestStart, bestLength, coverage, assignments);
            remainingBudget -= bestLength;
        }

        var uncovered = Enumerable.Range(0, 24)
            .Where(hour => coverage[hour] < minCoverage[hour])
            .Select(hour => $"hora {hour}")
            .ToArray();
        if (uncovered.Length > 0)
        {
            warnings.Add($"Dia {day}: cobertura minima insuficiente ({string.Join(", ", uncovered)}).");
        }

        // Reparación final: intenta cubrir déficits mínimos si quedaron huecos
        for (var hour = 0; hour < 24; hour++)
        {
            while (coverage[hour] < minCoverage[hour])
            {
                var added = ForceCoverHour(
                    day,
                    hour,
                    minCoverage,
                    coverage,
                    baseSeed,
                    promoters,
                    assignments);

                if (!added)
                {
                    warnings.Add($"Dia {day} hora {hour}: no se pudo alcanzar delta mínimo (asignados {coverage[hour]}/{minCoverage[hour]}).");
                    break;
                }

                remainingBudget -= MinShiftHours;
            }
        }
    }

    private static (int Start, int Length, int Score) FindBestWindow(
        int[] minCoverage,
        int[] targetCoverage,
        int[] maxCoverage,
        int[] coverage,
        int minStart,
        int maxLength,
        int remainingWeekly,
        bool nextDayNeedsEarly)
    {
        var bestScore = 0;
        var bestStart = 0;
        var bestLength = MinShiftHours;
        var bestEfficiency = double.NegativeInfinity;
        var lengthBias = remainingWeekly >= 8 ? 1 : 0;

        for (var start = minStart; start <= 24 - MinShiftHours; start++)
        {
            for (var length = MinShiftHours; length <= maxLength && start + length <= 24; length++)
            {
                var score = 0;
                for (var h = start; h < start + length; h++)
                {
                    var current = coverage[h];
                    var min = minCoverage[h];
                    var target = targetCoverage[h];
                    var max = maxCoverage[h];

                    if (current < min)
                    {
                        score += MinCoverageWeight;
                    }
                    else if (current < target)
                    {
                        score += TargetCoverageWeight;
                    }
                    else if (current < max)
                    {
                        score += ExtraCoverageWeight;
                    }
                    else
                    {
                        score -= OverCoveragePenalty;
                    }

                    if (nextDayNeedsEarly && h >= 22)
                    {
                        score -= 6;
                    }
                }

                var weightedScore = score + (lengthBias * length);
                var efficiency = weightedScore / length;

                if (efficiency > bestEfficiency ||
                    (Math.Abs(efficiency - bestEfficiency) < 0.0001 && weightedScore > bestScore))
                {
                    bestScore = weightedScore;
                    bestStart = start;
                    bestLength = length;
                    bestEfficiency = efficiency;
                }
            }
        }

        return (bestStart, bestLength, bestScore);
    }

    private static void AssignWindow(
        PromoterState promoter,
        int day,
        int start,
        int length,
        int[] coverage,
        Dictionary<(int Day, int Hour), List<int>> assignments)
    {
        for (var hour = start; hour < start + length; hour++)
        {
            if (!assignments.TryGetValue((day, hour), out var list))
            {
                list = new List<int>();
                assignments[(day, hour)] = list;
            }

            list.Add(promoter.Id);
            coverage[hour]++;
        }

        promoter.WeeklyHours += length;
        promoter.WorkedDays.Add(day);
        promoter.LastShiftEndAbsoluteHour = ToAbsoluteHour(day, start + length);
    }

    private static bool IsEligibleForDay(PromoterState promoter, int day, bool enforceRestDay)
    {
        if (promoter.WorkedDays.Contains(day))
        {
            return false;
        }

        if (promoter.WeeklyHours + MinShiftHours > MaxWeeklyHours)
        {
            return false;
        }

        if (enforceRestDay && promoter.RestDay == day)
        {
            return false;
        }

        if (promoter.LastShiftEndAbsoluteHour is null)
        {
            return true;
        }

        return GetMinStartHour(promoter, day) < 24;
    }

    private static int GetMinStartHour(PromoterState promoter, int day)
    {
        if (promoter.LastShiftEndAbsoluteHour is null)
        {
            return 0;
        }

        var minStartAbsolute = promoter.LastShiftEndAbsoluteHour.Value + MinRestHours;
        var dayStartAbsolute = ToAbsoluteHour(day, 0);
        var minStart = minStartAbsolute - dayStartAbsolute;
        if (minStart < 0)
        {
            return 0;
        }

        return minStart;
    }

    private static bool NeedsCoverage(int[] minCoverage, int[] targetCoverage, int[] coverage)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            if (coverage[hour] < minCoverage[hour] || coverage[hour] < targetCoverage[hour])
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMinDeficit(int[] minCoverage, int[] coverage)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            if (coverage[hour] < minCoverage[hour])
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMinDeficit(int[] minCoverage, int[] coverage, int start, int length)
    {
        for (var hour = start; hour < start + length; hour++)
        {
            if (coverage[hour] < minCoverage[hour])
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEarlyCoverageNeed(
        IReadOnlyDictionary<(int Day, int Hour), int> minCoverageLookup,
        int day)
    {
        for (var hour = 0; hour <= 5; hour++)
        {
            if (minCoverageLookup.TryGetValue((day, hour), out var min) && min > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetDeterministicTieBreaker(int baseSeed, int promoterId, int day)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + baseSeed;
            hash = (hash * 31) + promoterId;
            hash = (hash * 31) + day;
            return hash;
        }
    }

    private static bool ForceCoverHour(
        int day,
        int hour,
        int[] minCoverage,
        int[] coverage,
        int baseSeed,
        List<PromoterState> promoters,
        Dictionary<(int Day, int Hour), List<int>> assignments)
    {
        var candidates = promoters
            .Where(p => IsEligibleForDay(p, day, enforceRestDay: true))
            .Where(p => p.WeeklyHours + MinShiftHours <= MaxWeeklyHours)
            .Where(p => GetMinStartHour(p, day) <= hour && hour + MinShiftHours <= 24)
            .OrderBy(p => p.WeeklyHours)
            .ThenBy(p => GetDeterministicTieBreaker(baseSeed, p.Id, day))
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        var promoter = candidates[0];
        AssignWindow(promoter, day, hour, MinShiftHours, coverage, assignments);
        return true;
    }


    private static int ToAbsoluteHour(int day, int hour)
        => ((day - 1) * 24) + hour;

    private sealed class PromoterState
    {
        public PromoterState(int id, string name, int restDay)
        {
            Id = id;
            Name = name;
            RestDay = restDay;
            WorkedDays = new HashSet<int>();
        }

        public int Id { get; }
        public string Name { get; }
        public int RestDay { get; }
        public int WeeklyHours { get; set; }
        public int? LastShiftEndAbsoluteHour { get; set; }
        public HashSet<int> WorkedDays { get; }
    }

}
