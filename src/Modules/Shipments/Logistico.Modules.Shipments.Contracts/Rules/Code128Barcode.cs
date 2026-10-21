using System.Globalization;
using System.Net;
using System.Text;

namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Pure ISO/IEC 15417 Code-128 (Subset B) barcode encoder and vector SVG generator.
/// Produces crisp, dependency-free linear barcodes for 4x6" thermal AWB labels (203/300 DPI)
/// and on-screen action barcodes scannable by USB/Bluetooth laser/imager guns and phone cameras.
/// </summary>
public static class Code128Barcode
{
    private const int StartCodeB = 104;
    private const int StopCode = 106;

    // Official ISO/IEC 15417 bit patterns for values 0..106 (each symbol is 11 modules, Stop is 13 modules).
    private static readonly string[] Patterns =
    [
        "11011001100", "11001101100", "11001100110", "10010011000", "10010001100", "10001001100", "10011001000", "10011000100",
        "10001100100", "11001001000", "11001000100", "11000100100", "10110011100", "10011011100", "10011001110", "10111001100",
        "10011101100", "10011100110", "11001110010", "11001011100", "11001001110", "11011100100", "11001110100", "11101101110",
        "11101001100", "11100101100", "11100100110", "11101100100", "11100110100", "11100110010", "11011011000", "11011000110",
        "11000110110", "10100011000", "10001011000", "10001000110", "10110001000", "10001101000", "10001100010", "11010001000",
        "11000101000", "11000100010", "10110111000", "10110001110", "10001101110", "10111011000", "10111000110", "10001110110",
        "11101110110", "11010001110", "11000101110", "11011101000", "11011100010", "11011101110", "11101011000", "11101000110",
        "11100010110", "11101101000", "11101100010", "11100011010", "11101111010", "11001000010", "11110001010", "10100110000",
        "10100001100", "10010110000", "10010000110", "10000101100", "10000100110", "10110010000", "10110000100", "10011010000",
        "10011000010", "10000110100", "10000110010", "11000010010", "11001010000", "11110111010", "11000010100", "10001111010",
        "10100111100", "10010111100", "10010011110", "10111100100", "10011110100", "10011110010", "11110100100", "11110010100",
        "11110010010", "11011011110", "11011110110", "11110110110", "10101111000", "10100011110", "10001011110", "10111101000",
        "10111100010", "11110101000", "11110100010", "10111011110", "10111101110", "11101011110", "11110101110", "11010000100",
        "11010010000", "11010011100", "1100011101011"
    ];

    /// <summary>Encodes printable ASCII text (32..126) into a binary module string ('1' = bar, '0' = space).</summary>
    public static string EncodeModules(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var clean = value.Trim();

        var builder = new StringBuilder((clean.Length + 3) * 11 + 2);
        builder.Append(Patterns[StartCodeB]);

        var checksum = StartCodeB;
        var weight = 1;

        foreach (var ch in clean)
        {
            if (ch is < ' ' or > '~')
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"Character '{ch}' is outside printable ASCII range (32..126) for Code-128B.");
            }

            var code = ch - 32;
            builder.Append(Patterns[code]);
            checksum += code * weight;
            weight++;
        }

        var checkSymbol = checksum % 103;
        builder.Append(Patterns[checkSymbol]);
        builder.Append(Patterns[StopCode]);
        return builder.ToString();
    }

    /// <summary>Generates a self-contained, responsive SVG element for a Code-128B barcode.</summary>
    public static string ToSvg(string value, int barHeight = 54, bool showLabel = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var clean = value.Trim();
        var modules = EncodeModules(clean);
        const int quietZone = 10;
        var totalModules = modules.Length + (quietZone * 2);
        var textSpace = showLabel ? 18 : 0;
        var totalHeight = barHeight + textSpace + 6;

        var svg = new StringBuilder(1024);
        svg.AppendFormat(
            CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {1}\" width=\"100%\" height=\"{1}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"{2}\">",
            totalModules,
            totalHeight,
            WebUtility.HtmlEncode(clean));
        svg.AppendFormat(
            CultureInfo.InvariantCulture,
            "<rect width=\"{0}\" height=\"{1}\" fill=\"#ffffff\"/>",
            totalModules,
            totalHeight);

        var i = 0;
        while (i < modules.Length)
        {
            if (modules[i] == '1')
            {
                var start = i;
                while (i < modules.Length && modules[i] == '1')
                {
                    i++;
                }

                var width = i - start;
                svg.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<rect x=\"{0}\" y=\"3\" width=\"{1}\" height=\"{2}\" fill=\"#000000\"/>",
                    quietZone + start,
                    width,
                    barHeight);
            }
            else
            {
                i++;
            }
        }

        if (showLabel)
        {
            svg.AppendFormat(
                CultureInfo.InvariantCulture,
                "<text x=\"{0}\" y=\"{1}\" text-anchor=\"middle\" font-family=\"monospace\" font-size=\"11\" font-weight=\"700\" fill=\"#000000\" letter-spacing=\"1.2\">{2}</text>",
                totalModules / 2.0,
                barHeight + 16,
                WebUtility.HtmlEncode(clean));
        }

        svg.Append("</svg>");
        return svg.ToString();
    }
}

