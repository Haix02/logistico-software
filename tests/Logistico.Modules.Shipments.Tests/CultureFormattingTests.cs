using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

public class CultureFormattingTests
{
    [Fact]
    public void Arabic_numbers_use_unambiguous_separators()
    {
        var arabic = AppCultures.Create(AppCultures.Arabic);

        Assert.Equal("1,234,567.89", 1_234_567.89m.ToString("N2", arabic));
        Assert.Equal("0.49", 0.49m.ToString("0.00", arabic));
    }

    [Fact]
    public void Arabic_negative_amounts_keep_the_minus_on_the_left_in_right_to_left_text()
    {
        Assert.Equal("‎-5,000", (-5000m).ToString("N0", AppCultures.Create(AppCultures.Arabic)));
    }

    [Fact]
    public void Arabic_dates_use_the_gregorian_calendar_with_iraqi_month_names()
    {
        var arabic = AppCultures.Create(AppCultures.Arabic);

        Assert.Equal("19 تشرين الأول 2026", new DateTime(2026, 10, 19).ToString("dd MMMM yyyy", arabic));
        Assert.IsType<System.Globalization.GregorianCalendar>(arabic.DateTimeFormat.Calendar);
    }

    [Fact]
    public void Unknown_cultures_fall_back_to_arabic()
    {
        Assert.Equal(AppCultures.Arabic, AppCultures.Create("fr-FR").Name);
        Assert.Equal("1,234.5", 1234.5m.ToString("N1", AppCultures.Create(AppCultures.English)));
    }
}
