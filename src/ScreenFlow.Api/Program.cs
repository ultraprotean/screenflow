using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using ScreenFlow.Api.Endpoints;
using ScreenFlow.Api.Errors;
using ScreenFlow.Infrastructure;
using ScreenFlow.Infrastructure.Encryption;
using ScreenFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScreenFlowInfrastructure();
builder.Services.AddSingleton(TimeProvider.System); // injectable clock: tests can control time
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true); // unreadable bodies -> ExceptionMapper
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionMapper>();
builder.Services.AddHealthChecks().AddDbContextCheck<ScreenFlowDbContext>();

var app = builder.Build();

// Fail fast: a missing or malformed encryption key stops startup instead of the first order.
app.Services.GetRequiredService<FieldEncryptor>();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ScreenFlowDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler(); // first, so it catches exceptions from everything after it

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() }),
});

var api = app.MapGroup("/api/v1");
api.MapCatalogEndpoints();
api.MapOrderEndpoints();

await app.RunAsync();

/// <summary>Exposed so the integration tests' WebApplicationFactory can host the app in memory.</summary>
public partial class Program;
