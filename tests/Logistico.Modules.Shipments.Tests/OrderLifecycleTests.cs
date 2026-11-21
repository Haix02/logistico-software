using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.Modules.Shipments.Infrastructure;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>An order from booking to its last step: every rule <see cref="Shipment"/> enforces along the way.</summary>
public class OrderLifecycleTests
{
    private static readonly DateTimeOffset Baghdad20h = new(2026, 10, 20, 20, 0, 0, TimeSpan.FromHours(3)); // Tuesday
    private static readonly DateTimeOffset Thursday20h = new(2026, 10, 22, 20, 0, 0, TimeSpan.FromHours(3));

    private static string Code(Action act) => Assert.Throws<DomainException>(act).Code;

    // ── Booking ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("IQ-XX", "0770 123 4567", "شارع الكورنيش")]
    [InlineData("IQ-BA", "12", "شارع الكورنيش")]
    [InlineData("IQ-BA", "0770 123 4567", "   ")]
    public void A_domestic_order_needs_a_known_governorate_a_mobile_number_and_an_address(string governorate, string phone, string address)
    {
        var request = Orders.Request(governorate);
        request.ConsigneePhone = phone;
        request.DeliveryAddress = address;

        Assert.Equal("shipment.domestic_address", Code(() => Shipment.Book(Tenants.Default, "LX26000000017", request, Orders.Shop, null, Orders.Monday, IraqHubs.Default)));
    }

    [Fact]
    public void A_booked_order_starts_its_history_and_knows_both_hubs_and_its_promise()
    {
        var parcel = Orders.Book();

        Assert.Equal(ShipmentStatus.Booked, parcel.Status);
        var booked = Assert.Single(parcel.Events);
        Assert.Equal((ShipmentStatus.Booked, "IQ-BG", "agent"), (booked.Status, booked.Location, booked.RecordedBy));
        Assert.Equal((IraqHubs.Baghdad, IraqHubs.Basra), (parcel.OriginHubCode, parcel.DestinationHubCode));
        Assert.Null(parcel.CurrentHubCode);
        Assert.Equal(Orders.Monday, parcel.LastStatusAt);

        // Baghdad → Basra: three working days, by 20:00 Baghdad time.
        Assert.Equal(Thursday20h, parcel.PromisedAt);
        Assert.Equal(parcel.PromisedAt, parcel.EtaAt);
        Assert.Equal(ConfirmationStatus.NotRequired, parcel.Confirmation);
        Assert.False(parcel.AwaitsConfirmation);
    }

    [Fact]
    public void Zero_amounts_mean_no_cash_on_delivery_and_no_fee()
    {
        var parcel = Orders.Book(cod: 0, fee: 0);

        Assert.Null(parcel.CodAmount);
        Assert.Null(parcel.CodCurrency);
        Assert.Null(parcel.DeliveryFee);
    }

    [Fact]
    public void A_promise_given_at_booking_wins_over_the_estimate()
    {
        var request = Orders.Request();
        request.PromisedAt = new DateTimeOffset(2026, 10, 25, 18, 0, 0, TimeSpan.FromHours(3));

        var parcel = Shipment.Book(Tenants.Default, "LX26000000017", request, Orders.Shop, null, Orders.Monday, IraqHubs.Default);

        Assert.Equal(TimeSpan.Zero, parcel.PromisedAt!.Value.Offset);
        Assert.Equal(request.PromisedAt, parcel.PromisedAt);
        Assert.Equal(parcel.PromisedAt, parcel.EtaAt);
    }

    [Fact]
    public void International_freight_needs_a_mode_has_no_pickup_and_no_seller_hub()
    {
        var request = new CreateShipmentRequest
        {
            ServiceType = ServiceType.International,
            Mode = null,
            ShipperName = "Mersin Textiles",
            ConsigneeName = "Baghdad Buyer",
            OriginCode = "TRMER",
            DestinationCode = "IQUQR",
        };
        Assert.Equal("shipment.mode_required", Code(() => Shipment.Book(Tenants.Default, "LX26000000017", request, null, null, Orders.Monday, IraqHubs.Default)));

        var parcel = Orders.International();
        Assert.False(parcel.PickupRequired);
        Assert.Null(parcel.OriginHubCode);
        Assert.Null(parcel.PromisedAt);

        var noCurrency = Orders.Request();
        noCurrency.DeclaredValue = 1_000m;
        noCurrency.DeclaredCurrency = " ";
        Assert.Equal("shipment.declared_currency", Code(() => Shipment.Book(Tenants.Default, "LX26000000017", noCurrency, Orders.Shop, null, Orders.Monday, IraqHubs.Default)));
    }

