using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>Cash a courier hands in, payouts to sellers, and the requests customers and sellers make on an order.</summary>
public class CashAndRequestDomainTests
{
    private static readonly DateTimeOffset Evening = Orders.Monday.AddHours(12);

    private static string Code(Action act) => Assert.Throws<DomainException>(act).Code;

    private static Courier Mustafa() => Courier.Create(Tenants.Default, new CourierRequest
    {
        Code = " BG-01 ",
        Name = " مصطفى ",
        Phone = "0770 123 4567",
        Governorate = "IQ-BG",
    }, Orders.Monday);

    private static Shipment Delivered(string tracking, decimal? cod, decimal? fee = 5_000m) =>
        new Trip(Orders.Book("IQ-BG", cod, fee, tracking)).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver().Parcel;

    /// <summary>A parcel handed back to the seller at the Baghdad hub, <paramref name="hoursLater"/> after the others, with its return fee.</summary>
    private static Shipment Returned(string tracking, decimal fee, int hoursLater)
    {
        var trip = new Trip(Orders.Book("IQ-BG", tracking: tracking)).ReceiveAt(Orders.Baghdad);
        trip.Parcel.StartReturn(ReturnReason.CustomerRefused, null, Orders.Agent, trip.Next(hoursLater));
        trip.Parcel.HandOverToSeller("RM-2026-000001", null, fee, Orders.Clerk, trip.Next());
        return trip.Parcel;
    }

    // ── Cash hand-in ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_hand_in_counts_against_what_the_courier_collected_and_keeps_the_shortage_on_record()
    {
        var a = Delivered("LX26000000017", 65_000m);
        var b = Delivered("LX26000000025", 40_000m);

        var handover = CashHandover.Create(Tenants.Default, "CH-2026-000001", Mustafa(), [a, b], 100_000m, "  ", "cashier", Evening);

        Assert.Equal((2, 105_000m, 100_000m, -5_000m), (handover.ParcelCount, handover.ExpectedIqd, handover.ReceivedIqd, handover.DifferenceIqd));
        Assert.Equal("مصطفى (BG-01)", handover.CourierLabel);
        Assert.Null(handover.Notes);
        Assert.All(new[] { a, b }, p => Assert.Equal((handover.Id, Evening), (p.CashHandoverId, p.CodHandedOverAt)));
    }

    [Fact]
    public void Without_parcels_a_courier_can_only_pay_back_what_they_owe()
    {
        Assert.Equal("cash.nothing_to_hand_in", Code(() => CashHandover.Create(Tenants.Default, "CH-1", Mustafa(), [], 5_000m, null, null, Evening)));
        Assert.Equal("cash.nothing_to_hand_in", Code(() => CashHandover.Create(Tenants.Default, "CH-1", Mustafa(), [], 0m, null, null, Evening, shortageIqd: 5_000m)));
        Assert.Equal("cash.repayment_too_high", Code(() => CashHandover.Create(Tenants.Default, "CH-1", Mustafa(), [], 6_000m, null, null, Evening, shortageIqd: 5_000m)));

        var repayment = CashHandover.Create(Tenants.Default, "CH-2", Mustafa(), [], 5_000m, "سدد النقص", null, Evening, shortageIqd: 5_000m);
        Assert.Equal((0, 0m, 5_000m), (repayment.ParcelCount, repayment.ExpectedIqd, repayment.DifferenceIqd));
    }

    [Fact]
    public void A_negative_amount_is_refused_before_any_parcel_is_marked()
    {
        var parcel = Delivered("LX26000000017", 65_000m);

        Assert.Equal("cash.negative", Code(() => CashHandover.Create(Tenants.Default, "CH-1", Mustafa(), [parcel], -1m, null, null, Evening)));
        Assert.Null(parcel.CodHandedOverAt);
    }

    // ── Seller payouts ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_payout_needs_delivered_cash()
    {
        Assert.Equal("finance.nothing_to_settle",
            Code(() => CodSettlement.Create(Tenants.Default, "STL-1", Orders.Shop.Id, Orders.Shop.Name, [], [Returned("LX26000000017", 4_000m, 1)], PayoutMethod.Cash, null, null, null, Evening)));
    }

