using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Iraq;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Parses unstructured Iraqi Instagram DM / WhatsApp / Excel order messages (Arabic &amp; English)
/// into structured <see cref="CreateShipmentRequest"/> drafts with governorate, district, landmark, phone, and IQD COD detection.
/// </summary>
public static partial class SmartOrderParser
{
    private static readonly (string Keyword, string GovernorateCode, string? DistrictName)[] LocationKeywords =
    [
        // Baghdad neighborhoods
        ("المنصور", "IQ-BG", "المنصور"),
        ("الكرادة", "IQ-BG", "الكرادة"),
        ("كرادة", "IQ-BG", "الكرادة"),
        ("زيونة", "IQ-BG", "زيونة"),
        ("الأعظمية", "IQ-BG", "الأعظمية"),
        ("اعظمية", "IQ-BG", "الأعظمية"),
        ("الكاظمية", "IQ-BG", "الكاظمية"),
        ("حي الجامعة", "IQ-BG", "حي الجامعة"),
        ("الدورة", "IQ-BG", "الدورة"),
        ("اليرموك", "IQ-BG", "اليرموك"),
        ("الشعب", "IQ-BG", "الشعب"),
        ("البلديات", "IQ-BG", "البلديات"),
        ("العامرية", "IQ-BG", "العامرية"),
        ("الغزالية", "IQ-BG", "الغزالية"),
        ("الحارثية", "IQ-BG", "الحارثية"),
        ("الجادرية", "IQ-BG", "الجادرية"),
        ("بغداد", "IQ-BG", null),
        ("baghdad", "IQ-BG", null),

        // Basra
        ("العشار", "IQ-BA", "العشار"),
        ("الجزائر", "IQ-BA", "الجزائر"),
        ("الزبير", "IQ-BA", "الزبير"),
        ("البراضعية", "IQ-BA", "البراضعية"),
        ("الطويسة", "IQ-BA", "الطويسة"),
        ("البصرة", "IQ-BA", null),
        ("بصرة", "IQ-BA", null),
        ("basra", "IQ-BA", null),

        // Erbil
        ("عنكاوا", "IQ-AR", "Ankawa"),
        ("ankawa", "IQ-AR", "Ankawa"),
        ("dream city", "IQ-AR", "Dream City"),
        ("أربيل", "IQ-AR", null),
        ("اربيل", "IQ-AR", null),
        ("erbil", "IQ-AR", null),
        ("hawler", "IQ-AR", null),

        // Najaf
        ("الكوفة", "IQ-NA", "الكوفة"),
        ("حي الأمير", "IQ-NA", "حي الأمير"),
        ("النجف", "IQ-NA", null),
        ("نجف", "IQ-NA", null),
        ("najaf", "IQ-NA", null),

        // Karbala
        ("حي الحسين", "IQ-KA", "حي الحسين"),
        ("كربلاء", "IQ-KA", null),
        ("karbala", "IQ-KA", null),

        // Nineveh / Mosul
        ("الزهور", "IQ-NI", "الزهور"),
        ("المجموعة الثقافية", "IQ-NI", "المجموعة الثقافية"),
        ("الموصل", "IQ-NI", "الموصل"),
        ("موصل", "IQ-NI", "الموصل"),
        ("نينوى", "IQ-NI", null),
        ("mosul", "IQ-NI", "Mosul"),

        // Sulaymaniyah
        ("بختياري", "IQ-SU", "Bakhtiari"),
        ("السليمانية", "IQ-SU", null),
        ("سليمانية", "IQ-SU", null),
        ("sulaymaniyah", "IQ-SU", null),

        // Babylon / Hilla
        ("الحلة", "IQ-BB", "الحلة"),
        ("بابل", "IQ-BB", null),
        ("babil", "IQ-BB", null),
        ("hilla", "IQ-BB", "Hilla"),

        // Dhi Qar / Nasiriyah
        ("الناصرية", "IQ-DQ", "الناصرية"),
        ("الشطرة", "IQ-DQ", "الشطرة"),
        ("ذي قار", "IQ-DQ", null),
        ("nasiriyah", "IQ-DQ", "Nasiriyah"),

        // Kirkuk
        ("رحيم آوه", "IQ-KI", "رحيم آوه"),
        ("كركوك", "IQ-KI", null),
        ("kirkuk", "IQ-KI", null),

        // Other Iraqi governorates
        ("الرمادي", "IQ-AN", "الرمادي"),
        ("الفلوجة", "IQ-AN", "الفلوجة"),
        ("الأنبار", "IQ-AN", null),
        ("الانبار", "IQ-AN", null),
        ("بعقوبة", "IQ-DI", "بعقوبة"),
        ("ديالى", "IQ-DI", null),
        ("العمارة", "IQ-MA", "العمارة"),
        ("ميسان", "IQ-MA", null),
        ("السماوة", "IQ-MU", "السماوة"),
        ("المثنى", "IQ-MU", null),
        ("الديوانية", "IQ-QA", "الديوانية"),
        ("القادسية", "IQ-QA", null),
        ("تكريت", "IQ-SD", "تكريت"),
        ("سامراء", "IQ-SD", "سامراء"),
        ("صلاح الدين", "IQ-SD", null),
        ("الكوت", "IQ-WA", "الكوت"),
        ("واسط", "IQ-WA", null),
        ("دهوك", "IQ-DA", "دهوك"),
        ("زاخو", "IQ-DA", "زاخو"),
        ("duhok", "IQ-DA", "Duhok"),
    ];

