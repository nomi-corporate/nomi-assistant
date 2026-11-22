using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nomi.Assistant.Domain;

namespace Nomi.Assistant.Infrastructure.Persistence;

public sealed class AudioUploadConfiguration : IEntityTypeConfiguration<AudioUpload>
{
    public void Configure(EntityTypeBuilder<AudioUpload> builder)
    {
        builder.ToTable("audio_uploads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DurationSeconds).HasPrecision(6, 2);
        builder.HasIndex(x => new { x.OwnerUserId, x.Id });
        builder.HasIndex(x => x.UploadExpiresAt);
    }
}
