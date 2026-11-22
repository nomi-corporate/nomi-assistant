using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Assistant.Domain;

namespace Nomi.Assistant.Infrastructure.Persistence;

public sealed class AudioTranscriptionConfiguration : IEntityTypeConfiguration<AudioTranscription>
{
    public void Configure(EntityTypeBuilder<AudioTranscription> builder)
    {
        builder.ToTable("audio_transcriptions");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.JobName);
        builder.Ignore(x => x.OutputKey);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(4000);
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.HasIndex(x => new { x.OwnerUserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.AudioId).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne<AudioUpload>().WithMany().HasForeignKey(x => x.AudioId).OnDelete(DeleteBehavior.Restrict);
    }
}
