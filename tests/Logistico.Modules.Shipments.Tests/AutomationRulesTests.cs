using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class AutomationRulesTests
{
    private static readonly AutomationPolicy Defaults = new();

    // Wednesday 21 Oct 2026, 15:00 Baghdad
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    // Thursday 22 Oct 2026, 15:00 Baghdad
    private static readonly DateTimeOffset Thursday = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_refused_parcel_goes_back_to_the_seller_straight_away()
    {
        var decision = DeliveryRules.AfterFailure(1, DeliveryFailureReason.CustomerRefused, Defaults, Wednesday);

        Assert.Equal(FailureFollowUp.Return, decision.FollowUp);
        Assert.Equal(ReturnReason.CustomerRefused, decision.ReturnReason);
    }

    [Fact]
    public void The_last_allowed_attempt_starts_a_return()
    {
        var decision = DeliveryRules.AfterFailure(AutomationPolicy.DefaultMaxAttempts, DeliveryFailureReason.NoAnswer, Defaults, Wednesday);

        Assert.Equal(FailureFollowUp.Return, decision.FollowUp);
        Assert.Equal(ReturnReason.MaxAttemptsReached, decision.ReturnReason);
    }

    [Fact]
    public void Earlier_attempts_are_rescheduled_for_the_next_working_day()
    {
        var decision = DeliveryRules.AfterFailure(1, DeliveryFailureReason.NoAnswer, Defaults, Wednesday);

        Assert.Equal(FailureFollowUp.Reschedule, decision.FollowUp);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero), decision.NextAttempt); // Thursday 20:00 Baghdad
    }

    [Fact]
    public void Rescheduling_skips_friday()
    {
        var decision = DeliveryRules.AfterFailure(1, DeliveryFailureReason.Unreachable, Defaults, Thursday);

        Assert.Equal(DayOfWeek.Saturday, decision.NextAttempt!.Value.ToOffset(TimeSpan.FromHours(3)).DayOfWeek);
    }

    [Fact]
    public void Switched_off_rules_do_nothing_automatically()
    {
        var manual = new AutomationPolicy { ReturnOnRefusal = false, AutoReschedule = false };

        Assert.Equal(FailureFollowUp.None, DeliveryRules.AfterFailure(1, DeliveryFailureReason.CustomerRefused, manual, Wednesday).FollowUp);
        Assert.Equal(FailureFollowUp.Return, DeliveryRules.AfterFailure(3, DeliveryFailureReason.CustomerRefused, manual, Wednesday).FollowUp);
    }

    [Theory]
    [InlineData(ShipmentStatus.Booked, RequestType.Cancel, true)]
    [InlineData(ShipmentStatus.AtWarehouse, RequestType.ChangeAddress, true)]
    [InlineData(ShipmentStatus.InTransitToHub, RequestType.Reschedule, true)]
    [InlineData(ShipmentStatus.OutForDelivery, RequestType.Reschedule, false)]
    [InlineData(ShipmentStatus.OutForDelivery, RequestType.Complaint, true)]
    [InlineData(ShipmentStatus.Returned, RequestType.Return, false)]
    [InlineData(ShipmentStatus.Delivered, RequestType.Reschedule, false)]
    public void Requests_match_the_stage_of_the_delivery(ShipmentStatus status, RequestType type, bool allowed)
    {
        Assert.Equal(allowed, RequestRules.IsAllowed(type, status, null, Wednesday));
    }

    [Fact]
    public void Returns_can_be_asked_for_within_a_week_of_delivery()
    {
        Assert.True(RequestRules.IsAllowed(RequestType.Return, ShipmentStatus.Delivered, Wednesday.AddDays(-6), Wednesday));
        Assert.False(RequestRules.IsAllowed(RequestType.Return, ShipmentStatus.Delivered, Wednesday.AddDays(-8), Wednesday));
    }

    [Theory]
    [InlineData(ShipmentStatus.AtWarehouse, false, 2)]
    [InlineData(ShipmentStatus.InTransitToHub, false, 3)]
    [InlineData(ShipmentStatus.AtWarehouse, true, 3)]
    [InlineData(ShipmentStatus.OutForDelivery, true, 4)]
    public void Cross_hub_parcels_show_an_on_the_way_stage_that_never_goes_backwards(ShipmentStatus status, bool atDestinationHub, int expected)
    {
        Assert.Equal(6, TrackingProgress.Stages(ServiceType.Domestic, crossesHubs: true).Count);
        Assert.Equal(expected, TrackingProgress.CurrentIndex(ServiceType.Domestic, status, crossesHubs: true, atDestinationHub));
    }

    [Theory]
    [InlineData(ServiceType.Domestic, ShipmentStatus.AtWarehouse, ShipmentStatus.InTransitToHub, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.InTransitToHub, ShipmentStatus.AtWarehouse, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.InTransitToHub, ShipmentStatus.OutForDelivery, false)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnInTransit, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.ReturnInTransit, ShipmentStatus.ReturnAtHub, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.ReturnAtHub, ShipmentStatus.Returned, true)]
    [InlineData(ServiceType.Domestic, ShipmentStatus.ReturnInitiated, ShipmentStatus.OutForDelivery, false)]
    public void Line_haul_and_return_legs_follow_the_state_machine(ServiceType service, ShipmentStatus from, ShipmentStatus to, bool allowed)
    {
        Assert.Equal(allowed, ShipmentLifecycle.CanTransition(service, from, to));
    }

    [Fact]
    public void Steps_that_belong_to_scans_and_workflows_are_not_offered_by_hand()
    {
        var manual = ShipmentLifecycle.ManualNext(ServiceType.Domestic, ShipmentStatus.AtWarehouse);

        Assert.DoesNotContain(ShipmentStatus.InTransitToHub, manual);
        Assert.Contains(ShipmentStatus.OutForDelivery, manual);
        Assert.Empty(ShipmentLifecycle.ManualNext(ServiceType.Domestic, ShipmentStatus.InTransitToHub));
    }
}
