using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Tests;

public class IraqReferenceTests
{
    [Theory]
    [InlineData("07701234567", "+9647701234567")]
    [InlineData("0770 123 4567", "+9647701234567")]
    [InlineData("+964 770 123 4567", "+9647701234567")]
    [InlineData("009647801234567", "+9647801234567")]
    [InlineData("7501234567", "+9647501234567")]
    [InlineData("0612345678", null)]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void Normalises_iraqi_mobile_numbers(string input, string? expected)
    {
        Assert.Equal(expected, IraqPhone.Normalize(input));
    }

    [Fact]
    public void Displays_numbers_in_local_format()
    {
        Assert.Equal("0770 123 4567", IraqPhone.Display("+9647701234567"));
        Assert.Equal("9647701234567", IraqPhone.WhatsAppDigits("+9647701234567"));
    }

    [Fact]
    public void Has_all_nineteen_governorates_with_arabic_names()
    {
        Assert.Equal(19, IraqGovernorates.All.Count);
        Assert.Equal(19, IraqGovernorates.All.Select(g => g.Code).Distinct().Count());
        Assert.All(IraqGovernorates.All, g => Assert.False(string.IsNullOrWhiteSpace(g.NameAr)));
        Assert.True(IraqGovernorates.IsKnown("iq-bg"));
        Assert.Equal("CNSHA", IraqGovernorates.NameOrCode("CNSHA"));
    }

    [Fact]
    public void Governorate_names_follow_the_ui_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-IQ");
            Assert.Equal("البصرة", IraqGovernorates.NameOrCode("IQ-BA"));
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal("Basra", IraqGovernorates.NameOrCode("IQ-BA"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Validation_messages_and_status_names_are_translated_to_arabic()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-IQ");
            var request = new CreateShipmentRequest { ServiceType = ServiceType.Domestic, ConsigneeName = "", DestinationCode = "IQ-BA", ConsigneePhone = "123" };
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

            Assert.Contains(results, r => r.ErrorMessage == "هذا الحقل مطلوب.");
            Assert.Equal("خرجت مع المندوب", ShipmentTexts.Of(ShipmentStatus.OutForDelivery));
            Assert.Equal("لا يمكن نقل شحنة حالتها «تم التسليم» إلى «في المخزن».",
                ShipmentTexts.Problem("shipment.invalid_transition", ShipmentTexts.Of(ShipmentStatus.Delivered), ShipmentTexts.Of(ShipmentStatus.AtWarehouse)));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Domestic_orders_need_an_iraqi_mobile_and_address()
    {
        var request = new CreateShipmentRequest
        {
            ServiceType = ServiceType.Domestic,
            ConsigneeName = "زينب",
            DestinationCode = "IQ-BA",
            ConsigneePhone = "+44 20 7946 0000",
        };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateShipmentRequest.ConsigneePhone)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateShipmentRequest.DeliveryAddress)));
    }

    [Fact]
    public void International_booking_rejects_the_same_place_twice()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var request = new CreateShipmentRequest
            {
                ServiceType = ServiceType.International,
                Mode = TransportMode.Air,
                ShipperName = "Shenzhen Shop",
                ConsigneeName = "Baghdad Buyer",
                OriginCode = " cnszx ",
                DestinationCode = "CNSZX",
            };
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

            Assert.Equal("cnszx", request.OriginCode);
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateShipmentRequest.DestinationCode)) && r.ErrorMessage == ValidationText.SamePlace);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Query_strings_round_trip_filters()
    {
        var query = new ShipmentQuery
        {
            Q = "زينب",
            Status = [ShipmentStatus.AtWarehouse, ShipmentStatus.DeliveryFailed],
            Service = ServiceType.Domestic,
            InWarehouse = true,
            Page = 2,
        };

        var qs = query.ToQueryString();
        Assert.Contains("status=AtWarehouse&status=DeliveryFailed", qs, StringComparison.Ordinal);
        Assert.Contains("q=%D8%B2%D9%8A%D9%86%D8%A8", qs, StringComparison.Ordinal);
        Assert.Contains("inWarehouse=true", qs, StringComparison.Ordinal);
        Assert.Equal(25, query.EffectivePageSize);
    }
}