    private static readonly string[] LandmarkPrefixes =
    [
        "قرب ", "مجاور ", "مقابل ", "خلف ", "وراء ", "بجانب ", "يم ", "صف ", "نقطة دالة", "near ", "opposite ", "behind "
    ];

    public static string NormalizeDigits(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "";
        }

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '٠' and <= '٩')
            {
                sb.Append((char)('0' + (ch - '٠')));
            }
            else if (ch is >= '۰' and <= '۹')
            {
                sb.Append((char)('0' + (ch - '۰')));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static ParsedOrderDraft ParseSingle(string? rawText, string originGovernorate = "IQ-BG")
    {
        var normalized = NormalizeDigits(rawText).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedOrderDraft();
        }

        // 1. Extract Iraqi mobile phone
        string? phone = null;
        var phoneMatch = IraqiPhoneMatchRegex().Match(normalized);
        var textWithoutPhone = normalized;
        if (phoneMatch.Success)
        {
            phone = IraqPhone.Display(IraqPhone.Normalize(phoneMatch.Value));
            textWithoutPhone = normalized.Remove(phoneMatch.Index, phoneMatch.Length);
        }

        // 2. Extract COD amount in IQD (e.g. "45 الف", "45,000", "45000", "35k")
        decimal? codAmount = null;
        var thousandWordMatch = ThousandWordAmountRegex().Match(textWithoutPhone);
        if (thousandWordMatch.Success && decimal.TryParse(thousandWordMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var thousands))
        {
            codAmount = thousands * 1_000m;
            textWithoutPhone = textWithoutPhone.Remove(thousandWordMatch.Index, thousandWordMatch.Length);
        }
        else
        {
            var numberMatch = ExplicitIqdAmountRegex().Match(textWithoutPhone);
            if (numberMatch.Success)
            {
                var cleanDigits = numberMatch.Groups[1].Value.Replace(",", "").Replace(".", "");
                if (decimal.TryParse(cleanDigits, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAmount) && parsedAmount >= 1_000m)
                {
                    codAmount = parsedAmount;
                    textWithoutPhone = textWithoutPhone.Remove(numberMatch.Index, numberMatch.Length);
                }
            }
        }

        // 3. Detect Governorate and District/Area
        var governorate = "IQ-BG";
        string? detectedDistrict = null;
        foreach (var (keyword, govCode, district) in LocationKeywords)
        {
            if (textWithoutPhone.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                governorate = govCode;
                detectedDistrict ??= district;
                break;
            }
        }

        // 4. Split remaining text into segments by '-', '|', '/', ',', tab, or newline
        var segments = SegmentSplitterRegex()
            .Split(textWithoutPhone)
            .Select(s => s.Trim(' ', '-', '—', ':', '،', ',', '.', '/'))
            .Where(s => s.Length > 1)
            .ToList();

        string? customerName = null;
        string? landmark = null;
        var addressParts = new List<string>();
        var itemParts = new List<string>();

        foreach (var rawSeg in segments)
        {
            var isExplicitLandmark = LandmarkLabelRegex().IsMatch(rawSeg) ||
                LandmarkPrefixes.Any(p => rawSeg.Contains(p, StringComparison.OrdinalIgnoreCase));
            var seg = CleanSegment(rawSeg);
            if (seg.Length <= 1)
            {
                continue;
            }

            if (isExplicitLandmark)
            {
                landmark ??= seg;
                continue;
            }

            if (LocationKeywords.Any(k => seg.Contains(k.Keyword, StringComparison.OrdinalIgnoreCase)) ||
                seg.Contains("شارع", StringComparison.OrdinalIgnoreCase) ||
                seg.Contains("محلة", StringComparison.OrdinalIgnoreCase) ||
                seg.Contains("زقاق", StringComparison.OrdinalIgnoreCase) ||
                seg.Contains("حي ", StringComparison.OrdinalIgnoreCase))
            {
                addressParts.Add(seg);
                continue;
            }

            if (customerName is null && seg.Length <= 45 && !seg.Any(char.IsDigit))
            {
                customerName = seg;
                continue;
            }

            itemParts.Add(seg);
        }

        var address = addressParts.Count > 0
            ? string.Join(" — ", addressParts)
            : detectedDistrict ?? IraqGovernorates.NameOrCode(governorate);

        var items = itemParts.Count > 0 ? string.Join(" · ", itemParts) : null;
        var deliveryFee = IraqTariffCalculator.CalculateDeliveryFeeIqd(originGovernorate, governorate, 1m);

        return new ParsedOrderDraft
        {
            ConsigneeName = customerName ?? (segments.Select(CleanSegment).FirstOrDefault(s => s.Length > 1) ?? "زبون"),
            ConsigneePhone = phone ?? "",
            DestinationCode = governorate,
            DestinationCity = detectedDistrict,
            DeliveryAddress = address,
            Landmark = landmark,
            ItemsDescription = items,
            CodAmount = codAmount ?? 25_000m,
            DeliveryFee = deliveryFee,
        };
    }

    public static IReadOnlyList<ParsedOrderDraft> ParseBatch(string? multiLineText, string originGovernorate = "IQ-BG")
    {
        if (string.IsNullOrWhiteSpace(multiLineText))
        {
            return [];
        }

        var normalized = NormalizeDigits(multiLineText);
        var blocks = BatchBlockSplitterRegex()
            .Split(normalized)
            .Select(b => b.Trim())
            .Where(b => b.Length >= 6)
            .ToList();

        var results = new List<ParsedOrderDraft>();
        foreach (var block in blocks)
        {
            var lines = block
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Length >= 4)
                .ToList();

            var linesWithPhones = lines.Count(l => IraqiPhoneMatchRegex().IsMatch(l));
            if (linesWithPhones > 1)
            {
                foreach (var line in lines.Where(l => l.Length >= 6))
                {
                    results.Add(ParseSingle(line, originGovernorate));
                }
            }
            else
            {
                results.Add(ParseSingle(block, originGovernorate));
            }
        }

        return results;
    }

    private static string CleanSegment(string value) =>
        LabelPrefixRegex().Replace(value, "").Trim(' ', '-', '—', ':', '،', ',', '.', '/');

    [GeneratedRegex(@"(?:\+?964|00964|0)?7[3-9][\d\s\-]{8,11}")]
    private static partial Regex IraqiPhoneMatchRegex();

    [GeneratedRegex(@"\b(\d{1,4}(?:\.\d+)?)\s*(?:الف|ألف|آلاف|الاف|k|K)\b")]
    private static partial Regex ThousandWordAmountRegex();

    [GeneratedRegex(@"\b(\d{1,3}(?:[,\.]\d{3})+|\d{4,8})\s*(?:د\.?ع|دينار|IQD|iqd)?\b")]
    private static partial Regex ExplicitIqdAmountRegex();

    [GeneratedRegex(@"[\r\n\t\|\-—/،,;]+")]
    private static partial Regex SegmentSplitterRegex();

    [GeneratedRegex(@"(?:\r?\n\s*\r?\n|\r?\n\s*---+\s*\r?\n)")]
    private static partial Regex BatchBlockSplitterRegex();

    [GeneratedRegex(@"^(?:أقرب نقطة دالة|اقرب نقطة دالة|نقطة دالة|landmark)\s*[:\-]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LandmarkLabelRegex();

    [GeneratedRegex(@"^(?:الاسم|اسم الزبون|العنوان|الهاتف|الموبايل|الرقم|أقرب نقطة دالة|اقرب نقطة دالة|نقطة دالة|الطلب|المواد|المبلغ|السعر|كلفة|name|phone|address|landmark|cod|price|items?)\s*[:\-]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPrefixRegex();
}

public sealed class ParsedOrderDraft
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string ConsigneeName { get; set; } = "";

    [System.ComponentModel.DataAnnotations.StringLength(24, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string ConsigneePhone { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string DestinationCode { get; set; } = "IQ-BG";

    [System.ComponentModel.DataAnnotations.StringLength(80, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? DestinationCity { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [System.ComponentModel.DataAnnotations.StringLength(300, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string DeliveryAddress { get; set; } = "";

    [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? Landmark { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(500, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string? ItemsDescription { get; set; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal CodAmount { get; set; } = 25_000m;

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Range))]
    public decimal DeliveryFee { get; set; } = 5_000m;
    public bool IsValid => !string.IsNullOrWhiteSpace(ConsigneeName) && IraqPhone.IsValid(ConsigneePhone) && IraqGovernorates.IsKnown(DestinationCode);
}
