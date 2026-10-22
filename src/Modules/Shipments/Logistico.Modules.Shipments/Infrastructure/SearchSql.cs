using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Logistico.Modules.Shipments.Infrastructure;

/// <summary>
/// <see cref="SearchText.Fold"/> inside SQL queries, for the smaller tables that have no folded search column of their own
/// (requests, trucks). EF translates it to <c>translate(lower(…))</c>; it never runs in .NET. Callers prefix the text with
/// a space, as <see cref="SearchText.Pattern"/> expects.
/// </summary>
internal static class SearchSql
{
    public static string Fold(string? text) => throw new InvalidOperationException("SearchSql.Fold only runs inside a database query.");

    public static void Register(ModelBuilder modelBuilder) =>
        modelBuilder.HasDbFunction(typeof(SearchSql).GetMethod(nameof(Fold))!)
            .HasTranslation(args =>
            {
                var text = args[0];
                var mapping = text.TypeMapping;
                var lower = new SqlFunctionExpression("lower", [text], nullable: true, argumentsPropagateNullability: [true], typeof(string), mapping);
                return new SqlFunctionExpression(
                    "translate",
                    [lower, new SqlConstantExpression(SearchText.FoldFrom, typeof(string), mapping), new SqlConstantExpression(SearchText.FoldTo, typeof(string), mapping)],
                    nullable: true,
                    argumentsPropagateNullability: [true, false, false],
                    typeof(string),
                    mapping);
            });
}
