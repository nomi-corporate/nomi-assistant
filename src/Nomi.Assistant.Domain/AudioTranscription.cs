namespace Nomi.Assistant.Domain;

public sealed class AudioTranscription
{
    private AudioTranscription() { }
    public AudioTranscription(Guid ownerUserId, Guid audioId, string idempotencyKey, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        AudioId = audioId;
        IdempotencyKey = idempotencyKey;
        Status = "queued";
        CreatedAt = now;
        ExpiresAt = now.AddHours(24);
    }
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid AudioId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string Status { get; private set; } = "queued";
    public string? Text { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string JobName => "nomi-" + Id.ToString("N");
    public string OutputKey => "transcripts/" + OwnerUserId.ToString("N") + "/" + Id.ToString("N") + ".json";

    public void Update(string status, string? text = null, string? errorCode = null)
    {
        Status = status;
        Text = text;
        ErrorCode = errorCode;
    }
}