    [Fact]
    public void Return_fees_come_off_the_payout_oldest_first_and_a_fee_that_does_not_fit_waits()
    {
        var sale = Delivered("LX26000000017", 50_000m, fee: 5_000m); // 45,000 left after the delivery fee
        var oldest = Returned("LX26000000025", 30_000m, 1);
        var middle = Returned("LX26000000033", 20_000m, 2);
        var newest = Returned("LX26000000041", 10_000m, 3);

        var payout = CodSettlement.Create(Tenants.Default, "STL-2026-0001", Orders.Shop.Id, Orders.Shop.Name, [sale], [newest, middle, oldest],
            PayoutMethod.ZainCash, " ZC-1 ", null, "finance", Evening.AddDays(1));

        Assert.Equal((50_000m, 5_000m, 2, 40_000m, 5_000m), (payout.GrossCodAmount, payout.DeliveryFeesAmount, payout.ReturnCount, payout.ReturnFeesAmount, payout.NetPayoutAmount));
        Assert.Equal("ZC-1", payout.PayoutReference);
        Assert.All(new[] { sale, oldest, newest }, p => Assert.Equal(payout.Id, p.CodSettlementId));
        Assert.Null(middle.CodSettlementId); // 20,000 did not fit in the 15,000 left: it waits for the next payout
    }

    [Fact]
    public void Fees_larger_than_the_cash_make_a_negative_payout_and_take_no_return_fee()
    {
        var sale = Delivered("LX26000000017", 3_000m, fee: 5_000m);
        var returned = Returned("LX26000000025", 1_000m, 1);

        var payout = CodSettlement.Create(Tenants.Default, "STL-2026-0002", Orders.Shop.Id, Orders.Shop.Name, [sale], [returned], PayoutMethod.Cash, null, null, null, Evening.AddDays(1));

        Assert.Equal(-2_000m, payout.NetPayoutAmount);
        Assert.Equal(0, payout.ReturnCount);
        Assert.Null(returned.CodSettlementId);
    }

    /// <summary>Regression: the same delivered parcel could be put into a second payout.</summary>
    [Fact]
    public void A_parcel_already_paid_out_cannot_be_paid_again()
    {
        var sale = Delivered("LX26000000017", 50_000m);
        CodSettlement.Create(Tenants.Default, "STL-2026-0003", Orders.Shop.Id, Orders.Shop.Name, [sale], [], PayoutMethod.Cash, null, null, null, Evening);

        Assert.Equal("shipment.cod_already_settled",
            Code(() => CodSettlement.Create(Tenants.Default, "STL-2026-0004", Orders.Shop.Id, Orders.Shop.Name, [sale], [], PayoutMethod.Cash, null, null, null, Evening.AddHours(1))));
    }

    // ── Requests ────────────────────────────────────────────────────────────────────────────────────────────

    private static ServiceRequest Ask(Shipment parcel, RequestType type, Action<CreateServiceRequest>? fill = null)
    {
        var fields = new CreateServiceRequest { ShipmentId = parcel.Id, Type = type };
        fill?.Invoke(fields);
        return ServiceRequest.Create(parcel, "RQ-2026-000001", fields, RequestSource.Customer, "customer", 24, Orders.Monday);
    }

    [Fact]
    public void A_request_keeps_only_the_fields_of_its_kind_and_is_due_after_the_company_sla()
    {
        var parcel = Orders.Book();

        var request = Ask(parcel, RequestType.ChangeAddress, r =>
        {
            r.NewGovernorate = " iq-ba ";
            r.NewAddress = " حي الجزائر ";
            r.NewPhone = "0780 765 4321";
            r.PreferredDate = Orders.Monday.AddDays(2);
        });

        Assert.Equal(("IQ-BA", "حي الجزائر"), (request.NewGovernorate, request.NewAddress));
        Assert.Null(request.NewPhone);
        Assert.Null(request.PreferredDate);
        Assert.Equal((RequestStatus.Open, Orders.Monday.AddHours(24), parcel.TrackingNumber), (request.Status, request.DueAt, request.TrackingNumber));
    }

