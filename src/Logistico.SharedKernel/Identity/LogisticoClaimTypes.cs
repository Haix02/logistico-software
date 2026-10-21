namespace Logistico.SharedKernel.Identity;

public static class LogisticoClaimTypes
{
    public const string TenantId = "logistico:tenant_id";
    public const string DisplayName = "logistico:display_name";

    /// <summary>On a courier's account: the code of the courier (Fleet page) whose parcels the account delivers.</summary>
    public const string CourierCode = "logistico:courier_code";

    /// <summary>On a seller's account: the id of the seller (Sellers page) whose orders the account books and follows.</summary>
    public const string MerchantId = "logistico:merchant_id";
}
