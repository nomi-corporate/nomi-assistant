using Nomi.Assistant.Domain;

namespace Nomi.Assistant.Application;

public interface IAudioTranscriptionGateway
{
    bool IsConfigured { get; }
    Task<string> CreateUploadUrlAsync(AudioUpload upload, CancellationToken ct);
    Task<bool> ValidateUploadAsync(AudioUpload upload, CancellationToken ct);
    Task StartAsync(AudioUpload upload, AudioTranscription transcription, CancellationToken ct);
    Task<(string Status, string? Text, string? ErrorCode)> GetResultAsync(AudioTranscription transcription, CancellationToken ct);
    Task DeleteArtifactsAsync(AudioUpload upload, AudioTranscription transcription, CancellationToken ct);
    Task DeleteUploadAsync(AudioUpload upload, CancellationToken ct);
}
