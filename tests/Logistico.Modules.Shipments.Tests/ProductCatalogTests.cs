using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public sealed class ProductCatalogTests
{
    [Theory]
    [InlineData("فستان سهرة", ProductCategory.Fashion)]
    [InlineData("عباية سوداء مع حجاب", ProductCategory.Fashion)]
    [InlineData("حذاء رياضي", ProductCategory.ShoesAndBags)]
    [InlineData("حقيبة نسائية", ProductCategory.ShoesAndBags)]
    [InlineData("عطر 100 مل", ProductCategory.Beauty)]
    [InlineData("والعطور الفرنسية", ProductCategory.Beauty)] // attached "و" and the article
    [InlineData("احمر شفاه", ProductCategory.Beauty)]
    [InlineData("سماعات بلوتوث", ProductCategory.Electronics)]
    [InlineData("كفر موبايل ×3", ProductCategory.Electronics)]
    [InlineData("ساعة ذكية", ProductCategory.Electronics)] // the phrase wins over "ساعة"
    [InlineData("ساعة يد", ProductCategory.Accessories)]
    [InlineData("ملابس أطفال", ProductCategory.KidsAndToys)] // the phrase wins over "ملابس"
    [InlineData("أواني مطبخ", ProductCategory.HomeAndKitchen)]
    [InlineData("كتب ×2", ProductCategory.Books)]
    [InlineData("مكمّلات غذائية", ProductCategory.Health)] // shadda and hamza seat normalised
    [InlineData("كيك عيد ميلاد", ProductCategory.Food)]
    [InlineData("AirPods Pro case", ProductCategory.Electronics)]
    [InlineData("Summer DRESS - red", ProductCategory.Fashion)]
    [InlineData("طرد", ProductCategory.Other)]
    [InlineData("", ProductCategory.Other)]
    [InlineData(null, ProductCategory.Other)]
    public void Classifies_arabic_and_english_item_descriptions(string? description, ProductCategory expected) =>
        Assert.Equal(expected, ProductCatalog.Classify(description));

    [Fact]
    public void Normalisation_gives_one_spelling_for_arabic_letter_variants()
    {
        Assert.Equal(ProductCatalog.Normalize("فُسْتَانٌ"), ProductCatalog.Normalize("فستان"));
        Assert.Equal("عبايه", ProductCatalog.Normalize("عبايـــة"));
        Assert.Equal("ادوات", ProductCatalog.Normalize("أدوات"));
        Assert.Equal("مستشفي", ProductCatalog.Normalize("مستشفى"));
        Assert.Equal("a b", ProductCatalog.Normalize("  A, ×  b!! "));
    }

    [Fact]
    public void Terms_drop_numbers_units_and_filler_but_keep_the_written_spelling()
    {
        var terms = ProductCatalog.Terms("فستان سهرة الأحمر مع حزام ×2 100 مل");

        Assert.Equal(["فستان", "سهره", "حزام"], terms.Select(t => t.Key)); // colour, quantity and unit dropped
        Assert.Equal("سهرة", terms[1].Display); // shown as written, counted as normalised

        var perfume = Assert.Single(ProductCatalog.Terms("والعطور ٣"));
        Assert.Equal(("عطور", "عطور"), perfume); // without the article and the attached "و"
        Assert.Equal(ProductCategory.Beauty, ProductCatalog.CategoryOfTerm(perfume.Key));
    }

    [Fact]
    public void Terms_are_counted_once_per_description()
    {
        Assert.Single(ProductCatalog.Terms("عطر عطر العطر"));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(10_000, 1)]
    [InlineData(25_000, 2)]
    [InlineData(99_000, 3)]
    [InlineData(150_000, 4)]
    [InlineData(200_000, 5)]
    [InlineData(2_500_000, 5)]
    public void Cash_on_delivery_amounts_fall_into_basket_bands(int? amount, int band) =>
        Assert.Equal(band, BasketBands.Of(amount));

    [Fact]
    public void Basket_band_ranges_cover_every_amount_without_gaps()
    {
        Assert.Equal((null, null), BasketBands.Range(BasketBands.NoCod));
        Assert.Equal((0m, 25_000m), BasketBands.Range(1));
        Assert.Equal((25_000m, 50_000m), BasketBands.Range(2));
        Assert.Equal((100_000m, 200_000m), BasketBands.Range(4));
        Assert.Equal((200_000m, null), BasketBands.Range(5));
        Assert.Equal(6, BasketBands.Count);
    }
}
