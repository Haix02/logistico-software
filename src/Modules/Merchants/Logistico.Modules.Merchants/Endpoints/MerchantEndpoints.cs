using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Merchants.Contracts;
using Logistico.Modules.Merchants.Domain;
using Logistico.Modules.Merchants.Infrastructure;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.SharedKernel.Iraq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Logistico.Modules.Merchants.Endpoints;

internal static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var merchants = app.MapGroup("/" + MerchantsApiRoutes.Merchants)
            .WithTags("Merchants")
            .RequireAuthorization(StaffPolicies.Staff);

        merchants.MapGet("/", ListAsync).WithSummary("Sellers, searchable by name, Instagram handle or phone.");
        merchants.MapGet("/lookup", LookupAsync).WithSummary("Active sellers for pick lists.");
        merchants.MapGet("/{id:guid}", GetAsync).WithSummary("Seller details with ETag.");
        merchants.MapPost("/", CreateAsync).WithSummary("Registers a seller.")
            .RequireAuthorization(StaffPolicies.Operate);
        merchants.MapPut("/{id:guid}", UpdateAsync).WithSummary("Updates a seller. Requires If-Match.")
            .RequireAuthorization(StaffPolicies.Operate);
        return app;
    }

    private static async Task<Ok<PagedResult<MerchantListItem>>> ListAsync(
        MerchantsDbContext db, string? q, int? page, int? pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var number = Paging.Page(page);
        var query = db.Merchants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var phone = IraqPhone.Normalize(term);
            var handle = Merchant.NormalizeHandle(term);
            query = query.Where(m =>
                EF.Functions.ILike(m.Name, $"%{term}%")
                || (m.InstagramHandle != null && EF.Functions.ILike(m.InstagramHandle, $"%{handle}%"))
                || (phone != null && m.Phone == phone)
                || EF.Functions.ILike(m.ContactName, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(m => m.Name)
            .Skip((number - 1) * size)
            .Take(size)
            .Select(m => new MerchantListItem(m.Id, m.Name, m.InstagramHandle, m.ContactName, m.Phone, m.Governorate, m.IsActive, m.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<MerchantListItem>(items, total, number, size));
    }

    private static async Task<Ok<IReadOnlyList<MerchantLookupItem>>> LookupAsync(IMerchantDirectory directory, CancellationToken ct) =>
        TypedResults.Ok(await directory.ListActiveAsync(ct));

    private static async Task<IResult> GetAsync(Guid id, HttpResponse response, MerchantsDbContext db, CancellationToken ct)
    {
        var m = await db.Merchants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null)
        {
            return NotFound();
        }

        response.Headers.ETag = $"\"{m.Version}\"";
        return TypedResults.Ok(ToDetails(m));
    }

    private static async Task<IResult> CreateAsync(MerchantRequest request, MerchantsDbContext db, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await HandleTakenAsync(db, request.InstagramHandle, null, ct))
        {
            return Conflict("merchant.duplicate_handle");
        }

        var merchant = Merchant.Create(tenant.TenantId, request, clock.GetUtcNow());
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created("/" + MerchantsApiRoutes.ById(merchant.Id), new CreateMerchantResponse(merchant.Id));
    }

    private static async Task<IResult> UpdateAsync(Guid id, MerchantRequest request, HttpRequest http, HttpResponse response, MerchantsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var ifMatch = http.GetTypedHeaders().IfMatch;
        if (ifMatch.Count != 1 || !uint.TryParse(ifMatch[0].Tag.Value?.Trim('"'), out var expected))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match header required");
        }

        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (merchant is null)
        {
            return NotFound();
        }

        if (merchant.Version != expected)
        {
            return Stale();
        }

        if (await HandleTakenAsync(db, request.InstagramHandle, id, ct))
        {
            return Conflict("merchant.duplicate_handle");
        }

        merchant.Update(request, clock.GetUtcNow());
        db.Entry(merchant).Property(m => m.Version).OriginalValue = expected;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }

        response.Headers.ETag = new EntityTagHeaderValue($"\"{merchant.Version}\"").ToString();
        return TypedResults.Ok(ToDetails(merchant));
    }

    private static Task<bool> HandleTakenAsync(MerchantsDbContext db, string? handle, Guid? exceptId, CancellationToken ct)
    {
        var normalized = Merchant.NormalizeHandle(handle);
        return normalized is null
            ? Task.FromResult(false)
            : db.Merchants.AnyAsync(m => m.InstagramHandle == normalized && m.Id != exceptId, ct);
    }

    private static MerchantDetails ToDetails(Merchant m) =>
        new(m.Id, m.Name, m.InstagramHandle, m.ContactName, m.Phone, m.Governorate, m.Address, m.Notes, m.IsActive, m.CreatedAt, m.Version);

    private static ValidationProblem? Validate(object request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            return null;
        }

        if (request is IValidatableObject validatable)
        {
            results.AddRange(validatable.Validate(context));
        }

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").Distinct().ToArray());
        return TypedResults.ValidationProblem(errors);
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: MerchantTexts.Problem("merchant.not_found"));

    private static ProblemHttpResult Stale() =>
        TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: MerchantTexts.Problem("merchant.stale"));

    private static ProblemHttpResult Conflict(string code) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: MerchantTexts.Problem(code),
            type: $"https://logistico.app/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
