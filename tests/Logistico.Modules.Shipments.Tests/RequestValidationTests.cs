using System.ComponentModel.DataAnnotations;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ClockServices(TimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? clock : null;
    }
}
