using Service.Api;
using Service.Application;
using Service.Domain;
using Service.Infrastructure;
using Service.Infrastructure.Sqlite;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT")
          ?? Environment.GetEnvironmentVariable("SCHEDULER_PORT")
          ?? "5031";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddDomain();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("local-ui", policy =>
        policy.SetIsOriginAllowed(origin =>
            string.IsNullOrWhiteSpace(origin) ||
            origin.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("http://localhost:4200", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("http://127.0.0.1:4200", StringComparison.OrdinalIgnoreCase) ||
            IsAllowedOrigin(origin))
        .AllowAnyHeader()
        .AllowAnyMethod());
});

static bool IsAllowedOrigin(string? origin)
{
    if (string.IsNullOrWhiteSpace(origin))
    {
        return false;
    }

    var allowed = Environment.GetEnvironmentVariable("SCHEDULER_ALLOWED_ORIGINS");
    if (string.IsNullOrWhiteSpace(allowed))
    {
        return false;
    }

    var list = allowed
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return list.Any(value => origin.StartsWith(value, StringComparison.OrdinalIgnoreCase));
}
builder.Services.AddWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(Service.Application.Csv.ParseCsvHandler).Assembly);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("local-ui");

app.MapControllers();

await SqliteBootstrapper.EnsureCreatedAsync();

app.Run();
