using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>Typed client for the optimization engine: planning settings, plans, approving and putting plans aside.</summary>
internal sealed class PlanningApi(HttpClient http) : ApiClient(http)
{
    public Task<PlanningPolicy> GetPolicyAsync(CancellationToken ct = default) => GetAsync<PlanningPolicy>(ShipmentsApiRoutes.PlanningPolicy, ct);

    public Task<PlanningPolicy> UpdatePolicyAsync(PlanningPolicy policy, CancellationToken ct = default) =>
        SendAsync<PlanningPolicy>(HttpMethod.Put, ShipmentsApiRoutes.PlanningPolicy, policy, policy.Version, ct);

    public Task<PagedResult<PlanRunSummary>> ListAsync(PlanKind? kind = null, string? hub = null, int page = 1, CancellationToken ct = default) =>
        GetAsync<PagedResult<PlanRunSummary>>(ShipmentsApiRoutes.PlanningRuns + Query(("kind", kind), ("hub", hub), ("page", page)), ct);

    public Task<PlanRunDetails?> GetAsync(Guid id, CancellationToken ct = default) => FindAsync<PlanRunDetails>(ShipmentsApiRoutes.PlanningRun(id), ct);

    public Task<PlanRunDetails> RunAsync(StartPlanRequest request, CancellationToken ct = default) =>
        SendAsync<PlanRunDetails>(HttpMethod.Post, ShipmentsApiRoutes.PlanningRuns, request, null, ct);

    public Task<PlanApplyResult> ApplyAsync(PlanRunSummary run, CancellationToken ct = default) =>
        SendAsync<PlanApplyResult>(HttpMethod.Post, ShipmentsApiRoutes.PlanningRunApply(run.Id), null, run.Version, ct);

    public Task<PlanRunSummary> DiscardAsync(PlanRunSummary run, CancellationToken ct = default) =>
        SendAsync<PlanRunSummary>(HttpMethod.Post, ShipmentsApiRoutes.PlanningRunDiscard(run.Id), null, run.Version, ct);
}
