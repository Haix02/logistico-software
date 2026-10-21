using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class DeliveryPromiseTests
{
    private static DateTime BaghdadDate(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(3)).Date;

    [Fact]
    public void Same_governorate_is_next_day()
    {
        var monday = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 20), BaghdadDate(DeliveryPromise.Estimate("IQ-BG", "IQ-BG", monday)));
    }

    [Fact]
    public void Other_governorates_take_three_days_and_skip_friday()
    {
        // Wednesday + 3 working days (Thu, Sat, Sun) = Sunday, because Friday is not a delivery day.
        var wednesday = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(new DateTime(2026, 10, 25), BaghdadDate(DeliveryPromise.Estimate("IQ-BG", "IQ-BA", wednesday)));
    }

    [Fact]
    public void A_promise_never_lands_on_a_friday()
    {
        var thursday = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(DayOfWeek.Saturday, BaghdadDate(DeliveryPromise.Estimate("IQ-BG", "IQ-BG", thursday)).DayOfWeek);
    }
}
