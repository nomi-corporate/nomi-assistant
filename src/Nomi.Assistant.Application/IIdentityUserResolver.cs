namespace Nomi.Assistant.Application;

public enum IdentityResolutionStatus { Authenticated, Unauthorized, Unavailable }
public sealed record IdentityResolution(IdentityResolutionStatus Status, Guid? UserId = null);
public interface IIdentityUserResolver
{
    Task<IdentityResolution> ResolveAsync(string? authorizationHeader, CancellationToken ct);
}
