using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace Logistico.Modules.Shipments.Endpoints;

/// <summary>Shared HTTP conventions of the module: validation, ETags, RFC 9457 problems, CSV.</summary>
internal static class Api
{
    /// <summary>Validates every DataAnnotations/IValidatableObject argument before the handler runs (400 with field errors).</summary>
    public static TBuilder WithValidation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            foreach (var argument in context.Arguments)
            {
                if (argument is not null && IsRequestModel(argument) && Validate(argument, context.HttpContext.RequestServices) is { } invalid)
                {
                    return invalid;
                }
            }

            return await next(context);
        });

    public static ValidationProblem? Validate(object request, IServiceProvider? services = null)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request, services, items: null);
        var valid = Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        // JSON also accepts enum numbers ("payoutMethod": 99); only the named values are real choices.
        foreach (var property in request.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type.IsEnum && property.GetIndexParameters().Length == 0 && property.GetValue(request) is { } value && !Enum.IsDefined(type, value))
            {
                results.Add(new ValidationResult(ValidationText.Choice, [property.Name]));
                valid = false;
            }
        }

        if (valid)
        {
            return null;
        }

        // The validator skips IValidatableObject when attributes fail; run it too so every problem shows at once.
        if (request is IValidatableObject validatable && results.Count > 0)
        {
            results.AddRange(validatable.Validate(context));
        }

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").Distinct().ToArray());
        return TypedResults.ValidationProblem(errors);
    }

    public static string? UserName(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool TryReadIfMatch(HttpRequest request, out uint version)
    {
        version = 0;
        var header = request.GetTypedHeaders().IfMatch;
        return header.Count == 1 && uint.TryParse(header[0].Tag.Value?.Trim('"'), out version);
    }

    public static string ETag(uint version) => new EntityTagHeaderValue($"\"{version}\"").ToString();

    public static ProblemHttpResult IfMatchRequired() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: ShipmentTexts.Problem("if_match_required"),
            detail: "Send the ETag you received from GET so concurrent edits are not lost.");

    public static ProblemHttpResult Stale(string code = "shipment.stale") =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: ShipmentTexts.Problem(code),
            detail: ShipmentTexts.Problem("shipment.stale_detail"));

    public static ProblemHttpResult NotFound(string code = "shipment.not_found") =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: ShipmentTexts.Problem(code),
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Conflict(string code, params object?[] args) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: ShipmentTexts.Problem(code, args),
            type: $"https://logistico.app/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Rule(DomainException ex) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: ShipmentTexts.Problem(ex.Code, ex.Args),
            type: $"https://logistico.app/problems/{ex.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = ex.Code });

    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Spreadsheet applications can interpret user-controlled cells as formulas.
        // Prefix dangerous values with an apostrophe before applying normal CSV escaping.
        var trimmed = value.AsSpan().TrimStart();
        var safe = !trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? $"'{value}"
            : value;

        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }

    /// <summary>Start of today in Baghdad, as UTC (dashboards count "today" in local time).</summary>
    public static DateTimeOffset StartOfBaghdadDay(DateTimeOffset now)
    {
        var local = now.ToOffset(TimeSpan.FromHours(3));
        return new DateTimeOffset(local.Date, TimeSpan.FromHours(3)).ToUniversalTime();
    }

    private static bool IsRequestModel(object argument)
    {
        // Contracts and their sub-namespaces (Rules holds the automation policy and the rate card).
        var type = argument.GetType();
        var contracts = typeof(ShipmentTexts).Namespace!;
        return type.IsClass && type != typeof(string) && type.Namespace is { } ns
            && (ns == contracts || ns.StartsWith(contracts + ".", StringComparison.Ordinal));
    }
}