    // ── Customer confirmation ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_order_waiting_for_the_customer_stays_with_the_seller_until_they_confirm()
    {
        var trip = new Trip(Orders.Book());
        trip.Parcel.RequireConfirmation(trip.Now);
        Assert.True(trip.Parcel.AwaitsConfirmation);
        Assert.Equal(trip.Now, trip.Parcel.ConfirmationRequestedAt);

        Assert.Equal("shipment.awaiting_confirmation", Code(() => trip.ReceiveAt(Orders.Baghdad)));
        Assert.Equal("shipment.awaiting_confirmation",
            Code(() => trip.Parcel.AdvanceTo(ShipmentStatus.PickedUp, trip.Next(), new MilestoneDetails(), "user", "driver", trip.Now)));

        trip.Parcel.RecordConfirmation(ConfirmationOutcome.NoAnswer, "  ", Orders.Agent, trip.Next());
        trip.Parcel.RecordConfirmation(ConfirmationOutcome.NoAnswer, null, Orders.Agent, trip.Next());
        Assert.Equal(2, trip.Parcel.ConfirmationAttempts);
        Assert.True(trip.Parcel.AwaitsConfirmation);

        trip.Parcel.RecordConfirmation(ConfirmationOutcome.Confirmed, "أكدت بالهاتف", Orders.Agent, trip.Next());
        Assert.Equal(ConfirmationStatus.Confirmed, trip.Parcel.Confirmation);
        Assert.Equal(("agent", trip.Now), (trip.Parcel.ConfirmationBy, trip.Parcel.ConfirmationDecidedAt));
        Assert.False(trip.Parcel.AwaitsConfirmation);

        trip.ReceiveAt(Orders.Baghdad);
        Assert.Equal(ShipmentStatus.AtWarehouse, trip.Parcel.Status);
    }

    [Fact]
    public void A_customer_who_declines_cancels_the_order_before_it_is_collected()
    {
        var parcel = Orders.Book();
        parcel.RequireConfirmation(Orders.Monday);

        parcel.RecordConfirmation(ConfirmationOutcome.Declined, " غيّرت رأيها ", new Actor("customer", "system"), Orders.Monday.AddHours(1));

        Assert.Equal(ShipmentStatus.Cancelled, parcel.Status);
        Assert.Equal(ConfirmationStatus.Declined, parcel.Confirmation);
        Assert.Equal("غيّرت رأيها", parcel.Events[^1].Notes);
        Assert.False(parcel.AwaitsConfirmation);
    }

    /// <summary>Regression: a cancelled order kept asking the customer to confirm it on the tracking page.</summary>
    [Fact]
    public void A_cancelled_order_no_longer_waits_for_the_customer_to_confirm_it()
    {
        var parcel = Orders.Book();
        parcel.RequireConfirmation(Orders.Monday);
        parcel.Cancel("الزبون اتصل وألغى", Orders.Agent, Orders.Monday.AddHours(1));

        Assert.Equal(ShipmentStatus.Cancelled, parcel.Status);
        Assert.Equal(ConfirmationStatus.Pending, parcel.Confirmation); // the question was asked…
        Assert.False(parcel.AwaitsConfirmation);                        // …but nobody waits for the answer any more
        Assert.False(ShipmentProjections.ToPublicView(parcel, Orders.Monday.AddHours(2), 0).AwaitingConfirmation);

        foreach (var outcome in Enum.GetValues<ConfirmationOutcome>())
        {
            Assert.Equal("shipment.confirmation_not_pending",
                Code(() => parcel.RecordConfirmation(outcome, null, new Actor("customer", "system"), Orders.Monday.AddHours(3))));
        }

        Assert.Equal(ShipmentStatus.Cancelled, parcel.Status);
        Assert.Equal(0, parcel.ConfirmationAttempts);
    }

    [Fact]
    public void Confirmation_is_only_asked_for_orders_still_with_the_seller_and_only_once()
    {
        var collected = new Trip(Orders.Book()).ReceiveAt(Orders.Baghdad);
        collected.Parcel.RequireConfirmation(collected.Next());
        Assert.Equal(ConfirmationStatus.NotRequired, collected.Parcel.Confirmation);

        var decided = Orders.Book();
        decided.RequireConfirmation(Orders.Monday);
        decided.RecordConfirmation(ConfirmationOutcome.Confirmed, null, Orders.Agent, Orders.Monday.AddHours(1));
        decided.RequireConfirmation(Orders.Monday.AddHours(2));
        Assert.Equal(ConfirmationStatus.Confirmed, decided.Confirmation);
    }

    // ── At the hub ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_second_receive_scan_moves_the_parcel_to_the_new_shelf_without_changing_its_status()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad, "a-01-1");
        var history = trip.Parcel.Events.Count;

        trip.ReceiveAt(Orders.Baghdad, " b-02-3 ");

