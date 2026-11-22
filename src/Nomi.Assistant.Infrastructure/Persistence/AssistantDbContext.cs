using Microsoft.EntityFrameworkCore;

namespace Nomi.Assistant.Infrastructure.Persistence;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<Nomi.Assistant.Domain.AudioUpload> AudioUploads => Set<Nomi.Assistant.Domain.AudioUpload>();
    public DbSet<Nomi.Assistant.Domain.AudioTranscription> AudioTranscriptions => Set<Nomi.Assistant.Domain.AudioTranscription>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("assistant");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);
    }
}
