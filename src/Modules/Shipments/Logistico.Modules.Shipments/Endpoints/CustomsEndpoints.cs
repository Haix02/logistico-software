using System.Security.Claims;
using System.Text;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Endpoints;

internal static class CustomsEndpoints
{
    public static IEndpointRouteBuilder MapCustomsEndpoints(this IEndpointRouteBuilder app)
    {
        var customs = app.MapGroup("/" + ShipmentsApiRoutes.Customs)
            .WithTags("Customs (ASYCUDA)")
            .RequireAuthorization(StaffPolicies.Staff)
            .WithValidation();

        customs.MapGet("/", GetQueueAsync)
            .WithSummary("Customs workbench queue (paged in SQL), compliance status and assessed duty totals.");
        customs.MapGet("/{shipmentId:guid}", GetByShipmentAsync)
            .WithSummary("The ASYCUDA declaration and e-verification status of an international shipment.");
        customs.MapPut("/{shipmentId:guid}", UpsertAsync)
            .WithSummary("Saves or advances a declaration (register, hold, or release after mandatory e-verification).")
            .RequireAuthorization(StaffPolicies.Operate);
        customs.MapGet("/{shipmentId:guid}/sad.xml", ExportSadXmlAsync)
            .WithSummary("Downloads the UNCTAD ASYCUDA World Single Administrative Document (SAD) XML.");
        return app;
    }

    private static async Task<Ok<CustomsSummary>> GetQueueAsync(
        string? q,
        ShipmentStatus? status,
        CustomsLane? lane,
        string? office,
        int? page,
        int? pageSize,
        ShipmentsDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var number = Paging.Page(page);
        var knownOffices = CustomsDutyCalculator.Offices.Select(o => o.Code).ToArray();

        // Everything below runs in PostgreSQL: international shipments left-joined to their declarations.
        var queue =
            from s in db.Shipments.AsNoTracking()
            where s.ServiceType == ServiceType.International && s.Status != ShipmentStatus.Cancelled
            join d in db.CustomsDeclarations.AsNoTracking() on s.Id equals d.ShipmentId into declarations
            from d in declarations.DefaultIfEmpty()
            select new { Shipment = s, Declaration = d };

        var kpis = await queue
            .Select(r => new
            {
                r.Shipment.Status,
                Lodged = r.Declaration != null && r.Declaration.DeclarationNumber != null,
                // Only lodged declarations count as assessed duties; estimates for undeclared cargo are shown per row.
                Duties = r.Declaration != null && r.Declaration.DeclarationNumber != null ? r.Declaration.AssessedDutyIqd + r.Declaration.TaxAndFeesIqd : 0m,
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Awaiting = g.Count(x => x.Status == ShipmentStatus.ArrivedPortOfEntry || (x.Status == ShipmentStatus.CustomsPending && !x.Lodged)),
                InClearance = g.Count(x => x.Status == ShipmentStatus.CustomsPending),
                OnHold = g.Count(x => x.Status == ShipmentStatus.CustomsHold),
                Released = g.Count(x => x.Status == ShipmentStatus.Released || x.Status == ShipmentStatus.AtDestinationWarehouse
                    || x.Status == ShipmentStatus.ReadyForDispatch || x.Status == ShipmentStatus.OutForDelivery || x.Status == ShipmentStatus.Delivered),
                Duties = g.Sum(x => x.Duties),
            })
            .SingleOrDefaultAsync(ct);

        if (status is { } st)
        {
            queue = queue.Where(r => r.Shipment.Status == st);
        }

        if (lane is { } ln)
        {
            queue = ln == CustomsLane.Unassigned
                ? queue.Where(r => r.Declaration == null || r.Declaration.SelectivityLane == ln)
                : queue.Where(r => r.Declaration != null && r.Declaration.SelectivityLane == ln);
        }

        if (!string.IsNullOrWhiteSpace(office))
        {
            // Undeclared cargo is expected at its destination office when that is a customs office, otherwise at Umm Qasr (see CustomsDeclaration.CreateFor).
            var code = office.Trim().ToUpperInvariant();
            queue = code == "IQUQR"
                ? queue.Where(r => r.Declaration != null ? r.Declaration.CustomsOfficeCode == code : r.Shipment.DestinationCode == code || !knownOffices.Contains(r.Shipment.DestinationCode))
                : knownOffices.Contains(code)
                    ? queue.Where(r => r.Declaration != null ? r.Declaration.CustomsOfficeCode == code : r.Shipment.DestinationCode == code)
                    : queue.Where(r => r.Declaration != null && r.Declaration.CustomsOfficeCode == code);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            queue = queue.Where(r =>
                EF.Functions.ILike(r.Shipment.TrackingNumber, pattern)
                || EF.Functions.ILike(r.Shipment.ConsigneeName, pattern)
                || EF.Functions.ILike(r.Shipment.ShipperName, pattern)
                || (r.Declaration != null && r.Declaration.DeclarationNumber != null && EF.Functions.ILike(r.Declaration.DeclarationNumber, pattern))
                || (r.Declaration != null && r.Declaration.HsCode != null && EF.Functions.ILike(r.Declaration.HsCode, pattern)));
        }

        var total = await queue.CountAsync(ct);
        var rows = await queue
            .OrderBy(r => r.Shipment.Status == ShipmentStatus.CustomsHold ? 0
                : r.Shipment.Status == ShipmentStatus.CustomsPending ? 1
                : r.Shipment.Status == ShipmentStatus.ArrivedPortOfEntry ? 2
                : r.Shipment.Status == ShipmentStatus.InTransit || r.Shipment.Status == ShipmentStatus.DepartedOrigin ? 3
                : r.Shipment.Status == ShipmentStatus.Released ? 4
                : 5)
            .ThenByDescending(r => r.Declaration != null ? r.Declaration.UpdatedAt : r.Shipment.UpdatedAt)
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        return TypedResults.Ok(new CustomsSummary(
            kpis?.Awaiting ?? 0,
            kpis?.InClearance ?? 0,
            kpis?.OnHold ?? 0,
            kpis?.Released ?? 0,
            kpis?.Duties ?? 0m,
            rows.Select(r => ToQueueItem(r.Shipment, r.Declaration, now)).ToList(),
            total,
            number,
            size));
    }

