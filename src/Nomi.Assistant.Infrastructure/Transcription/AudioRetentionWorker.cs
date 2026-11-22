using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nomi.Assistant.Application;
using Nomi.Assistant.Infrastructure.Persistence;

namespace Nomi.Assistant.Infrastructure.Transcription;

public sealed class AudioRetentionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AudioRetentionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Audio retention sweep will retry later."); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
        var gateway = scope.ServiceProvider.GetRequiredService<IAudioTranscriptionGateway>();
        if (!gateway.IsConfigured) return;
        var now = clock.GetUtcNow();
        var expired = await db.AudioTranscriptions.Where(x => x.ExpiresAt <= now).Take(100).ToListAsync(ct);
        foreach (var item in expired)
        {
            var upload = await db.AudioUploads.SingleAsync(x => x.Id == item.AudioId, ct);
            try { await gateway.DeleteArtifactsAsync(upload, item, ct); }
            catch (AmazonClientException) { continue; }
            db.AudioTranscriptions.Remove(item);
            db.AudioUploads.Remove(upload);
            await db.SaveChangesAsync(ct);
        }
        var orphanCutoff = now.AddHours(-1);
        var orphaned = await db.AudioUploads
            .Where(x => x.UploadExpiresAt <= orphanCutoff && !db.AudioTranscriptions.Any(t => t.AudioId == x.Id))
            .Take(100).ToListAsync(ct);
        foreach (var upload in orphaned)
        {
            try { await gateway.DeleteUploadAsync(upload, ct); }
            catch (AmazonClientException) { continue; }
            db.AudioUploads.Remove(upload);
            await db.SaveChangesAsync(ct);
        }
    }
}
