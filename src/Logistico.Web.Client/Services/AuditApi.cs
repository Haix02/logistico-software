using System.Net.Http.Json;
using Logistico.Modules.Audit.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the audit trail (administrators only).</summary>
internal sealed class AuditApi(HttpClient http)
{
    public async Task<PagedResult<AuditEntryItem>> ListAsync(string? q, string? entity, string? entityId, int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"{AuditApiRoutes.Audit}?page={page}&pageSize={pageSize}"
            + (string.IsNullOrWhiteSpace(q) ? "" : $"&q={Uri.EscapeDataString(q)}")
            + (string.IsNullOrWhiteSpace(entity) ? "" : $"&entity={Uri.EscapeDataString(entity)}")
            + (string.IsNullOrWhiteSpace(entityId) ? "" : $"&entityId={Uri.EscapeDataString(entityId)}");
        using var response = await http.GetAsync(url, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PagedResult<AuditEntryItem>>(ct))!;
    }
}
