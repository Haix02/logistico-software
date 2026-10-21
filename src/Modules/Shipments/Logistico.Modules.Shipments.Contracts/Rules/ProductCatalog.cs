using System.Globalization;
using System.Text;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Recognises what a parcel contains from the free-text item description sellers type ("فستان سهرة", "AirPods case",
/// "عطور ×2"…), in Arabic or English, and pulls out its meaningful words. Arabic is normalised first (diacritics and
/// tatweel removed, أ/إ/آ → ا, ى → ي, ة → ه, the article and attached و/ب/ل/ف dropped) so spelling variants count once.
/// </summary>
public static class ProductCatalog
{
    // Phrases are checked before single words, so "ساعة ذكية" is electronics while "ساعة" alone is an accessory.
    private static readonly (string Phrase, ProductCategory Category)[] Phrases = NormalizePhrases(
    [
        ("ساعه ذكيه", ProductCategory.Electronics), ("smart watch", ProductCategory.Electronics), ("smartwatch", ProductCategory.Electronics),
        ("ملابس اطفال", ProductCategory.KidsAndToys), ("احذيه اطفال", ProductCategory.KidsAndToys), ("baby clothes", ProductCategory.KidsAndToys),
        ("كفر موبايل", ProductCategory.Electronics), ("phone case", ProductCategory.Electronics),
        ("مكمل غذائي", ProductCategory.Health), ("مكملات غذائيه", ProductCategory.Health),
        ("ادوات مطبخ", ProductCategory.HomeAndKitchen),
        ("احمر شفاه", ProductCategory.Beauty), ("احمر خدود", ProductCategory.Beauty),
    ]);

    private static readonly Dictionary<string, ProductCategory> Words = Build(
        (ProductCategory.Fashion,
        [
            "فستان", "فساتين", "عبايه", "عبايات", "عباءه", "قميص", "قمصان", "بنطرون", "بنطلون", "تيشيرت", "تيشرت", "جاكيت", "بلوزه", "تنوره",
            "بدله", "دشداشه", "حجاب", "شال", "ملابس", "لبس", "هودي", "بيجامه", "قماش", "كنزه",
            "dress", "abaya", "shirt", "tshirt", "t-shirt", "jeans", "pants", "jacket", "hoodie", "blouse", "skirt", "hijab", "clothes", "suit",
        ]),
        (ProductCategory.ShoesAndBags,
        [
            "حذاء", "احذيه", "جزمه", "نعال", "شحاطه", "سنيكرز", "بوت", "حقيبه", "حقائب", "شنطه", "جنطه", "محفظه",
            "shoes", "shoe", "sneakers", "boots", "sandals", "bag", "bags", "handbag", "wallet", "backpack",
        ]),
        (ProductCategory.Beauty,
        [
            "عطر", "عطور", "مكياج", "ميكب", "كريم", "كريمات", "مستحضرات", "شامبو", "بلسم", "سيروم", "ماسكرا", "مناكير", "بخور", "عود", "تجميل", "صابون", "لوشن",
            "perfume", "parfum", "fragrance", "makeup", "cosmetics", "cream", "serum", "shampoo", "lipstick", "mascara", "skincare", "lotion", "oud",
        ]),
        (ProductCategory.Electronics,
        [
            "موبايل", "هاتف", "تلفون", "ايفون", "سامسونج", "سماعه", "سماعات", "شاحن", "شواحن", "كيبل", "كابل", "بلوتوث", "لابتوب", "حاسبه", "تابلت", "ايباد",
            "باوربانك", "كفر", "كفرات", "سبيكر", "ماوس", "كيبورد", "راوتر", "كاميرا", "بلايستيشن", "شاشه",
            "phone", "iphone", "samsung", "airpods", "earbuds", "headphones", "headset", "charger", "cable", "bluetooth", "laptop", "tablet", "ipad",
            "powerbank", "speaker", "mouse", "keyboard", "router", "camera", "playstation", "case",
        ]),
        (ProductCategory.Accessories,
        [
            "ساعه", "ساعات", "اكسسوارات", "اكسسوار", "خاتم", "قلاده", "سوار", "اسواره", "حلق", "تراجي", "ذهب", "فضه", "نظاره", "نظارات", "مسبحه",
            "watch", "watches", "jewelry", "jewellery", "ring", "necklace", "bracelet", "earrings", "sunglasses", "glasses",
        ]),
        (ProductCategory.KidsAndToys,
        [
            "اطفال", "طفل", "رضيع", "العاب", "لعبه", "لعب", "دميه", "رضاعه", "حفاظات", "عربانه",
            "kids", "kid", "baby", "toy", "toys", "doll", "lego", "diapers", "stroller",
        ]),
        (ProductCategory.HomeAndKitchen,
        [
            "مطبخ", "اواني", "قدور", "قدر", "طاوه", "صحون", "مفرش", "مفارش", "شرشف", "بطانيه", "ستائر", "ستاره", "ديكور", "سجاده", "سجاد", "منزلي", "منزل",
            "مخده", "وساده", "فرشه", "ترمس", "خلاط",
            "kitchen", "pot", "pots", "pan", "plates", "bedding", "blanket", "curtain", "curtains", "decor", "rug", "carpet", "pillow", "blender", "home",
        ]),
        (ProductCategory.Books,
        [
            "كتاب", "كتب", "روايه", "روايات", "قرطاسيه", "دفتر", "دفاتر", "قرآن", "مصحف", "اقلام", "قلم",
            "book", "books", "novel", "stationery", "notebook", "pens",
        ]),
        (ProductCategory.Food,
        [
            "حلويات", "حلو", "كيك", "كعك", "معمول", "تمر", "تمور", "قهوه", "شاي", "بهارات", "عسل", "اكل", "طعام", "شوكولاته", "مكسرات", "معجنات",
            "cake", "sweets", "chocolate", "coffee", "tea", "honey", "dates", "food", "spices", "nuts",
        ]),
        (ProductCategory.Health,
        [
            "فيتامين", "فيتامينات", "مكمل", "مكملات", "دواء", "ادويه", "طبي", "طبيه", "بروتين", "كمامات",
            "vitamin", "vitamins", "supplement", "supplements", "medicine", "medical", "protein",
        ]));

    // Words that describe quantity, packaging or colour rather than the product.
    private static readonly HashSet<string> StopWords = new(
        [
            "مع", "من", "في", "على", "الى", "عدد", "قطعه", "قطع", "حبه", "حبات", "لون", "الوان", "حجم", "قياس", "نوع", "جديد", "اصلي", "مل", "غرام", "كغم",
            "سيت", "طقم", "علبه", "كارتون", "صغير", "كبير", "رجالي", "نسائي", "ولادي", "بناتي", "ابيض", "اسود", "احمر", "ازرق", "هديه", "عرض",
            "and", "with", "for", "the", "set", "pcs", "pc", "piece", "pieces", "size", "color", "colour", "new", "original", "box", "pack", "ml", "gift",
            "men", "women", "black", "white",
        ],
        StringComparer.Ordinal);

    /// <summary>The category of an item description; <see cref="ProductCategory.Other"/> when nothing is recognised.</summary>
    public static ProductCategory Classify(string? description)
    {
        var normalized = Normalize(description);
        if (normalized.Length == 0)
        {
            return ProductCategory.Other;
        }

        var padded = " " + normalized + " ";
        foreach (var (phrase, category) in Phrases)
        {
            if (padded.Contains(" " + phrase + " ", StringComparison.Ordinal))
            {
                return category;
            }
        }

        foreach (var token in Tokens(normalized))
        {
            if (Lookup(token) is { } category)
            {
                return category;
            }
        }

        return ProductCategory.Other;
    }

    /// <summary>
    /// The meaningful words of a description, each once, without numbers, units or filler words: <c>Key</c> is the
    /// normalised form used for counting ("سهره"), <c>Display</c> the word as written without its article ("سهرة").
    /// </summary>
    public static IReadOnlyList<(string Key, string Display)> Terms(string? description)
    {
        var terms = new List<(string Key, string Display)>();
        foreach (var written in Tokens(Simplify(description)))
        {
            // Letter mapping is one-to-one, so the prefix removed from the key has the same length in the written word.
            var key = StripPrefix(MapLetters(written));
            if (key.Length >= 3 && !StopWords.Contains(key) && !key.Any(char.IsDigit) && terms.TrueForAll(t => t.Key != key))
            {
                terms.Add((key, written[(written.Length - key.Length)..]));
            }
        }

        return terms;
    }

    /// <summary>The category a single (normalised) word belongs to, if any.</summary>
    public static ProductCategory CategoryOfTerm(string term) => Lookup(term) ?? ProductCategory.Other;

    /// <summary>
    /// Lower-case Latin; Arabic without diacritics or tatweel and with one spelling for alef, ya, ta marbuta and hamza
    /// seats. Everything that is not a letter or digit becomes a single space.
    /// </summary>
    public static string Normalize(string? text) => MapLetters(Simplify(text));

    /// <summary>Lower case, no harakat or tatweel, words separated by single spaces; letters are kept as written.</summary>
    private static string Simplify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (c == 'ـ' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // tatweel and harakat carry no meaning for matching
            }

            if (char.IsLetterOrDigit(c) || c == '-')
            {
                sb.Append(c);
                space = false;
            }
            else if (!space && sb.Length > 0)
            {
                sb.Append(' ');
                space = true;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>One spelling per letter (one-to-one, so lengths never change).</summary>
    private static string MapLetters(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = source[i] switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' or 'ئ' or 'ی' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'گ' or 'ک' => 'ك', // Iraqi/Persian letters typed for the same sound
                var c => c,
            };
        }
    });

    private static IEnumerable<string> Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('-')).Where(t => t.Length > 0);

    private static ProductCategory? Lookup(string token)
    {
        if (Words.TryGetValue(token, out var direct))
        {
            return direct;
        }

        var stripped = StripPrefix(token);
        return stripped != token && Words.TryGetValue(stripped, out var category) ? category : null;
    }

    /// <summary>Drops the Arabic article and attached conjunctions/prepositions: "والعطور" → "عطور".</summary>
    private static string StripPrefix(string token)
    {
        foreach (var prefix in (ReadOnlySpan<string>)["وال", "بال", "فال", "كال", "لل", "ال"])
        {
            if (token.Length - prefix.Length >= 3 && token.StartsWith(prefix, StringComparison.Ordinal))
            {
                return token[prefix.Length..];
            }
        }

        return token.Length > 4 && token[0] is 'و' or 'ب' && Words.ContainsKey(token[1..]) ? token[1..] : token;
    }

    private static (string Phrase, ProductCategory Category)[] NormalizePhrases((string Phrase, ProductCategory Category)[] phrases) =>
        phrases.Select(p => (Normalize(p.Phrase), p.Category)).ToArray();

    private static Dictionary<string, ProductCategory> Build(params (ProductCategory Category, string[] Words)[] groups)
    {
        var map = new Dictionary<string, ProductCategory>(StringComparer.Ordinal);
        foreach (var (category, words) in groups)
        {
            foreach (var word in words)
            {
                map.TryAdd(Normalize(word), category);
            }
        }

        return map;
    }
}

/// <summary>Cash-on-delivery amount bands used to compare basket size with refusals (IQD).</summary>
public static class BasketBands
{
    /// <summary>Upper limits of bands 1…4; band 0 is "no cash on delivery", band 5 is everything above the last limit.</summary>
    public static readonly IReadOnlyList<decimal> Limits = [25_000m, 50_000m, 100_000m, 200_000m];

    public const int NoCod = 0;

    public static int Count => Limits.Count + 2;

    public static int Of(decimal? codAmount)
    {
        if (codAmount is not > 0)
        {
            return NoCod;
        }

        for (var i = 0; i < Limits.Count; i++)
        {
            if (codAmount < Limits[i])
            {
                return i + 1;
            }
        }

        return Limits.Count + 1;
    }

    /// <summary>Lower and upper limit of a band (null where open); both null for <see cref="NoCod"/>.</summary>
    public static (decimal? From, decimal? To) Range(int band) => band switch
    {
        NoCod => (null, null),
        1 => (0m, Limits[0]),
        _ when band <= Limits.Count => (Limits[band - 2], Limits[band - 1]),
        _ => (Limits[^1], null),
    };
}
