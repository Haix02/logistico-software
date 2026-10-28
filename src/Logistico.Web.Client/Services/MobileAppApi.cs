using Logistico.SharedKernel.Mobile;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the mobile app's settings (administrators) and its start-up config.</summary>
internal sealed class MobileAppApi(HttpClient http) : ApiClient(http)
{
    public Task<MobileAppSettingsView> GetSettingsAsync(CancellationToken ct = default) => GetAsync<MobileAppSettingsView>(MobileApiRoutes.Settings, ct);

    public Task<MobileAppSettingsView> SaveSettingsAsync(MobileAppSettings settings, uint? version, CancellationToken ct = default) =>
        SendAsync<MobileAppSettingsView>(HttpMethod.Put, MobileApiRoutes.Settings, settings, version, ct);

    public Task<MobileAppConfig> GetConfigAsync(CancellationToken ct = default) => GetAsync<MobileAppConfig>(MobileApiRoutes.Config, ct);
}