        Assert.Equal(ShipmentStatus.AtWarehouse, trip.Parcel.Status);
        Assert.Equal("B-02-3", trip.Parcel.WarehouseBin);
        Assert.Equal(history + 1, trip.Parcel.Events.Count);
        Assert.Equal((ShipmentStatus.AtWarehouse, "B-02-3", IraqHubs.Baghdad), (trip.Parcel.Events[^1].Status, trip.Parcel.Events[^1].WarehouseBin, trip.Parcel.Events[^1].Location));

        trip.ReceiveAt(Orders.Baghdad); // no shelf typed: it stays where it is
        Assert.Equal("B-02-3", trip.Parcel.WarehouseBin);
    }

    [Fact]
    public void A_parcel_with_a_courier_or_whose_journey_ended_cannot_be_received()
    {
        var withCourier = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal("shipment.with_courier", Code(() => withCourier.ReceiveAt(Orders.Baghdad)));

        var delivered = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        Assert.Equal("shipment.invalid_transition", Code(() => delivered.ReceiveAt(Orders.Baghdad)));

        var cancelled = new Trip(Orders.Book());
        cancelled.Parcel.Cancel(null, Orders.Agent, cancelled.Next());
        Assert.Equal("shipment.invalid_transition", Code(() => cancelled.ReceiveAt(Orders.Baghdad)));
    }

    [Fact]
    public void Moving_to_a_shelf_works_for_parcels_on_a_shelf_and_returns_waiting_for_the_seller()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad);
        trip.Parcel.StartReturn(ReturnReason.SellerCancelled, null, Orders.Agent, trip.Next());
        Assert.Equal(ShipmentStatus.ReturnAtHub, trip.Parcel.Status);

        trip.Parcel.MoveToBin("r-01", "clerk", trip.Next(), IraqHubs.Baghdad);
        Assert.Equal("R-01", trip.Parcel.WarehouseBin);

        var withCourier = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal("shipment.not_in_warehouse", Code(() => withCourier.Parcel.MoveToBin("A-01-1", "clerk", withCourier.Next())));
    }

    // ── With the courier ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handing_a_parcel_to_a_courier_needs_a_name_and_takes_it_off_the_shelf()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad, "A-01-1");
        Assert.Equal("shipment.courier_required", Code(() => trip.Parcel.DispatchToCourier(Orders.Baghdad, " ", Orders.Clerk, trip.Next())));

        trip.Dispatch(Orders.Baghdad);

        Assert.Equal(ShipmentStatus.OutForDelivery, trip.Parcel.Status);
        Assert.Null(trip.Parcel.WarehouseBin);
        Assert.Null(trip.Parcel.CurrentHubCode);
        Assert.Equal(Orders.CourierName, trip.Parcel.CourierName);
        Assert.Equal(1, trip.Parcel.DeliveryAttempts);
    }

    [Fact]
    public void An_order_still_with_the_seller_cannot_go_straight_out_for_delivery()
    {
        var parcel = Orders.Book("IQ-BG");

        Assert.Equal("shipment.invalid_transition", Code(() => parcel.DispatchToCourier(Orders.Baghdad, Orders.CourierName, Orders.Clerk, Orders.Monday.AddHours(1))));
    }

    [Fact]
    public void A_second_attempt_goes_out_again_and_delivery_collects_the_cash()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal(FailureFollowUp.Reschedule, trip.Fail(DeliveryFailureReason.NoAnswer).FollowUp);
        Assert.NotNull(trip.Parcel.RescheduledTo);

        trip.Dispatch(Orders.Baghdad); // the courier tries again the next day without bringing it back
        Assert.Equal(2, trip.Parcel.DeliveryAttempts);
        Assert.Null(trip.Parcel.RescheduledTo);

        trip.Deliver();
        Assert.Equal(ShipmentStatus.Delivered, trip.Parcel.Status);
        Assert.Equal(trip.Now, trip.Parcel.DeliveredAt);
        Assert.Equal(trip.Now, trip.Parcel.CodCollectedAt);
    }

    [Fact]
    public void A_prepaid_order_is_delivered_without_collecting_cash()
    {
        var trip = new Trip(Orders.Book("IQ-BG", cod: null)).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();

        Assert.NotNull(trip.Parcel.DeliveredAt);
        Assert.Null(trip.Parcel.CodCollectedAt);
    }

    [Fact]
    public void A_step_may_be_up_to_five_minutes_ahead_of_the_server_clock_but_not_more()
    {
        var parcel = Orders.Book();
        var now = Orders.Monday.AddHours(1);

        Assert.Equal("shipment.event_in_future", Code(() => parcel.AdvanceTo(ShipmentStatus.PickedUp, now.AddMinutes(6), new MilestoneDetails(), "user", null, now)));
        parcel.AdvanceTo(ShipmentStatus.PickedUp, now.AddMinutes(4), new MilestoneDetails(), "user", null, now);
        Assert.Equal(ShipmentStatus.PickedUp, parcel.Status);
    }

    // ── Failed attempts ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_answer_is_tried_again_on_the_next_working_day()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);

        var decision = trip.Fail(DeliveryFailureReason.NoAnswer);

        Assert.Equal(FailureFollowUp.Reschedule, decision.FollowUp);
        Assert.Equal(ShipmentStatus.DeliveryFailed, trip.Parcel.Status);
        Assert.Equal(DeliveryFailureReason.NoAnswer, trip.Parcel.LastFailureReason);
        Assert.Equal(Baghdad20h, trip.Parcel.RescheduledTo);
        Assert.Equal(Baghdad20h, trip.Parcel.EtaAt);
        Assert.Equal(DeliveryFailureReason.NoAnswer, trip.Parcel.Events[^1].FailureReason);
    }

    [Fact]
    public void A_failure_on_thursday_is_tried_again_on_saturday()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad);
        trip.Next(72); // Thursday
        trip.Dispatch(Orders.Baghdad);

        trip.Fail(DeliveryFailureReason.Unreachable);

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(3)), trip.Parcel.RescheduledTo);
    }

    [Fact]
    public void A_refusal_sends_the_parcel_back_at_once_without_collecting_cash()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);

        var decision = trip.Fail(DeliveryFailureReason.CustomerRefused);

        Assert.Equal((FailureFollowUp.Return, ReturnReason.CustomerRefused), (decision.FollowUp, decision.ReturnReason));
        Assert.Equal(ShipmentStatus.ReturnInitiated, trip.Parcel.Status);
        Assert.Equal(ReturnReason.CustomerRefused, trip.Parcel.ReturnReason);
        Assert.Null(trip.Parcel.CodCollectedAt);
        Assert.Null(trip.Parcel.RescheduledTo);
        Assert.Null(trip.Parcel.CurrentHubCode); // the courier still has it
        Assert.Equal("system", trip.Parcel.Events[^1].Source);
    }

    [Theory]
    [InlineData(false, true, DeliveryFailureReason.CustomerRefused, FailureFollowUp.Reschedule)]
    [InlineData(true, false, DeliveryFailureReason.NoAnswer, FailureFollowUp.None)]
    public void The_company_rules_decide_what_follows_a_failed_attempt(bool returnOnRefusal, bool autoReschedule, DeliveryFailureReason reason, FailureFollowUp expected)
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);

        var decision = trip.Fail(reason, new AutomationPolicy { ReturnOnRefusal = returnOnRefusal, AutoReschedule = autoReschedule });

        Assert.Equal(expected, decision.FollowUp);
        Assert.Equal(ShipmentStatus.DeliveryFailed, trip.Parcel.Status);
        Assert.Equal(expected == FailureFollowUp.Reschedule, trip.Parcel.RescheduledTo is not null);
    }

    [Fact]
    public void A_failed_attempt_needs_a_reason()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);

        Assert.Equal("shipment.failure_reason_required",
            Code(() => trip.Parcel.AdvanceTo(ShipmentStatus.DeliveryFailed, trip.Next(), new MilestoneDetails(), "user", "courier", trip.Now)));
    }

    // ── Returns ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_return_that_is_already_at_the_seller_hub_is_ready_to_hand_over_at_once()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad, "A-01-1");

        trip.Parcel.StartReturn(ReturnReason.SellerCancelled, null, Orders.Agent, trip.Next());

        Assert.Equal(ShipmentStatus.ReturnAtHub, trip.Parcel.Status);
        Assert.Equal(new[] { ShipmentStatus.ReturnInitiated, ShipmentStatus.ReturnAtHub }, trip.Parcel.Events.TakeLast(2).Select(e => e.Status));
        Assert.Equal(trip.Now, trip.Parcel.ReturnRequestedAt);
        Assert.False(trip.Parcel.NeedsTransfer);
    }

    [Fact]
    public void A_return_from_the_customer_city_travels_back_and_is_handed_to_the_seller_with_its_fee()
    {
        var trip = new Trip(Orders.Book()).ReceiveAt(Orders.Basra, "B-01-1");

        trip.Parcel.StartReturn(ReturnReason.WrongAddress, "العنوان غير موجود", Orders.Agent, trip.Next());
        Assert.Equal(ShipmentStatus.ReturnInitiated, trip.Parcel.Status);
        Assert.Equal(IraqHubs.Baghdad, trip.Parcel.TargetHubCode);
        Assert.True(trip.Parcel.NeedsTransfer);

        var eta = trip.Parcel.EtaAt;
        var truck = trip.Truck(Orders.Basra, Orders.Baghdad);
        Assert.Equal(ShipmentStatus.ReturnInTransit, trip.Parcel.Status);
        Assert.True(Assert.Single(truck.Items).IsReturn);
        Assert.Equal(eta, trip.Parcel.EtaAt); // a return does not move the customer's ETA

        truck.Receive(trip.Parcel, "clerk", trip.Next());
        trip.ReceiveAt(Orders.Baghdad, "R-01");
        Assert.Equal(ShipmentStatus.ReturnAtHub, trip.Parcel.Status);
        Assert.Null(trip.Parcel.TransferId);

        trip.Parcel.HandOverToSeller("RM-2026-000001", "أبو حسن", 4_000m, Orders.Clerk, trip.Next());
        Assert.Equal(ShipmentStatus.Returned, trip.Parcel.Status);
        Assert.Equal((4_000m, "RM-2026-000001", trip.Now), (trip.Parcel.ReturnFee, trip.Parcel.ReturnManifestNumber, trip.Parcel.ReturnedAt));
        Assert.Equal("RM-2026-000001 · أبو حسن", trip.Parcel.Events[^1].Notes);
        Assert.Null(trip.Parcel.CurrentHubCode);
    }

    /// <summary>
    /// Regression: a return waiting at the seller's hub that was scanned in at another hub stayed "waiting at the
    /// seller's hub" there: never planned onto a truck, and offered for hand-over at the wrong hub.
    /// </summary>
    [Fact]
    public void A_return_taken_away_from_the_seller_hub_needs_a_truck_back()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad);
        trip.Parcel.StartReturn(ReturnReason.SellerCancelled, null, Orders.Agent, trip.Next());
        Assert.Equal(ShipmentStatus.ReturnAtHub, trip.Parcel.Status);

        trip.ReceiveAt(Orders.Najaf, "N-01");

        Assert.Equal(ShipmentStatus.ReturnInitiated, trip.Parcel.Status);
        Assert.Equal(IraqHubs.Najaf, trip.Parcel.CurrentHubCode);
        Assert.True(trip.Parcel.NeedsTransfer);
        Assert.Equal("shipment.return_not_at_hub", Code(() => trip.Parcel.HandOverToSeller("RM-2026-000002", null, 0m, Orders.Clerk, trip.Next())));

        trip.Truck(Orders.Najaf, Orders.Baghdad);
        trip.ReceiveAt(Orders.Baghdad);
        Assert.Equal(ShipmentStatus.ReturnAtHub, trip.Parcel.Status);
    }

    [Fact]
    public void A_return_on_a_truck_that_stops_at_another_hub_waits_there_for_the_next_truck()
    {
        var trip = new Trip(Orders.Book()).ReceiveAt(Orders.Basra);
        trip.Parcel.StartReturn(ReturnReason.CustomerRefused, null, Orders.Agent, trip.Next());
        trip.Truck(Orders.Basra, Orders.Baghdad);

        trip.ReceiveAt(Orders.Najaf);

        Assert.Equal(ShipmentStatus.ReturnInitiated, trip.Parcel.Status);
        Assert.Equal(IraqHubs.Najaf, trip.Parcel.CurrentHubCode);
        Assert.True(trip.Parcel.NeedsTransfer);
        Assert.Null(trip.Parcel.TransferId);
    }

    [Fact]
    public void A_return_cannot_start_twice_or_while_the_courier_has_the_parcel()
    {
        var withCourier = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal("shipment.with_courier", Code(() => withCourier.Parcel.StartReturn(ReturnReason.Other, null, Orders.Agent, withCourier.Next())));

        var returning = new Trip(Orders.Book()).ReceiveAt(Orders.Basra);
        returning.Parcel.StartReturn(ReturnReason.Other, null, Orders.Agent, returning.Next());
        Assert.Equal("shipment.already_returning", Code(() => returning.Parcel.StartReturn(ReturnReason.Other, null, Orders.Agent, returning.Next())));

        var cancelled = Orders.Book();
        cancelled.Cancel(null, Orders.Agent, Orders.Monday.AddHours(1));
        Assert.Equal("shipment.already_returning", Code(() => cancelled.StartReturn(ReturnReason.Other, null, Orders.Agent, Orders.Monday.AddHours(2))));
    }

    [Fact]
    public void A_customer_return_after_delivery_keeps_the_delivery_on_record_but_cannot_be_rated()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        var deliveredAt = trip.Parcel.DeliveredAt;

        trip.Parcel.StartReturn(ReturnReason.CustomerReturn, null, new Actor("customer", "system"), trip.Next());

        Assert.Equal(ShipmentStatus.ReturnInitiated, trip.Parcel.Status);
        Assert.Null(trip.Parcel.CurrentHubCode); // the courier collects it from the customer
        Assert.Equal(deliveredAt, trip.Parcel.DeliveredAt);
        Assert.False(trip.Parcel.CanBeRated(trip.Now));
    }

    [Fact]
    public void A_parcel_whose_cash_was_paid_to_the_seller_cannot_be_returned()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        trip.Parcel.MarkCodSettled(Guid.CreateVersion7(), trip.Next());

        Assert.Equal("shipment.return_after_payout", Code(() => trip.Parcel.StartReturn(ReturnReason.CustomerReturn, null, Orders.Agent, trip.Next())));
    }

    [Fact]
    public void Only_a_return_waiting_at_the_hub_is_handed_over_and_a_free_return_has_no_fee()
    {
        var trip = new Trip(Orders.Book()).ReceiveAt(Orders.Basra);
        trip.Parcel.StartReturn(ReturnReason.Damaged, null, Orders.Agent, trip.Next());
        Assert.Equal("shipment.return_not_at_hub", Code(() => trip.Parcel.HandOverToSeller("RM-2026-000003", null, 0m, Orders.Clerk, trip.Next())));

        var atSellerHub = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad);
        atSellerHub.Parcel.StartReturn(ReturnReason.Damaged, null, Orders.Agent, atSellerHub.Next());
        atSellerHub.Parcel.HandOverToSeller("RM-2026-000004", null, 0m, Orders.Clerk, atSellerHub.Next());
        Assert.Null(atSellerHub.Parcel.ReturnFee);
        Assert.Equal("RM-2026-000004", atSellerHub.Parcel.Events[^1].Notes);
    }

    // ── Cancelling ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancelling_before_collection_cancels_and_afterwards_sends_the_parcel_back()
    {
        var booked = Orders.Book();
        booked.Cancel("طلب مكرر", Orders.Agent, Orders.Monday.AddHours(1));
        Assert.Equal(ShipmentStatus.Cancelled, booked.Status);
        Assert.Equal("طلب مكرر", booked.Events[^1].Notes);
        Assert.True(ShipmentLifecycle.IsTerminal(ServiceType.Domestic, booked.Status));

        var collected = new Trip(Orders.Book());
        collected.Parcel.AdvanceTo(ShipmentStatus.PickedUp, collected.Next(), new MilestoneDetails(CourierName: "سائق"), "user", "driver", collected.Now);
        collected.Parcel.Cancel("البائع ألغى", Orders.Agent, collected.Next());
        Assert.Equal(ShipmentStatus.ReturnInitiated, collected.Parcel.Status);
        Assert.Equal(ReturnReason.SellerCancelled, collected.Parcel.ReturnReason);
    }

    // ── Corrections ─────────────────────────────────────────────────────────────────────────────────────────

    private static UpdateOrderRequest EditOf(Shipment parcel) => UpdateOrderRequest.From(ShipmentProjections.ToDetails(parcel));

    [Fact]
    public void Saving_an_order_unchanged_records_nothing()
    {
        var parcel = Orders.Book();
        var history = parcel.Events.Count;

        var changes = parcel.EditOrder(EditOf(parcel), IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1));

        Assert.Empty(changes);
        Assert.Equal(history, parcel.Events.Count);
    }

    [Fact]
    public void An_edit_records_what_changed_and_can_remove_the_cash_on_delivery()
    {
        var parcel = Orders.Book();
        var edit = EditOf(parcel);
        edit.CodAmount = 70_000m;
        edit.ConsigneeName = " زينب علي ";

        var changes = parcel.EditOrder(edit, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1));

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Contains("65,000 → 70,000", StringComparison.Ordinal));
        Assert.Equal(("زينب علي", 70_000m, "IQD"), (parcel.ConsigneeName, parcel.CodAmount, parcel.CodCurrency));
        Assert.StartsWith("✎ ", parcel.Events[^1].Notes, StringComparison.Ordinal);

        var prepaid = EditOf(parcel);
        prepaid.CodAmount = 0;
        parcel.EditOrder(prepaid, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(2));
        Assert.Null(parcel.CodAmount);
        Assert.Null(parcel.CodCurrency);
    }

    /// <summary>Regression: a new governorate moved the parcel to another hub but kept the old lane's ETA (a late alert on day one).</summary>
    [Fact]
    public void A_new_governorate_moves_the_destination_hub_and_the_eta_to_the_new_lane()
    {
        var parcel = Orders.Book("IQ-BG");
        Assert.Equal(Baghdad20h, parcel.EtaAt);
        var edit = EditOf(parcel);
        edit.DestinationCode = "IQ-BA";

        parcel.EditOrder(edit, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1));

        Assert.Equal((IraqHubs.Basra, "IQ-BA"), (parcel.DestinationHubCode, parcel.DestinationCode));
        Assert.Equal(Thursday20h, parcel.EtaAt);
        Assert.Equal(Baghdad20h, parcel.PromisedAt); // what was promised at booking stays on record
    }

    [Fact]
    public void A_shorter_lane_never_brings_the_eta_forward_and_an_agreed_date_stays()
    {
        var far = Orders.Book("IQ-BA");
        var closer = EditOf(far);
        closer.DestinationCode = "IQ-BG";
        far.EditOrder(closer, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1));
        Assert.Equal(IraqHubs.Baghdad, far.DestinationHubCode);
        Assert.Equal(Thursday20h, far.EtaAt);

        var agreed = Orders.Book("IQ-BG");
        var wednesday = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(3));
        agreed.Reschedule(wednesday, Orders.Agent, Orders.Monday.AddHours(1));
        agreed.ChangeAddress("IQ-BA", null, "حي الجزائر", null, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(2));
        Assert.Equal(IraqHubs.Basra, agreed.DestinationHubCode);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(3)), agreed.EtaAt);
    }

    [Fact]
    public void An_address_change_to_another_governorate_moves_the_eta_too()
    {
        var parcel = Orders.Book("IQ-BG");

        parcel.ChangeAddress("IQ-BA", "العشار", "شارع الكورنيش", "قرب الجسر", IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1));

        Assert.Equal(("IQ-BA", IraqHubs.Basra), (parcel.DestinationCode, parcel.DestinationHubCode));
        Assert.Equal(Thursday20h, parcel.EtaAt);
        Assert.Equal(("العشار", "شارع الكورنيش", "قرب الجسر"), (parcel.DestinationCity, parcel.DeliveryAddress, parcel.Landmark));
        Assert.Equal("shipment.domestic_address",
            Code(() => parcel.ChangeAddress("IQ-XX", null, "؟", null, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(2))));
    }

    [Fact]
    public void An_order_out_with_a_courier_on_its_way_back_or_from_abroad_cannot_be_corrected()
    {
        var withCourier = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal("shipment.not_changeable", Code(() => withCourier.Parcel.EditOrder(EditOf(withCourier.Parcel), IraqHubs.Default, Orders.Agent, withCourier.Next())));
        Assert.Equal("shipment.not_changeable", Code(() => withCourier.Parcel.Reschedule(Orders.Monday.AddDays(2), Orders.Agent, withCourier.Next())));

        var returning = new Trip(Orders.Book()).ReceiveAt(Orders.Basra);
        returning.Parcel.StartReturn(ReturnReason.Other, null, Orders.Agent, returning.Next());
        Assert.Equal("shipment.not_changeable", Code(() => returning.Parcel.ChangePhone("07801234567", Orders.Agent, returning.Next())));

        var international = Orders.International();
        Assert.Equal("shipment.not_changeable", Code(() => international.EditOrder(new UpdateOrderRequest { ConsigneePhone = "07701234567", DestinationCode = "IQ-BG", DeliveryAddress = "x" }, IraqHubs.Default, Orders.Agent, Orders.Monday.AddHours(1))));
    }

    [Fact]
    public void A_new_phone_is_normalised_and_a_bad_one_refused()
    {
        var parcel = Orders.Book();

        parcel.ChangePhone("0780 765 4321", Orders.Agent, Orders.Monday.AddHours(1));
        Assert.Equal("+9647807654321", parcel.ConsigneePhone);

        Assert.Equal("shipment.domestic_address", Code(() => parcel.ChangePhone("123", Orders.Agent, Orders.Monday.AddHours(2))));
        Assert.Equal("+9647807654321", parcel.ConsigneePhone);
    }

    [Fact]
    public void A_new_delivery_date_is_the_end_of_that_day_in_baghdad()
    {
        var parcel = Orders.Book();

        parcel.Reschedule(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(3)), Orders.Agent, Orders.Monday.AddHours(1));

        var saturday = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal((saturday, saturday), (parcel.RescheduledTo, parcel.EtaAt));
        Assert.Equal("→ 2026-10-24", parcel.Events[^1].Notes);
    }

    // ── Line-haul links ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_truck_link_is_only_cleared_by_the_truck_it_belongs_to_and_departure_pushes_the_eta()
    {
        var trip = new Trip(Orders.Book()).ReceiveAt(Orders.Baghdad);
        var mine = Guid.CreateVersion7();
        trip.Parcel.AssignToTransfer(mine, trip.Next());

        trip.Parcel.RemoveFromTransfer(Guid.CreateVersion7(), trip.Next());
        Assert.Equal(mine, trip.Parcel.TransferId);
        trip.Parcel.RemoveFromTransfer(mine, trip.Next());
        Assert.Null(trip.Parcel.TransferId);

        // Leaving on Thursday night, after the promised time: the customer's ETA moves to the truck's arrival.
        trip.Next(84);
        trip.Parcel.DepartOnTransfer("TR-2026-000009", IraqHubs.Baghdad, IraqHubs.Basra, 7, Orders.Clerk, trip.Now);
        Assert.Equal(ShipmentStatus.InTransitToHub, trip.Parcel.Status);
        Assert.Equal(trip.Now.AddHours(7), trip.Parcel.EtaAt);
        Assert.Null(trip.Parcel.CurrentHubCode);
    }

    [Fact]
    public void A_retired_hub_hands_its_parcels_to_the_hub_that_now_covers_their_governorate()
    {
        var parcel = Orders.Book();
        var network = new HubNetwork(IraqHubs.Standard.Where(h => h.Code != IraqHubs.Basra));

        Assert.True(parcel.RerouteAround(IraqHubs.Basra, network, Orders.Monday.AddHours(1)));
        Assert.NotEqual(IraqHubs.Basra, parcel.DestinationHubCode);
        Assert.False(parcel.RerouteAround(IraqHubs.Mosul, network, Orders.Monday.AddHours(2)));
    }

    // ── Rating ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_delivery_is_rated_once_within_two_weeks()
    {
        var undelivered = Orders.Book();
        Assert.Equal("shipment.rating_not_possible", Code(() => undelivered.Rate(5, null, Orders.Monday)));

        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        var late = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        Assert.Equal("shipment.rating_not_possible", Code(() => late.Parcel.Rate(4, null, late.Now.AddDays(15))));

        trip.Parcel.Rate(9, "  " + new string('ج', 600) + "  ", trip.Now.AddDays(1));
        Assert.Equal(5, trip.Parcel.Rating);
        Assert.Equal(500, trip.Parcel.RatingComment!.Length);
        Assert.Equal("shipment.rating_not_possible", Code(() => trip.Parcel.Rate(1, null, trip.Now.AddDays(2))));
    }

    // ── Cash ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cash_is_handed_in_once_and_only_after_it_was_collected()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad);
        Assert.Equal("cash.not_with_courier", Code(() => trip.Parcel.MarkCashHandedOver(Guid.CreateVersion7(), trip.Next())));

        trip.Deliver();
        var handover = Guid.CreateVersion7();
        trip.Parcel.MarkCashHandedOver(handover, trip.Next());
        Assert.Equal((handover, trip.Now), (trip.Parcel.CashHandoverId, trip.Parcel.CodHandedOverAt));
        Assert.Equal("cash.not_with_courier", Code(() => trip.Parcel.MarkCashHandedOver(Guid.CreateVersion7(), trip.Next())));
    }

    /// <summary>Regression: nothing stopped the same parcel's cash (or return fee) from going into a second payout.</summary>
    [Fact]
    public void A_parcel_is_settled_with_the_seller_only_once()
    {
        var trip = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver();
        var first = Guid.CreateVersion7();
        trip.Parcel.MarkCodSettled(first, trip.Next());

        Assert.Equal("shipment.cod_already_settled", Code(() => trip.Parcel.MarkCodSettled(Guid.CreateVersion7(), trip.Next())));
        Assert.Equal(first, trip.Parcel.CodSettlementId);
    }

    // ── What a person may record by hand ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression: the details-page endpoint accepted any allowed transition, so a person could mark a parcel "returned"
    /// without a hand-over manifest or return fee, put it "between hubs" without a truck, or release it from customs
    /// without e-verification.
    /// </summary>
    [Theory]
    [InlineData(ShipmentStatus.InTransitToHub)]
    [InlineData(ShipmentStatus.ReturnInTransit)]
    [InlineData(ShipmentStatus.ReturnAtHub)]
    [InlineData(ShipmentStatus.Returned)]
    [InlineData(ShipmentStatus.CustomsPending)]
    [InlineData(ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.Released)]
    public void Steps_that_belong_to_a_workflow_are_never_recorded_by_hand(ShipmentStatus step)
    {
        Assert.Equal("shipment.step_by_workflow", Code(() => Orders.Book().EnsureRecordableByHand(step)));
        Assert.Equal("shipment.step_by_workflow", Code(() => Orders.International().EnsureRecordableByHand(step)));
    }

    [Fact]
    public void A_parcel_on_a_truck_comes_off_it_by_a_scan_not_by_hand()
    {
        var trip = new Trip(Orders.Book()).ReceiveAt(Orders.Baghdad);
        trip.Truck(Orders.Baghdad, Orders.Basra);

        Assert.Equal("shipment.step_by_workflow", Code(() => trip.Parcel.EnsureRecordableByHand(ShipmentStatus.AtWarehouse)));
    }

    [Theory]
    [InlineData(ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.AtWarehouse)]
    [InlineData(ShipmentStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.DeliveryFailed)]
    [InlineData(ShipmentStatus.ReturnInitiated)]
    [InlineData(ShipmentStatus.Cancelled)]
    public void Ordinary_steps_stay_open_to_a_person(ShipmentStatus step)
    {
        var parcel = Orders.Book();

        parcel.EnsureRecordableByHand(step);

        Assert.Equal(ShipmentStatus.Booked, parcel.Status);
    }
}
