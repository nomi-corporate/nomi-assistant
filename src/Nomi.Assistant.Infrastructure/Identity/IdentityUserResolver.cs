using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Infrastructure.Identity;

public sealed class IdentityUserResolver(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment)
    : IIdentityUserResolver
{
    public async Task<IdentityResolution> ResolveAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
            return new(IdentityResolutionStatus.Unauthorized);
        var baseUrl = configuration["Identity:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var identityBase)
            || !string.IsNullOrEmpty(identityBase.UserInfo) || !string.IsNullOrEmpty(identityBase.Query)
            || !string.IsNullOrEmpty(identityBase.Fragment))
            return new(IdentityResolutionStatus.Unavailable);
        if (identityBase.Scheme != Uri.UriSchemeHttps
            && !(environment.IsDevelopment() && identityBase.IsLoopback && identityBase.Scheme == Uri.UriSchemeHttp))
            return new(IdentityResolutionStatus.Unavailable);
        var root = new Uri(identityBase, identityBase.AbsolutePath.EndsWith('/') ? identityBase.AbsolutePath : identityBase.AbsolutePath + "/");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "v1/auth/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Parameter);
        try
        {
            using var response = await clients.CreateClient("identity").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new(IdentityResolutionStatus.Unauthorized);
            if (!response.IsSuccessStatusCode) return new(IdentityResolutionStatus.Unavailable);
            var user = await response.Content.ReadFromJsonAsync<IdentityUser>(ct);
            return user is { Id: var id } && id != Guid.Empty
                ? new(IdentityResolutionStatus.Authenticated, id)
                : new(IdentityResolutionStatus.Unavailable);
        }
        catch (HttpRequestException) { return new(IdentityResolutionStatus.Unavailable); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(IdentityResolutionStatus.Unavailable); }
        catch (JsonException) { return new(IdentityResolutionStatus.Unavailable); }
    }
    private sealed record IdentityUser(Guid Id);
}
