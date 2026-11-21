using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.Web.Client.Services;

namespace Logistico.Web.Client.Tests;

/// <summary>A booking form sends one Idempotency-Key per order: kept for a retry, new for a changed or a next order.</summary>
public sealed class RetryKeyTests
{
    private static CreateShipmentRequest Order(decimal cod) => new() { ConsigneeName = "زبون", CodAmount = cod };

    [Fact]
    public void A_retry_of_the_same_order_keeps_its_key_and_a_changed_or_next_order_gets_a_new_one()
    {
        var retry = new RetryKey();
        var first = retry.For(Order(25_000));

        Assert.True(IdempotencyKey.IsValid(first));
        Assert.Equal(first, retry.For(Order(25_000)));

        var changed = retry.For(Order(30_000));
        Assert.NotEqual(first, changed);

        retry.Done();
        Assert.NotEqual(changed, retry.For(Order(30_000)));
    }
}
