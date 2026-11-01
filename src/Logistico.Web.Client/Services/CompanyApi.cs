using Logistico.SharedKernel.Setup;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the company profile, the getting-started guide and the installation health (docs/19).</summary>
internal sealed class CompanyApi(HttpClient http) : ApiClient(http)
{
    private CompanyProfileView? profile;

    /// <summary>The company's name changed (the top bar shows it).</summary>
    public event Action? Changed;

    public string? Name => profile?.Profile.Name is { Length: > 0 } name ? name : null;

    public async Task<CompanyProfileView> GetCompanyAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (profile is null || refresh)
        {
            profile = await GetAsync<CompanyProfileView>(SetupApiRoutes.Company, ct);
        }

        return profile;
    }

    public async Task<CompanyProfileView> SaveCompanyAsync(CompanyProfile company, uint version, CancellationToken ct = default)
    {
        profile = await SendAsync<CompanyProfileView>(HttpMethod.Put, SetupApiRoutes.Company, company, version == 0 ? null : version, ct);
        Changed?.Invoke();
        return profile;
    }

    public Task<GettingStartedView> GetGuideAsync(CancellationToken ct = default) => GetAsync<GettingStartedView>(SetupApiRoutes.GettingStarted, ct);

    public Task SkipAsync(string step, bool skip, CancellationToken ct = default) =>
        SendAsync(skip ? HttpMethod.Post : HttpMethod.Delete, SetupApiRoutes.SkipStep(step), null, null, ct);

    public Task<SystemChecksView> GetChecksAsync(CancellationToken ct = default) => GetAsync<SystemChecksView>(SetupApiRoutes.SystemChecks, ct);
}