    [Fact]
    public void Only_simple_requests_are_applied_without_a_person()
    {
        var booked = Orders.Book();
        var collected = new Trip(Orders.Book()).ReceiveAt(Orders.Baghdad).Parcel;

        Assert.True(Ask(booked, RequestType.Reschedule).CanAutoApply(booked));
        Assert.True(Ask(booked, RequestType.ChangePhone, r => r.NewPhone = "07807654321").CanAutoApply(booked));
        Assert.True(Ask(booked, RequestType.ChangeAddress, r => r.NewAddress = "x").CanAutoApply(booked));
        Assert.True(Ask(booked, RequestType.ChangeAddress, r => (r.NewAddress, r.NewGovernorate) = ("x", "IQ-BA")).CanAutoApply(booked));
        Assert.False(Ask(booked, RequestType.ChangeAddress, r => (r.NewAddress, r.NewGovernorate) = ("x", "IQ-NA")).CanAutoApply(booked));
        Assert.True(Ask(booked, RequestType.Cancel).CanAutoApply(booked));
        Assert.False(Ask(collected, RequestType.Cancel).CanAutoApply(collected));
        Assert.False(Ask(booked, RequestType.Complaint).CanAutoApply(booked));
        Assert.False(Ask(booked, RequestType.Return).CanAutoApply(booked));
    }

    [Fact]
    public void Applying_a_request_changes_the_order_the_way_it_asks()
    {
        var actor = new Actor("customer", "system");

        var moved = Orders.Book("IQ-BG");
        Ask(moved, RequestType.ChangeAddress, r => (r.NewAddress, r.NewGovernorate) = ("حي الجزائر", "IQ-BA")).ApplyTo(moved, IraqHubs.Default, actor, Orders.Monday.AddHours(1));
        Assert.Equal((IraqHubs.Basra, "حي الجزائر"), (moved.DestinationHubCode, moved.DeliveryAddress));

        var cancelled = Orders.Book();
        Ask(cancelled, RequestType.Cancel, r => r.Details = "طلبت بالخطأ").ApplyTo(cancelled, IraqHubs.Default, actor, Orders.Monday.AddHours(1));
        Assert.Equal((ShipmentStatus.Cancelled, "طلبت بالخطأ"), (cancelled.Status, cancelled.Events[^1].Notes));

        var sentBack = new Trip(Orders.Book("IQ-BG")).ReceiveAt(Orders.Baghdad).Dispatch(Orders.Baghdad).Deliver().Parcel;
        var returnRequest = Ask(sentBack, RequestType.Return);
        returnRequest.ApplyTo(sentBack, IraqHubs.Default, actor, sentBack.LastStatusAt.AddHours(1));
        Assert.Equal((ShipmentStatus.ReturnInitiated, ReturnReason.CustomerReturn), (sentBack.Status, sentBack.ReturnReason));
        Assert.Equal(returnRequest.Number, sentBack.Events[^1].Notes);

        var complained = Orders.Book();
        var history = complained.Events.Count;
        Ask(complained, RequestType.Complaint, r => r.Details = "تأخر").ApplyTo(complained, IraqHubs.Default, actor, Orders.Monday.AddHours(1));
        Assert.Equal(history, complained.Events.Count);
    }

    [Fact]
    public void A_request_is_closed_once_and_more_details_keep_the_newest_text()
    {
        var request = Ask(Orders.Book(), RequestType.Complaint, r => r.Details = "تأخر التوصيل");

        request.Start("care");
        Assert.Equal(RequestStatus.InProgress, request.Status);
        Assert.True(request.IsOpen);

        request.AddDetails(new string('ب', 1200));
        Assert.Equal(1000, request.Details!.Length);
        Assert.EndsWith("ببب", request.Details, StringComparison.Ordinal);

        request.Resolve(" اعتذرنا ", "care", Orders.Monday.AddHours(3));
        Assert.Equal((RequestStatus.Resolved, "اعتذرنا", Orders.Monday.AddHours(3)), (request.Status, request.Resolution, request.ResolvedAt));
        Assert.Equal("request.closed", Code(() => request.Reject(null, "care", Orders.Monday.AddHours(4))));
        Assert.Equal("request.closed", Code(() => request.Start("care")));
    }
}