    private static async Task<IResult> GetByShipmentAsync(Guid shipmentId, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.ServiceType != ServiceType.International)
        {
            return Api.Rule(new DomainException("customs.domestic_not_supported", "Customs declarations apply only to international shipments."));
        }

        var declaration = await db.CustomsDeclarations.AsNoTracking().FirstOrDefaultAsync(d => d.ShipmentId == shipmentId, ct);
        return TypedResults.Ok(ToQueueItem(shipment, declaration, clock.GetUtcNow()));
    }

    private static async Task<IResult> UpsertAsync(
        Guid shipmentId,
        UpsertCustomsDeclarationRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        ShipmentsDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var shipment = await db.Shipments.Include(s => s.Events).FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        var now = clock.GetUtcNow();
        var declaration = await db.CustomsDeclarations.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId, ct);
        var isNew = declaration is null;
        if (declaration is not null)
        {
            // Every save rewrites the whole declaration: never over a colleague's newer changes (hold reason, checks).
            if (!Api.TryReadIfMatch(httpRequest, out var expected))
            {
                return Api.IfMatchRequired();
            }

            if (declaration.Version != expected)
            {
                return Api.Stale("customs.stale");
            }

            db.Entry(declaration).Property(d => d.Version).OriginalValue = expected;
        }

        try
        {
            if (declaration is null)
            {
                declaration = CustomsDeclaration.CreateFor(shipment, now);
                db.CustomsDeclarations.Add(declaration);
            }

            declaration.Apply(request, shipment, user.UserName(), now);
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Api.Rule(ex);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Api.Stale("customs.stale");
        }
        catch (DbUpdateException) when (isNew)
        {
            // Two clerks created the first declaration at the same moment; the other one was saved.
            return Api.Stale("customs.stale");
        }

        return TypedResults.Ok(ToQueueItem(shipment, declaration, now));
    }

    private static async Task<IResult> ExportSadXmlAsync(Guid shipmentId, HttpResponse response, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
        {
            return Api.NotFound();
        }

        if (shipment.ServiceType != ServiceType.International)
        {
            return Api.Rule(new DomainException("customs.domestic_not_supported", "Customs declarations apply only to international shipments."));
        }

        var declaration = await db.CustomsDeclarations.AsNoTracking().FirstOrDefaultAsync(d => d.ShipmentId == shipmentId, ct)
            ?? CustomsDeclaration.CreateFor(shipment, clock.GetUtcNow());

        var xml = declaration.ToAsycudaSadXml(shipment);
        response.ContentType = "application/xml; charset=utf-8";
        response.Headers.ContentDisposition = $"attachment; filename=\"asycuda-sad-{shipment.TrackingNumber}.xml\"";
        await response.WriteAsync(xml, Encoding.UTF8, ct);
        return TypedResults.Empty;
    }

    private static CustomsQueueItem ToQueueItem(Shipment s, CustomsDeclaration? d, DateTimeOffset now)
    {
        var x = d ?? CustomsDeclaration.CreateFor(s, now);
        return new CustomsQueueItem(
            s.Id, s.TrackingNumber, s.Status, s.Mode, s.OriginCode, s.DestinationCode, s.ShipperName, s.ConsigneeName, s.Pieces, s.GrossWeightKg,
            s.DeclaredValue, s.DeclaredCurrency, x.CustomsOfficeCode, x.Regime, x.DeclarationNumber, x.AssessmentNumber, x.ReceiptNumber,
            x.ReleaseOrderNumber, x.SelectivityLane, x.HsCode, x.GoodsDescription ?? s.ItemsDescription, x.CountryOfOrigin, x.CooNumber,
            x.CooEVerified, x.CommercialInvoiceNumber, x.InvoiceEVerified, x.CocNumber, x.CustomsValueUsd, x.DutyRatePercent, x.AssessedDutyIqd,
            x.TaxAndFeesIqd, x.HoldReason, x.BrokerName, x.RegisteredAt, x.ReleasedAt, d?.UpdatedAt ?? s.UpdatedAt, d?.Version);
    }
}
