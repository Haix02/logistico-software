using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class SmartOrderAndTariffTests
{
    [Theory]
    [InlineData("IQ-BG", "IQ-BG", 1, 5_000)]
    [InlineData("IQ-BG", "IQ-NA", 2, 6_000)]
    [InlineData("IQ-BG", "IQ-BA", 3, 7_000)]
    [InlineData("IQ-BG", "IQ-AR", 4, 8_000)]
    [InlineData("IQ-BG", "IQ-BA", 7.2, 10_000)] // 7,000 base + ceil(2.2)=3 * 1,000 overweight
    public void Tariff_calculator_computes_zone_and_overweight_fees_in_iqd(
        string origin,
        string destination,
        decimal weightKg,
        decimal expectedFeeIqd)
    {
        var fee = IraqTariffCalculator.CalculateDeliveryFeeIqd(origin, destination, weightKg);
        Assert.Equal(expectedFeeIqd, fee);
    }

    [Fact]
    public void Smart_parser_extracts_arabic_dm_with_eastern_digits_landmark_and_thousands_word()
    {
        const string dm = "سارة محمد - ٠٧٧٠١٢٣٤٥٦٧ - بغداد المنصور شارع 14 رمضان - قرب مطعم الساعة - فستان أسود وحقيبة - ٤٥ الف";

        var draft = SmartOrderParser.ParseSingle(dm, "IQ-BG");

        Assert.True(draft.IsValid);
        Assert.Equal("سارة محمد", draft.ConsigneeName);
        Assert.Equal("0770 123 4567", draft.ConsigneePhone);
        Assert.Equal("IQ-BG", draft.DestinationCode);
        Assert.Equal("المنصور", draft.DestinationCity);
        Assert.Contains("المنصور", draft.DeliveryAddress, StringComparison.Ordinal);
        Assert.Equal("قرب مطعم الساعة", draft.Landmark);
        Assert.Equal("فستان أسود وحقيبة", draft.ItemsDescription);
        Assert.Equal(45_000m, draft.CodAmount);
        Assert.Equal(5_000m, draft.DeliveryFee);
    }

    [Fact]
    public void Smart_parser_detects_basra_and_erbil_from_neighborhood_names_and_calculates_zone_tariffs()
    {
        var basra = SmartOrderParser.ParseSingle(
            "علي البصري | 07809876543 | العشار شارع الوطن | مقابل فندق شط العرب | عطر رجالي | 65,000",
            "IQ-BG");

        Assert.True(basra.IsValid);
        Assert.Equal("IQ-BA", basra.DestinationCode);
        Assert.Equal("العشار", basra.DestinationCity);
        Assert.Equal(65_000m, basra.CodAmount);
        Assert.Equal(7_000m, basra.DeliveryFee);

        var erbil = SmartOrderParser.ParseSingle(
            "دلشاد كريم - +9647504567890 - عنكاوا شارع المنتزه - نقطة دالة: كافيه لافندر - ساعة ذكية - 85k",
            "IQ-BG");

        Assert.True(erbil.IsValid);
        Assert.Equal("IQ-AR", erbil.DestinationCode);
        Assert.Equal("Ankawa", erbil.DestinationCity);
        Assert.Equal("كافيه لافندر", erbil.Landmark);
        Assert.Equal(85_000m, erbil.CodAmount);
        Assert.Equal(8_000m, erbil.DeliveryFee);
    }

    [Fact]
    public void Smart_parser_parses_multi_line_batch_of_orders()
    {
        const string batch = """
            سارة محمد - 07701234567 - بغداد المنصور - قرب مطعم الساعة - فستان - 45 الف
            علي البصري - 07809876543 - البصرة العشار - مقابل فندق شط العرب - عطر - 65,000
            دلشاد كريم - 07504567890 - اربيل عنكاوا - مجاور كافيه لافندر - ساعة - 85 الف
            """;

        var drafts = SmartOrderParser.ParseBatch(batch, "IQ-BG");

        Assert.Equal(3, drafts.Count);
        Assert.All(drafts, d => Assert.True(d.IsValid));
        Assert.Equal(["IQ-BG", "IQ-BA", "IQ-AR"], drafts.Select(d => d.DestinationCode));
        Assert.Equal([5_000m, 7_000m, 8_000m], drafts.Select(d => d.DeliveryFee));
    }

    [Fact]
    public void Code128_barcode_generates_deterministic_svg_and_modules()
    {
        var bars = Code128Barcode.EncodeModules("LX2600001842");
        var svg = Code128Barcode.ToSvg("LX2600001842", 56, showLabel: true);

        Assert.StartsWith("11010010000", bars, StringComparison.Ordinal); // Code 128 Subset B start symbol (104)
        Assert.EndsWith("1100011101011", bars, StringComparison.Ordinal); // Code 128 Stop pattern
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("LX2600001842", svg, StringComparison.Ordinal);
    }
}
