using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nomi.Assistant.Infrastructure.Persistence;
using Nomi.Assistant.Infrastructure.Identity;
using Nomi.Assistant.Infrastructure.Transcription;
using Nomi.Assistant.Infrastructure.Academic;
using Nomi.Assistant.Infrastructure.Interpretation;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAssistantPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Assistant");
        var healthChecks = services.AddHealthChecks();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("identity", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IIdentityUserResolver, IdentityUserResolver>();
        services.AddSingleton<IAudioTranscriptionGateway, AwsTranscriptionGateway>();
        services.AddHttpClient("academic", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IAcademicContextClient, AcademicContextClient>();
        services.AddSingleton<ICommitmentInterpreter, ClaudeCommitmentInterpreter>();
        services.AddSingleton(provider => new ProposalRateLimiter(provider.GetRequiredService<TimeProvider>()));
        if (string.IsNullOrWhiteSpace(connection))
        {
            healthChecks.AddCheck("postgresql",
                () => HealthCheckResult.Unhealthy("Assistant database is not configured."),
                tags: ["ready"]);
            return services;
        }

        services.AddDbContext<AssistantDbContext>(options => options.UseNpgsql(connection,
            provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "migrations")));
        services.AddHostedService<AudioRetentionWorker>();
        healthChecks.AddCheck<AssistantDatabaseHealthCheck>("postgresql", tags: ["ready"]);
        return services;
    }
}
