using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

public sealed class RequestValidationTests
{
    [Fact]
    public void Reschedule_window_uses_the_Baghdad_business_date()
    {
        // 22:30 UTC on Wednesday is already Thursday in Baghdad.
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 21, 22, 30, 0, TimeSpan.Zero));
        var request = new CustomerRequest
        {
            Type = RequestType.Reschedule,
            PreferredDate = new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(3)),
            PhoneLast4 = "3344",
        };
        var context = new ValidationContext(request, new ClockServices(clock), items: null);

        var problems = request.Validate(context).ToList();

        Assert.Contains(problems, problem => problem.MemberNames.Contains(nameof(request.PreferredDate)));
    }

    [Fact]
    public void Bulk_booking_rejects_null_orders_without_throwing()
    {
        var request = new BulkBookOrdersRequest { MerchantId = Guid.NewGuid(), Orders = null };
        var problems = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), problems, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(problems, problem => problem.MemberNames.Contains(nameof(request.Orders)));
    }

    [Fact]
    public void Bulk_booking_validates_nested_order_limits()
    {
        var request = new BulkBookOrdersRequest
        {
            MerchantId = Guid.NewGuid(),
            Orders =
            [
                new ParsedOrderDraft
                {
                    ConsigneeName = new string('x', 201),
                    ConsigneePhone = "0781 222 3344",
                    DestinationCode = "IQ-BG",
                    DeliveryAddress = new string('a', 301),
                    CodAmount = 1_000_000_001m,
                },
            ],
        };

        var problems = request.Validate(new ValidationContext(request)).ToList();

        Assert.Contains(problems, problem => problem.MemberNames.Contains("Orders[0].ConsigneeName"));
        Assert.Contains(problems, problem => problem.MemberNames.Contains("Orders[0].DeliveryAddress"));
        Assert.Contains(problems, problem => problem.MemberNames.Contains("Orders[0].CodAmount"));
    }

    /// <summary>
    /// Regression: a date picker sends local midnight (+03:00), the evening before in UTC; the window compared that UTC
    /// date with today in Baghdad, so "today" was refused and a 15th day was accepted.
    /// </summary>
    [Theory]
    [InlineData(19, true)]  // today (Monday)
    [InlineData(20, true)]  // tomorrow
    [InlineData(33, true)]  // 2 November: the 14th day
    [InlineData(34, false)] // 3 November: one day too far
    [InlineData(18, false)] // yesterday
    public void A_day_picked_in_baghdad_is_checked_against_the_baghdad_window(int dayOfOctober, bool accepted)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(3)));
        var picked = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddDays(dayOfOctober - 1);
        var request = new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = picked, PhoneLast4 = "3344" };

        var problems = request.Validate(new ValidationContext(request, new ClockServices(clock), items: null)).ToList();

        Assert.Equal(accepted, !problems.Any(p => p.MemberNames.Contains(nameof(request.PreferredDate))));
    }

    [Fact]
    public void A_friday_picked_in_baghdad_is_refused_as_a_friday()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(3)));
        var request = new CustomerRequest { Type = RequestType.Reschedule, PreferredDate = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromHours(3)), PhoneLast4 = "3344" };

        var problem = Assert.Single(request.Validate(new ValidationContext(request, new ClockServices(clock), items: null)));

        Assert.Equal(ValidationText.NoFriday, problem.ErrorMessage);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ClockServices(TimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? clock : null;
    }
}
