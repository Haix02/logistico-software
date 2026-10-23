using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class RateCardTests
{
    private static readonly Guid BigSeller = Guid.CreateVersion7();

    [Theory]
    [InlineData("IQ-BG", "IQ-BG", 1, 5_000)]   // same governorate
    [InlineData("IQ-BG", "IQ-BA", 1, 7_000)]   // south
    [InlineData("IQ-BG", "IQ-AR", 1, 8_000)]   // north
    [InlineData("IQ-BG", "IQ-NA", 1, 6_000)]   // mid-Euphrates
    [InlineData("IQ-BG", "IQ-BA", 5, 7_000)]   // weight allowance
    [InlineData("IQ-BG", "IQ-BA", 5.2, 8_000)] // a started kilogram counts
    [InlineData("IQ-BG", "IQ-BA", 8, 10_000)]
    public void Standard_prices_follow_destination_and_weight(string from, string to, decimal kg, decimal fee) =>
        Assert.Equal(fee, new RateCard().Quote(from, to, kg).Fee);

    [Fact]
    public void Seller_discounts_apply_and_fees_round_to_250_dinars()
    {
        var card = new RateCard { SellerDiscounts = [new SellerDiscount(BigSeller, "Big", 15)] };

        var quote = card.Quote("IQ-BG", "IQ-BA", 1, BigSeller);

        Assert.Equal(6_000m, quote.Fee); // 7,000 − 15 % = 5,950 → 6,000
        Assert.Equal(15m, quote.DiscountPercent);
        Assert.Equal(7_000m, card.Quote("IQ-BG", "IQ-BA", 1, Guid.CreateVersion7()).Fee);
    }

    [Fact]
    public void Governorates_without_a_price_use_the_default()
    {
        var card = new RateCard { DefaultFee = 9_000m, GovernorateFees = [] };

        Assert.Equal(9_000m, card.Quote("IQ-BG", "IQ-MU", 1).Fee);
    }

    [Theory]
    [InlineData(0, 5_000, ReturnReason.CustomerRefused, 0)]
    [InlineData(50, 5_000, ReturnReason.CustomerRefused, 2_500)]
    [InlineData(50, 7_000, ReturnReason.MaxAttemptsReached, 3_500)]
    [InlineData(30, 7_000, ReturnReason.SellerCancelled, 2_000)]
    [InlineData(100, 6_000, ReturnReason.WrongAddress, 6_000)]
    [InlineData(100, 6_000, ReturnReason.CustomerReturn, 6_000)]
    [InlineData(100, 6_000, ReturnReason.Damaged, 0)]
    [InlineData(100, 6_000, ReturnReason.Other, 0)]
    public void Return_fees_are_a_share_of_the_delivery_fee_for_returns_on_the_sellers_side(int percent, int deliveryFee, ReturnReason reason, int expected) =>
        Assert.Equal(expected, new RateCard { ReturnFeePercent = percent }.ReturnFee(deliveryFee, reason));
}
