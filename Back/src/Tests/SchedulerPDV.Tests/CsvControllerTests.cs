using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Service.Api.Controllers;
using Service.Application.Common;
using Service.Application.Csv;
using Service.Domain.Models;
using Wolverine;

namespace Service.Tests;

/// <summary>
/// Tests for <see cref="CsvController"/>.
/// </summary>
[TestClass]
public sealed class CsvControllerTests
{
    /// <summary>
    /// Ensures the controller returns schedule preview rows.
    /// </summary>
    [TestMethod]
    public async Task Parse_ReturnsSchedulePreview()
    {
        var messageBus = Substitute.For<IMessageBus>();
        var parsedRows = new List<TrxHourData>
        {
            new(1, 0, 23.4, null),
            new(2, 13, 58.1, null),
        };
        var parseResult = new CsvParseResult(parsedRows, Array.Empty<string>());
        var result = Result<CsvParseResult>.Ok(parseResult);

        messageBus
            .InvokeAsync<Result<CsvParseResult>>(Arg.Any<ParseCsvQuery>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var file = CreateCsvFile("1;0;23.4\n2;13;58.1");
        var scheduleGenerator = Substitute.For<Service.Application.Scheduling.IScheduleGenerator>();
        scheduleGenerator
            .GenerateAsync(Arg.Any<IReadOnlyList<TrxHourData>>(), Arg.Any<Service.Application.Scheduling.ScheduleConfig>(), Arg.Any<CancellationToken>())
            .Returns(new Service.Application.Scheduling.ScheduleResult(
                new[]
                {
                    new Service.Application.Scheduling.ScheduleRow(1, 0, 23.4, null, 1, new[] { 1 }),
                    new Service.Application.Scheduling.ScheduleRow(2, 13, 58.1, null, 2, new[] { 1, 2 }),
                },
                Array.Empty<string>()));

        var controller = new CsvController(messageBus, scheduleGenerator);

        var request = new CsvParseRequest
        {
            File = file,
            SelectedDays = "1,2,3,4",
            PdvName = "Test",
            PdvCode = "PDV1",
            TrxAverage = "28",
            PromoterCount = 2,
        };
        var response = await controller.Parse(request, CancellationToken.None);
        var ok = response as OkObjectResult;

        ok.Should().NotBeNull();
        var payload = ok!.Value as SchedulePreviewResponse;

        payload.Should().NotBeNull();
        payload!.Success.Should().BeTrue();
        payload.Rows.Should().HaveCount(2);
        payload.Rows[0].DayNumber.Should().Be(1);
        payload.Rows[0].Hour.Should().Be(0);
        payload.Rows[0].TrxValue.Should().Be(23.4);
        payload.Rows[0].AssignedPromoters.Should().Contain("Promotor 1");
        payload.OptimizationPercent.Should().BeGreaterThan(0);
    }

    private static IFormFile CreateCsvFile(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "input.csv");
    }
}
