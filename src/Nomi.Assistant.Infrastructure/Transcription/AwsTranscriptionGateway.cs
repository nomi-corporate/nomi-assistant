using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.TranscribeService;
using Amazon.TranscribeService.Model;
using Amazon;
using Microsoft.Extensions.Configuration;
using Nomi.Assistant.Application;
using Nomi.Assistant.Domain;

namespace Nomi.Assistant.Infrastructure.Transcription;

public sealed class AwsTranscriptionGateway : IAudioTranscriptionGateway, IDisposable
{
    private readonly string? _bucket;
    private readonly Lazy<IAmazonS3> _s3;
    private readonly Lazy<IAmazonTranscribeService> _transcribe;
    private IAmazonS3 s3 => _s3.Value;
    private IAmazonTranscribeService transcribe => _transcribe.Value;
    public AwsTranscriptionGateway(IConfiguration configuration)
    {
        _bucket = configuration["Transcription:BucketName"];
        var region = RegionEndpoint.GetBySystemName(configuration["AWS:Region"]
            ?? Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-2");
        _s3 = new Lazy<IAmazonS3>(() => new AmazonS3Client(region));
        _transcribe = new Lazy<IAmazonTranscribeService>(() => new AmazonTranscribeServiceClient(region));
    }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_bucket);

    public void Dispose()
    {
        if (_s3.IsValueCreated) _s3.Value.Dispose();
        if (_transcribe.IsValueCreated) _transcribe.Value.Dispose();
    }

    public Task<string> CreateUploadUrlAsync(AudioUpload upload, CancellationToken ct)
    {
        var request = new GetPreSignedUrlRequest {
            BucketName = _bucket, Key = upload.ObjectKey, Verb = HttpVerb.PUT,
            ContentType = "audio/mp4", Expires = upload.UploadExpiresAt.UtcDateTime, Protocol = Protocol.HTTPS
        };
        return s3.GetPreSignedURLAsync(request);
    }

    public async Task<bool> ValidateUploadAsync(AudioUpload upload, CancellationToken ct)
    {
        try
        {
            using var result = await s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = upload.ObjectKey }, ct);
            if (result.ContentLength != upload.SizeBytes || upload.SizeBytes is < 1 or > 10_000_000) return false;
            using var buffer = new MemoryStream((int)upload.SizeBytes);
            await result.ResponseStream.CopyToAsync(buffer, ct);
            if (buffer.Length != upload.SizeBytes) return false;
            var bytes = buffer.ToArray();
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            var duration = ReadM4aDuration(bytes);
            return sha.Equals(upload.Sha256, StringComparison.OrdinalIgnoreCase)
                && duration is > 0 and <= 60 && Math.Abs(duration.Value - (double)upload.DurationSeconds) <= 2;
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound) { return false; }
    }

    public async Task StartAsync(AudioUpload upload, AudioTranscription transcription, CancellationToken ct)
    {
        await transcribe.StartTranscriptionJobAsync(new StartTranscriptionJobRequest {
            TranscriptionJobName = transcription.JobName,
            LanguageCode = "pt-BR",
            MediaFormat = "m4a",
            Media = new Media { MediaFileUri = "s3://" + _bucket + "/" + upload.ObjectKey },
            OutputBucketName = _bucket,
            OutputKey = transcription.OutputKey
        }, ct);
    }

    public async Task<(string Status, string? Text, string? ErrorCode)> GetResultAsync(AudioTranscription transcription, CancellationToken ct)
    {
        var result = await transcribe.GetTranscriptionJobAsync(
            new GetTranscriptionJobRequest { TranscriptionJobName = transcription.JobName }, ct);
        var status = result.TranscriptionJob?.TranscriptionJobStatus?.Value;
        if (status == "FAILED") return ("failed", null, "TRANSCRIPTION_UNAVAILABLE");
        if (status != "COMPLETED") return ("processing", null, null);
        try
        {
            using var output = await s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = transcription.OutputKey }, ct);
            using var document = await JsonDocument.ParseAsync(output.ResponseStream, cancellationToken: ct);
            var text = document.RootElement.GetProperty("results").GetProperty("transcripts")[0].GetProperty("transcript").GetString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? ("failed", null, "NO_SPEECH_DETECTED") : ("succeeded", text[..Math.Min(text.Length, 4000)], null);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound) { return ("processing", null, null); }
        catch (JsonException) { return ("failed", null, "TRANSCRIPTION_UNAVAILABLE"); }
        catch (InvalidOperationException) { return ("failed", null, "TRANSCRIPTION_UNAVAILABLE"); }
        catch (KeyNotFoundException) { return ("failed", null, "TRANSCRIPTION_UNAVAILABLE"); }
        catch (IndexOutOfRangeException) { return ("failed", null, "NO_SPEECH_DETECTED"); }
    }

    public async Task DeleteArtifactsAsync(AudioUpload upload, AudioTranscription transcription, CancellationToken ct)
    {
        await s3.DeleteObjectAsync(_bucket, upload.ObjectKey, ct);
        await s3.DeleteObjectAsync(_bucket, transcription.OutputKey, ct);
        try {
            await transcribe.DeleteTranscriptionJobAsync(
                new DeleteTranscriptionJobRequest { TranscriptionJobName = transcription.JobName }, ct);
        } catch (AmazonTranscribeServiceException) {
            // An active job cannot be deleted; the S3 lifecycle policy must remove later output.
        }
    }

    public Task DeleteUploadAsync(AudioUpload upload, CancellationToken ct) =>
        s3.DeleteObjectAsync(_bucket, upload.ObjectKey, ct);

    private static double? ReadM4aDuration(byte[] data)
    {
        if (data.Length < 16 || Encoding.ASCII.GetString(data, 4, 4) != "ftyp") return null;
        for (var pos = 0; pos + 8 <= data.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            if (size < 8 || size > data.Length - pos) return null;
            if (Encoding.ASCII.GetString(data, pos + 4, 4) == "moov")
            {
                var end = pos + (int)size;
                for (var inner = pos + 8; inner + 8 <= end;)
                {
                    var atomSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(inner, 4));
                    if (atomSize < 8 || atomSize > end - inner) return null;
                    if (Encoding.ASCII.GetString(data, inner + 4, 4) == "mvhd")
                    {
                        if (atomSize < 28) return null;
                        var version = data[inner + 8];
                        if (version is not (0 or 1)) return null;
                        var scaleOffset = inner + (version == 1 ? 28 : 20);
                        var durationOffset = scaleOffset + 4;
                        if (durationOffset + (version == 1 ? 8 : 4) > inner + atomSize) return null;
                        var scale = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(scaleOffset, 4));
                        var duration = version == 1
                            ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(durationOffset, 8))
                            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(durationOffset, 4));
                        return scale == 0 ? null : (double)duration / scale;
                    }
                    inner += (int)atomSize;
                }
            }
            pos += (int)size;
        }
        return null;
    }
}
