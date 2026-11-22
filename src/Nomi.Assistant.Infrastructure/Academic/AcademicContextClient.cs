using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Infrastructure.Academic;

/// <summary>
/// Reads the user's subjects and professors through the Academic public API, forwarding the
/// user's own bearer token. The Assistant never reads Academic tables (TEC-003 §2).
/// </summary>
public sealed class AcademicContextClient(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment)
    : IAcademicContextClient
{
    private const int PageSize = 100;

    public async Task<AcademicContext> GetContextAsync(string authorizationHeader, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
            return AcademicContext.Empty;
        var root = ResolveRoot();
        if (root is null) return AcademicContext.Empty;
        try
        {
            var client = clients.CreateClient("academic");
            var subjects = await GetAsync<SubjectList>(client, new Uri(root, $"api/v1/subjects?archived=false&limit={PageSize}"), authorization, ct);
            var professors = await GetAsync<ProfessorList>(client, new Uri(root, $"api/v1/professors?archived=false&limit={PageSize}"), authorization, ct);
            return new AcademicContext(
                (subjects?.Items ?? []).Where(s => s.Id != Guid.Empty && !string.IsNullOrWhiteSpace(s.Name))
                    .Select(s => new AcademicSubjectContext(s.Id, s.Name!.Trim(), string.IsNullOrWhiteSpace(s.Code) ? null : s.Code.Trim(), s.ProfessorIds ?? []))
                    .ToArray(),
                (professors?.Items ?? []).Where(p => p.Id != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new AcademicProfessorContext(p.Id, p.Name!.Trim()))
                    .ToArray());
        }
        catch (HttpRequestException) { return AcademicContext.Empty; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return AcademicContext.Empty; }
        catch (JsonException) { return AcademicContext.Empty; }
    }

    private Uri? ResolveRoot()
    {
        var baseUrl = configuration["Academic:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var academicBase)
            || !string.IsNullOrEmpty(academicBase.UserInfo) || !string.IsNullOrEmpty(academicBase.Query)
            || !string.IsNullOrEmpty(academicBase.Fragment))
            return null;
        if (academicBase.Scheme != Uri.UriSchemeHttps
            && !(environment.IsDevelopment() && academicBase.IsLoopback && academicBase.Scheme == Uri.UriSchemeHttp))
            return null;
        return new Uri(academicBase, academicBase.AbsolutePath.EndsWith('/') ? academicBase.AbsolutePath : academicBase.AbsolutePath + "/");
    }

    private static async Task<T?> GetAsync<T>(HttpClient client, Uri uri, AuthenticationHeaderValue authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Parameter);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, ct);
    }

    private sealed record SubjectList(SubjectItem[]? Items);
    private sealed record SubjectItem(Guid Id, string? Name, string? Code, Guid[]? ProfessorIds);
    private sealed record ProfessorList(ProfessorItem[]? Items);
    private sealed record ProfessorItem(Guid Id, string? Name);
}
