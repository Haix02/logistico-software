using System.Globalization;
using System.Resources;

namespace Logistico.SharedKernel;

/// <summary>Supported UI cultures. Arabic (Iraq) is the default; Gregorian calendar and Iraqi month names.</summary>
public static class AppCultures
{
    public const string Arabic = "ar-IQ";
    public const string English = "en-US";
    public const string Default = Arabic;

    public static readonly string[] Supported = [Arabic, English];

    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    // Iraqi (Syriac-derived) month names, as printed on Iraqi documents and receipts.
    private static readonly string[] IraqiMonths =
    [
        "كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران",
        "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول", "",
    ];

    /// <summary>
    /// The culture used for formatting on both server and browser. For Arabic it keeps Western digits with
    /// "." for decimals and "," for thousands (ICU's Arabic separators ٫ and ٬ look like commas and make
    /// 0٫49 kg and 308٬000 IQD ambiguous), keeps the minus sign left of the number in right-to-left text,
    /// and uses Iraqi month names consistently (browser and server ICU data differ).
    /// </summary>
    public static CultureInfo Create(string? name)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(name is not null && Supported.Contains(name) ? name : Default).Clone();
        if (culture.TwoLetterISOLanguageName != "ar")
        {
            return culture;
        }

        var numbers = culture.NumberFormat;
        numbers.NumberDecimalSeparator = numbers.CurrencyDecimalSeparator = numbers.PercentDecimalSeparator = ".";
        numbers.NumberGroupSeparator = numbers.CurrencyGroupSeparator = numbers.PercentGroupSeparator = ",";
        numbers.NegativeSign = "\u200E-";
        numbers.DigitSubstitution = DigitShapes.None;

        var dates = culture.DateTimeFormat;
        dates.Calendar = new GregorianCalendar();
        dates.MonthNames = dates.AbbreviatedMonthNames = IraqiMonths;
        dates.MonthGenitiveNames = dates.AbbreviatedMonthGenitiveNames = IraqiMonths;
        return culture;
    }
}

/// <summary>
/// Validation messages shared by every module, localised through CommonText.resx / CommonText.ar.resx.
/// Used as <c>ErrorMessageResourceType</c> on DataAnnotations attributes, so the browser and the server show the same text.
/// </summary>
public static class ValidationText
{
    private static readonly ResourceManager Resources = new("Logistico.SharedKernel.Resources.CommonText", typeof(ValidationText).Assembly);

    public static string Required => Get("Validation_Required");
    public static string MaxLength => Get("Validation_MaxLength");
    public static string Range => Get("Validation_Range");
    public static string IraqiMobile => Get("Validation_IraqiMobile");
    public static string Phone => Get("Validation_Phone");
    public static string Governorate => Get("Validation_Governorate");
    public static string LocationCode => Get("Validation_LocationCode");
    public static string SamePlace => Get("Validation_SamePlace");
    public static string Currency => Get("Validation_Currency");
    public static string Incoterm => Get("Validation_Incoterm");
    public static string InstagramHandle => Get("Validation_InstagramHandle");
    public static string Email => Get("Validation_Email");
    public static string DateWindow => Get("Validation_DateWindow");
    public static string NoFriday => Get("Validation_NoFriday");
    public static string PhoneLast4 => Get("Validation_PhoneLast4");
    public static string RoutingTag => Get("Validation_RoutingTag");
    public static string InsideIraq => Get("Validation_InsideIraq");
    public static string Role => Get("Validation_Role");
    public static string CourierCode => Get("Validation_CourierCode");
    public static string Merchant => Get("Validation_Merchant");
    public static string PasswordLength => Get("Validation_PasswordLength");
    public static string Choice => Get("Validation_Choice");
    public static string Format => Get("Validation_Format");

    private static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
