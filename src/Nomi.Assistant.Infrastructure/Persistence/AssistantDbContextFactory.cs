using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nomi.Assistant.Infrastructure.Persistence;

public sealed class AssistantDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    public AssistantDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NOMI_ASSISTANT_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "Configure NOMI_ASSISTANT_MIGRATION_CONNECTION com a identidade de migrations. " +
                "Consulte docs/postgresql.md para os comandos de migrations.");
        }

        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseNpgsql(connection, provider =>
                provider.MigrationsHistoryTable("__EFMigrationsHistory", "migrations"))
            .Options;

        return new AssistantDbContext(options);
    }
}
