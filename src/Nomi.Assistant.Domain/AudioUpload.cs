namespace Nomi.Assistant.Domain;

public sealed class AudioUpload
{
    private AudioUpload() { }
    public AudioUpload(Guid ownerUserId, string objectKey, long sizeBytes, decimal durationSeconds, string sha256, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        ObjectKey = objectKey.Replace("{id}", Id.ToString("N"));
        SizeBytes = sizeBytes;
        DurationSeconds = durationSeconds;
        Sha256 = sha256;
        CreatedAt = now;
        UploadExpiresAt = now.AddMinutes(10);
    }
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string ObjectKey { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public decimal DurationSeconds { get; private set; }
    public string Sha256 { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
}
