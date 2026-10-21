namespace Logistico.SharedKernel.Identity;

/// <summary>What the browser client knows about the signed-in user (GET /api/v1/account/me).</summary>
public sealed record UserInfo(
    bool IsAuthenticated,
    string? UserName = null,
    string? DisplayName = null,
    Guid? TenantId = null,
    IReadOnlyList<string>? Roles = null)
{
    public static readonly UserInfo Anonymous = new(false);
}
