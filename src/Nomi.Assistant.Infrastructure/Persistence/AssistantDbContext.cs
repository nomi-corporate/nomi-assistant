using Microsoft.EntityFrameworkCore;

namespace Nomi.Assistant.Infrastructure.Persistence;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("assistant");
    }
}
