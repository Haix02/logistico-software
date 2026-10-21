using Logistico.Modules.Shipments.Contracts.Rules;

namespace Logistico.Modules.Shipments.Tests;

public class TrackingNumberTests
{
    [Fact]
    public void Luhn_check_digit_matches_the_reference_example()
    {
        // Classic Luhn example: 7992739871 → check digit 3
        Assert.Equal(3, TrackingNumber.LuhnCheckDigit("7992739871"));
    }

    [Fact]
    public void Create_formats_prefix_year_sequence_and_check_digit()
    {
        var number = TrackingNumber.Create(2026, 123);

        Assert.Equal(TrackingNumber.Length, number.Length);
        Assert.StartsWith("LX2600000123", number, StringComparison.Ordinal);
        Assert.True(TrackingNumber.IsValid(number));
    }

    [Fact]
    public void IsValid_accepts_lowercase_spaces_and_dashes()
    {
        var number = TrackingNumber.Create(2026, 12345);
        var messy = $"lx {number[2..6]}-{number[6..10]}-{number[10..]}".ToLowerInvariant();

        Assert.True(TrackingNumber.IsValid(messy));
        Assert.Equal(number, TrackingNumber.Normalize(messy));
    }

    [Theory]
    [InlineData("LX2600000123")]   // too short
    [InlineData("XX26000001234")]  // wrong prefix
    [InlineData("LX2600000A234")]  // not a digit
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_rejects_malformed_numbers(string? input)
    {
        Assert.False(TrackingNumber.IsValid(input));
    }

    [Fact]
    public void A_single_mistyped_digit_is_detected()
    {
        var number = TrackingNumber.Create(2026, 4015);
        var digit = number[8] == '9' ? '0' : (char)(number[8] + 1);
        var mistyped = number[..8] + digit + number[9..];

        Assert.False(TrackingNumber.IsValid(mistyped));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TrackingNumber.MaxSequence + 1)]
    public void Create_rejects_sequences_out_of_range(long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingNumber.Create(2026, sequence));
    }
}
