namespace Logistico.SharedKernel;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public static class Paging
{
    /// <summary>Highest page number accepted, so (page - 1) × page size can never overflow into a negative offset.</summary>
    public const int MaxPage = 100_000;

    /// <summary>The requested page number, 1 when missing and kept within 1…<see cref="MaxPage"/>.</summary>
    public static int Page(int? page) => Math.Clamp(page ?? 1, 1, MaxPage);
}
