using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Logistico.Modules.Merchants.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the Merchants (sellers) REST API.</summary>
internal sealed class MerchantsApi(HttpClient http)
{
    private IReadOnlyList<MerchantLookupItem>? _lookup;

    public async Task<PagedResult<MerchantListItem>> ListAsync(string? q, int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"{MerchantsApiRoutes.Merchants}?page={page}&pageSize={pageSize}" + (string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}");
        return (await http.GetFromJsonAsync<PagedResult<MerchantListItem>>(url, ct))!;
    }

    /// <summary>Active sellers for pick lists (cached for the session; call <see cref="Invalidate"/> after changes).</summary>
    public async Task<IReadOnlyList<MerchantLookupItem>> LookupAsync(CancellationToken ct = default) =>
        _lookup ??= (await http.GetFromJsonAsync<List<MerchantLookupItem>>(MerchantsApiRoutes.Lookup, ct))!;

    public void Invalidate() => _lookup = null;

    public async Task<MerchantDetails?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(MerchantsApiRoutes.ById(id), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ApiException.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MerchantDetails>(ct);
    }

    public async Task<Guid> CreateAsync(MerchantRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(MerchantsApiRoutes.Merchants, request, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        Invalidate();
        return (await response.Content.ReadFromJsonAsync<CreateMerchantResponse>(ct))!.Id;
    }

    public async Task<MerchantDetails> UpdateAsync(Guid id, uint version, MerchantRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, MerchantsApiRoutes.ById(id)) { Content = JsonContent.Create(request) };
        message.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        Invalidate();
        return (await response.Content.ReadFromJsonAsync<MerchantDetails>(ct))!;
    }
}
