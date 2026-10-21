using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>Customers as the company knows them from earlier orders (by mobile number).</summary>
internal static class CustomerEndpoints
{
    private const int ListSize = 5;

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ShipmentsApiRoutes.CustomerLookup, LookupAsync)
            .WithTags("Customers")
            .WithSummary("Last address, delivery history, return risk and open orders for a mobile number.")
            .RequireAuthorization(StaffPolicies.Care);
        return app;
    }

    private static async Task<IResult> LookupAsync(string? phone, ShipmentsDbContext db, CancellationToken ct)
    {
        if (IraqPhone.Normalize(phone) is not { } normalized)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["phone"] = [ValidationText.IraqiMobile] });
        }

        var orders = db.Shipments.AsNoTracking().Where(s => s.ConsigneePhone == normalized && s.ServiceType == ServiceType.Domestic);
        var returning = new[] { ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned };
        var closed = new[] { ShipmentStatus.Delivered, ShipmentStatus.Returned, ShipmentStatus.Cancelled };

        var totals = await orders.GroupBy(_ => 1).Select(g => new
        {
            Orders = g.Count(),
            Delivered = g.Count(s => s.Status == ShipmentStatus.Delivered),
            Returned = g.Count(s => returning.Contains(s.Status)),
            Refused = g.Count(s => s.ReturnReason == ReturnReason.CustomerRefused),
            Attempts = g.Sum(s => s.DeliveryAttempts),
            Last = g.Max(s => (DateTimeOffset?)s.CreatedAt),
        }).SingleOrDefaultAsync(ct);

        var recent = await orders.OrderByDescending(s => s.CreatedAt).Take(20)
            .Select(s => new
            {
                Ref = new CustomerOrderRef(s.Id, s.TrackingNumber, s.Status, s.MerchantId, s.MerchantName, s.CodAmount, s.CreatedAt),
                s.ConsigneeName,
                s.DestinationCode,
                s.DestinationCity,
                s.DeliveryAddress,
                s.Landmark,
            })
            .ToListAsync(ct);

        var last = recent.FirstOrDefault();
        var delivered = totals?.Delivered ?? 0;
        var returned = totals?.Returned ?? 0;
        var refused = totals?.Refused ?? 0;
        var failed = Math.Max(0, (totals?.Attempts ?? 0) - delivered);
        return TypedResults.Ok(new CustomerProfile(
            normalized,
            last?.ConsigneeName,
            last?.DestinationCode,
            last?.DestinationCity,
            last?.DeliveryAddress,
            last?.Landmark,
            totals?.Orders ?? 0,
            delivered,
            returned,
            refused,
            failed,
            totals?.Last,
            CustomerRiskRules.Assess(delivered, returned, refused, failed),
            // Orders still heading to the customer (returns travelling back to the seller do not count).
            recent.Where(r => !closed.Contains(r.Ref.Status) && !returning.Contains(r.Ref.Status)).Take(ListSize).Select(r => r.Ref).ToList(),
            recent.Take(ListSize).Select(r => r.Ref).ToList()));
    }
}
