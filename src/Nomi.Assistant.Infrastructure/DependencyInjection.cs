using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nomi.Assistant.Infrastructure.Persistence;

namespace Nomi.Assistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAssistantPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Assistant");
        var healthChecks = services.AddHealthChecks();
        if (string.IsNullOrWhiteSpace(connection))
        {
            healthChecks.AddCheck("postgresql",
                () => HealthCheckResult.Unhealthy("Assistant database is not configured."),
                tags: ["ready"]);
            return services;
        }

        services.AddDbContext<AssistantDbContext>(options => options.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "migrations")));
        healthChecks.AddCheck<AssistantDatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
