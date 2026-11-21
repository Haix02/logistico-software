using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>
/// Properties of the whole state machine, checked over every status rather than a few examples: no step leads
/// nowhere, every status can be reached and can still end, and the browser, the server and the tracking page agree.
/// </summary>
public class LifecycleInvariantTests
{
    public static TheoryData<ServiceType> Services => [ServiceType.Domestic, ServiceType.International];

    private static IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> Machine(ServiceType service) => ShipmentLifecycle.For(service);

    private static HashSet<ShipmentStatus> ReachableFrom(ServiceType service, ShipmentStatus start)
    {
        var seen = new HashSet<ShipmentStatus> { start };
        var queue = new Queue<ShipmentStatus>([start]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in ShipmentLifecycle.AllowedNext(service, current).Where(seen.Add))
            {
                queue.Enqueue(next);
            }
        }

        return seen;
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_step_leads_to_a_status_the_same_machine_knows(ServiceType service)
    {
        foreach (var (from, targets) in Machine(service))
        {
            Assert.All(targets, to => Assert.True(Machine(service).ContainsKey(to), $"{service}: {from} → {to}"));
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_status_can_be_reached_from_a_new_order(ServiceType service)
    {
        var reached = ReachableFrom(service, ShipmentStatus.Draft);

        Assert.Empty(Machine(service).Keys.Except(reached));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void No_parcel_is_ever_stuck_every_status_can_still_reach_an_end(ServiceType service)
    {
        foreach (var status in Machine(service).Keys)
        {
            Assert.True(ReachableFrom(service, status).Overlaps(ShipmentLifecycle.ClosedStatuses), $"{service}: {status} cannot finish");
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void No_status_moves_to_itself(ServiceType service)
    {
        Assert.All(Machine(service), pair => Assert.DoesNotContain(pair.Key, pair.Value));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Only_returned_and_cancelled_end_a_journey_for_good(ServiceType service)
    {
        var terminal = Machine(service).Keys.Where(s => ShipmentLifecycle.IsTerminal(service, s)).Order();

        Assert.Equal(new[] { ShipmentStatus.Returned, ShipmentStatus.Cancelled }.Order(), terminal);
        Assert.False(ShipmentLifecycle.IsTerminal(service, ShipmentStatus.Delivered)); // a customer may still send it back
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void A_receive_scan_moves_to_a_status_of_the_same_machine(ServiceType service)
    {
        var shelf = ShipmentLifecycle.WarehouseStatusFor(service);

        Assert.True(ShipmentLifecycle.IsInWarehouse(shelf));
        Assert.True(Machine(service).ContainsKey(shelf));
    }

    [Fact]
    public void List_order_holds_every_status_exactly_once()
    {
        Assert.Equal(Enum.GetValues<ShipmentStatus>().Order(), ShipmentLifecycle.Order.Order());
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Steps_offered_by_hand_are_allowed_steps_and_never_workflow_steps(ServiceType service)
    {
        foreach (var current in Machine(service).Keys)
        {
            var manual = ShipmentLifecycle.ManualNext(service, current);

            Assert.All(manual, s => Assert.True(ShipmentLifecycle.CanTransition(service, current, s), $"{current} → {s}"));
            Assert.Empty(manual.Intersect(ShipmentLifecycle.WorkflowStatuses));
            Assert.All(manual, s => Assert.True(ShipmentLifecycle.CanRecordByHand(service, current, s)));
            if (ShipmentLifecycle.IsOnLineHaul(current))
            {
                Assert.DoesNotContain(manual, ShipmentLifecycle.IsInWarehouse);
            }

            if (ShipmentLifecycle.IsReturning(current))
            {
                Assert.DoesNotContain(ShipmentStatus.ReturnInitiated, manual);
            }
        }
    }

    [Fact]
    public void Every_workflow_step_is_actually_used_by_a_machine()
    {
        var targets = Enum.GetValues<ServiceType>().SelectMany(service => Machine(service).Values.SelectMany(t => t)).ToHashSet();

        Assert.All(ShipmentLifecycle.WorkflowStatuses, s => Assert.Contains(s, targets));
    }

    [Fact]
    public void Customs_steps_exist_only_for_international_freight()
    {
        ShipmentStatus[] customs = [ShipmentStatus.CustomsPending, ShipmentStatus.CustomsHold, ShipmentStatus.Released];

        Assert.All(customs, s => Assert.False(Machine(ServiceType.Domestic).ContainsKey(s)));
        Assert.All(customs, s => Assert.True(Machine(ServiceType.International).ContainsKey(s)));
        Assert.Empty(ShipmentLifecycle.ManualNext(ServiceType.International, ShipmentStatus.ArrivedPortOfEntry)); // the customs workbench takes over
    }

    // ── Tracking page ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_status_has_a_place_on_every_progress_bar()
    {
        foreach (var service in Enum.GetValues<ServiceType>())
        {
            foreach (var crosses in new[] { false, true })
            {
                var stages = TrackingProgress.Stages(service, crosses).Count;
                foreach (var status in Enum.GetValues<ShipmentStatus>())
                {
                    foreach (var atDestination in new[] { false, true })
                    {
                        Assert.InRange(TrackingProgress.CurrentIndex(service, status, crosses, atDestination), 0, stages - 1);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_parcel_crossing_iraq_never_moves_back_on_the_bar_on_its_way_to_the_customer()
    {
        (ShipmentStatus Status, bool AtDestinationHub)[] journey =
        [
            (ShipmentStatus.Booked, false), (ShipmentStatus.PickedUp, false), (ShipmentStatus.AtWarehouse, false),
            (ShipmentStatus.InTransitToHub, false), (ShipmentStatus.AtWarehouse, true), (ShipmentStatus.ReadyForDispatch, true),
            (ShipmentStatus.OutForDelivery, false), (ShipmentStatus.Delivered, false),
        ];

        var bar = journey.Select(step => TrackingProgress.CurrentIndex(ServiceType.Domestic, step.Status, crossesHubs: true, step.AtDestinationHub)).ToList();

        Assert.Equal(bar.Order(), bar);
        Assert.Equal(TrackingProgress.Stages(ServiceType.Domestic, crossesHubs: true).Count - 1, bar[^1]);
    }

    [Fact]
    public void International_freight_never_moves_back_on_the_bar_through_customs()
    {
        ShipmentStatus[] journey =
        [
            ShipmentStatus.Booked, ShipmentStatus.ReceivedAtOrigin, ShipmentStatus.Consolidated, ShipmentStatus.DepartedOrigin,
            ShipmentStatus.InTransit, ShipmentStatus.ArrivedPortOfEntry, ShipmentStatus.CustomsPending, ShipmentStatus.CustomsHold,
            ShipmentStatus.CustomsPending, ShipmentStatus.Released, ShipmentStatus.AtDestinationWarehouse, ShipmentStatus.OutForDelivery,
            ShipmentStatus.Delivered,
        ];
        for (var i = 1; i < journey.Length; i++)
        {
            Assert.True(ShipmentLifecycle.CanTransition(ServiceType.International, journey[i - 1], journey[i]), $"{journey[i - 1]} → {journey[i]}");
        }

        var bar = journey.Select(s => TrackingProgress.CurrentIndex(ServiceType.International, s)).ToList();

        Assert.Equal(bar.Order(), bar);
        Assert.Equal(TrackingProgress.Stages(ServiceType.International).Count - 1, bar[^1]);
    }

    [Fact]
    public void Journeys_that_end_without_delivery_are_shown_as_ended()
    {
        Assert.All(ShipmentLifecycle.ReturnStatuses.Append(ShipmentStatus.Returned).Append(ShipmentStatus.Cancelled), s => Assert.True(TrackingProgress.IsAborted(s)));
        Assert.False(TrackingProgress.IsAborted(ShipmentStatus.Delivered));
        Assert.False(TrackingProgress.IsAborted(ShipmentStatus.DeliveryFailed));
    }

    // ── Customer requests ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_customer_can_always_complain()
    {
        var now = Orders.Monday;

        Assert.All(Enum.GetValues<ShipmentStatus>(), s => Assert.Contains(RequestType.Complaint, RequestRules.Allowed(s, now.AddDays(-30), now)));
    }

    [Fact]
    public void Changes_are_only_offered_while_the_order_can_still_change()
    {
        ShipmentStatus[] settled = [ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentStatus.Returned, ShipmentStatus.Cancelled, .. ShipmentLifecycle.ReturnStatuses];

        foreach (var status in settled)
        {
            var allowed = RequestRules.Allowed(status, Orders.Monday, Orders.Monday.AddHours(1));
            Assert.DoesNotContain(RequestType.Reschedule, allowed);
            Assert.DoesNotContain(RequestType.ChangeAddress, allowed);
            Assert.DoesNotContain(RequestType.ChangePhone, allowed);
        }
    }

    [Fact]
    public void A_return_can_be_asked_for_up_to_seven_days_after_delivery()
    {
        var delivered = Orders.Monday;

        Assert.Contains(RequestType.Return, RequestRules.Allowed(ShipmentStatus.Delivered, delivered, delivered.AddDays(RequestRules.ReturnWindowDays)));
        Assert.DoesNotContain(RequestType.Return, RequestRules.Allowed(ShipmentStatus.Delivered, delivered, delivered.AddDays(RequestRules.ReturnWindowDays).AddMinutes(1)));
        Assert.Equal(new[] { RequestType.Complaint }, RequestRules.Allowed(ShipmentStatus.OutForDelivery, null, delivered));
    }

    [Theory]
    [InlineData(2, DeliveryFailureReason.NoAnswer, FailureFollowUp.Reschedule)]
    [InlineData(3, DeliveryFailureReason.NoAnswer, FailureFollowUp.Return)]
    [InlineData(1, DeliveryFailureReason.CustomerRefused, FailureFollowUp.Return)]
    [InlineData(1, DeliveryFailureReason.CustomerRescheduled, FailureFollowUp.Reschedule)]
    public void The_last_allowed_attempt_or_a_refusal_ends_in_a_return(int attempts, DeliveryFailureReason reason, FailureFollowUp expected)
    {
        var decision = DeliveryRules.AfterFailure(attempts, reason, new AutomationPolicy { MaxDeliveryAttempts = 3 }, Orders.Monday);

        Assert.Equal(expected, decision.FollowUp);
        Assert.Equal(expected == FailureFollowUp.Return, decision.ReturnReason is not null);
        Assert.Equal(expected == FailureFollowUp.Reschedule, decision.NextAttempt is not null);
    }
}
