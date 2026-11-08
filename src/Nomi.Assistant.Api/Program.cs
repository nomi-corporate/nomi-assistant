using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Nomi.Assistant.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddAssistantPersistence(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.Run();

public partial class Program;
