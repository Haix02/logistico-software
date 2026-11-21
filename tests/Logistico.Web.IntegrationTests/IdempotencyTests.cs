using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Web.IntegrationTests;

/// <summary>
/// A booking retried after a lost answer (a phone on a weak network, a second tap on "Try again") is booked once: the
/// retry with the same Idempotency-Key gets the first answer instead of a second order.
/// </summary>
[Collection("Api")]
public sealed class IdempotencyTests(LogisticoApiFactory factory)
{
    private static async Task<HttpResponseMessage> BookAsync(HttpClient client, CreateShipmentRequest order, string? key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/" + ShipmentsApiRoutes.Shipments) { Content = JsonContent.Create(order) };
        if (key is not null)
        {
            message.Headers.Add(IdempotencyKey.Header, key);
        }

        return await client.SendAsync(message);
    }

    private static async Task<int> CountAsync(HttpClient client, string customer) =>
        (await client.GetFromJsonAsync<ShipmentListResult>("/" + ShipmentsApiRoutes.Shipments + "?q=" + Uri.EscapeDataString(customer)))!.TotalCount;

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    [Fact]
    public async Task A_retried_booking_with_the_same_key_is_booked_once_and_answered_the_same()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر المحاولة", "idempotent.booking");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون المحاولة المكررة");
        var key = IdempotencyKey.New();

        var first = await BookAsync(client, order, key);
        var again = await BookAsync(client, order, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<CreateShipmentResponse>())!.TrackingNumber, (await again.Content.ReadFromJsonAsync<CreateShipmentResponse>())!.TrackingNumber);
        Assert.Equal(first.Headers.Location, again.Headers.Location);
        Assert.False(first.Headers.Contains(IdempotencyKey.ReplayedHeader));
        Assert.Equal("true", Assert.Single(again.Headers.GetValues(IdempotencyKey.ReplayedHeader)));
        Assert.Equal(1, await CountAsync(client, "زبون المحاولة المكررة"));

        // Without a key every request is its own (the old behaviour).
        await BookAsync(client, order, null);
        Assert.Equal(2, await CountAsync(client, "زبون المحاولة المكررة"));
    }

    [Fact]
    public async Task A_key_reused_for_another_request_is_refused_and_a_bad_key_is_rejected()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر المفتاح", "idempotent.reuse");
        var key = IdempotencyKey.New();
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(client, ApiTestHelpers.DomesticOrder(shop, "زبون أول"), key)).StatusCode);

        var other = await BookAsync(client, ApiTestHelpers.DomesticOrder(shop, "زبون آخر"), key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal("idempotency.key_reused", await CodeAsync(other));
        Assert.Equal(0, await CountAsync(client, "زبون آخر"));

        var tooLong = await BookAsync(client, ApiTestHelpers.DomesticOrder(shop, "زبون ثالث"), new string('k', 256));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("idempotency.key_invalid", await CodeAsync(tooLong));
    }

    [Fact]
    public async Task A_refused_attempt_does_not_spend_the_key_so_the_corrected_request_goes_through()
    {
        var client = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        var shop = await client.CreateMerchantAsync("متجر التصحيح", "idempotent.fix");
        var key = IdempotencyKey.New();
        var broken = ApiTestHelpers.DomesticOrder(shop, "زبون بعد التصحيح");
        broken.ConsigneePhone = "123";

        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(client, broken, key)).StatusCode);
        var fixedOrder = await BookAsync(client, ApiTestHelpers.DomesticOrder(shop, "زبون بعد التصحيح"), key);

        Assert.Equal(HttpStatusCode.Created, fixedOrder.StatusCode);
        Assert.False(fixedOrder.Headers.Contains(IdempotencyKey.ReplayedHeader));
    }

    [Fact]
    public async Task Keys_belong_to_one_person_so_a_colleague_s_identical_key_books_its_own_order()
    {
        var company = Guid.CreateVersion7();
        var first = factory.CreateUserClient("first@logistico.test", company);
        var second = factory.CreateUserClient("second@logistico.test", company);
        var shop = await first.CreateMerchantAsync("متجر الزميلين", "idempotent.people");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون الزميلين");
        const string key = "same-key-for-both";

        var a = await BookAsync(first, order, key);
        var b = await BookAsync(second, order, key);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (a.StatusCode, b.StatusCode));
        Assert.False(b.Headers.Contains(IdempotencyKey.ReplayedHeader));
        Assert.Equal(2, await CountAsync(first, "زبون الزميلين"));
    }

    [Fact]
    public async Task A_running_attempt_holds_its_key_until_it_finishes_gives_up_or_expires()
    {
        var store = factory.Services.GetRequiredService<IdempotencyStore>();
        var id = new IdempotencyStore.Id(Guid.CreateVersion7(), "store@logistico.test", IdempotencyKey.New());
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(ClaimOutcome.Claimed, (await store.ClaimAsync(id, "fingerprint", now, default)).Outcome);
        Assert.Equal(ClaimOutcome.InProgress, (await store.ClaimAsync(id, "fingerprint", now.AddSeconds(1), default)).Outcome);
        Assert.Equal(ClaimOutcome.Reused, (await store.ClaimAsync(id, "another", now.AddSeconds(1), default)).Outcome);

        // An attempt that never finished (a crash) is given up after five minutes.
        Assert.Equal(ClaimOutcome.Claimed, (await store.ClaimAsync(id, "fingerprint", now + IdempotencyMiddleware.GiveUpAfter + TimeSpan.FromSeconds(1), default)).Outcome);
        await store.CompleteAsync(id, new StoredAnswer(201, "application/json", "/x", "\"7\"", "{}"u8.ToArray()), now, default);
        var replay = await store.ClaimAsync(id, "fingerprint", now.AddMinutes(10), default);
        Assert.Equal((ClaimOutcome.Replay, 201, "\"7\""), (replay.Outcome, replay.Stored!.StatusCode, replay.Stored.ETag));

        // A key is kept for a day from its last claim; after that the same key starts over, and the hourly clean-up removes it.
        var dayLater = now + IdempotencyMiddleware.GiveUpAfter + IdempotencyMiddleware.KeepFor + TimeSpan.FromMinutes(1);
        Assert.Equal(ClaimOutcome.Claimed, (await store.ClaimAsync(id, "fingerprint", dayLater, default)).Outcome);
        Assert.True(await store.PurgeAsync(dayLater + TimeSpan.FromDays(2), default) >= 1);
        Assert.Equal(ClaimOutcome.Claimed, (await store.ClaimAsync(id, "fingerprint", now, default)).Outcome);
        await store.ReleaseAsync(id, default);
    }
}
