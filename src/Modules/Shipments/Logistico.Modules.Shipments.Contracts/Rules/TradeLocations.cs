using System.Globalization;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>Readable names for location codes: Iraqi governorates (IQ-BG), Iraqi ports and airports (IQUQR) and common origin ports (CNSHA).</summary>
public static class TradeLocations
{
    private static readonly Dictionary<string, (string En, string Ar)> Ports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IQUQR"] = ("Umm Qasr Port", "ميناء أم قصر"),
        ["IQBGW"] = ("Baghdad Airport", "مطار بغداد"),
        ["IQBSR"] = ("Basra Airport", "مطار البصرة"),
        ["IQEBL"] = ("Erbil Airport", "مطار أربيل"),
        ["IQISU"] = ("Sulaymaniyah Airport", "مطار السليمانية"),
        ["IQNJF"] = ("Najaf Airport", "مطار النجف"),
        ["CNSHA"] = ("Shanghai", "شنغهاي"),
        ["CNNGB"] = ("Ningbo", "نينغبو"),
        ["CNSZX"] = ("Shenzhen", "شنتشن"),
        ["CNCAN"] = ("Guangzhou", "غوانزو"),
        ["TRIST"] = ("Istanbul", "إسطنبول"),
        ["TRMER"] = ("Mersin", "مرسين"),
        ["AEJEA"] = ("Jebel Ali", "جبل علي"),
        ["AEDXB"] = ("Dubai", "دبي"),
        ["DEHAM"] = ("Hamburg", "هامبورغ"),
        ["INNSA"] = ("Nhava Sheva", "نافا شيفا"),
    };

    /// <summary>Name in the current UI language, or the code itself when unknown.</summary>
    public static string Name(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "";
        }

        if (IraqGovernorates.Find(code) is { } governorate)
        {
            return governorate.Name;
        }

        return Ports.TryGetValue(code, out var port)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? port.Ar : port.En
            : code;
    }
}
