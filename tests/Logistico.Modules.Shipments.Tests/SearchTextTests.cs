using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Tests;

/// <summary>Search finds what staff type, however they type it (see <see cref="SearchText"/>).</summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إبراهيم", "ابراهيم")]
    [InlineData("آمنة", "امنه")]
    [InlineData("فاطمة", "فاطمه")]
    [InlineData("مصطفى", "مصطفي")]
    [InlineData("مؤسسة", "موسسه")]
    [InlineData("مُحَمَّد", "محمد")]
    [InlineData("عـــلي", "علي")]
    [InlineData("٠٧٧٠ ۱۲۳", "0770 123")]
    [InlineData("LX26 Baghdad", "lx26 baghdad")]
    public void Arabic_letter_variants_digits_marks_and_case_fold_to_one_form(string typed, string folded) =>
        Assert.Equal(folded, SearchText.Fold(typed));

    [Theory]
    [InlineData("0770 123", "770123")]
    [InlineData("+964 770 123 4567", "7701234567")]
    [InlineData("00964770123", "770123")]
    [InlineData("٠٧٨١-٢٢٢", "781222")]
    [InlineData("8024", "8024")]
    [InlineData("LX26 0000 80", "lx26000080")]
    [InlineData("lx-2600-0008", "lx26000008")]
    public void A_phone_or_tracking_number_typed_in_groups_is_one_word(string typed, string word) =>
        Assert.Equal([word], SearchText.Words(typed));

    [Fact]
    public void Words_are_folded_distinct_and_lose_an_instagram_at()
    {
        Assert.Equal(["ساره", "محمد"], SearchText.Words("  سارة   محمد سارة "));
        Assert.Equal(["lamar.boutique"], SearchText.Words("@lamar.boutique"));
        Assert.Empty(SearchText.Words("   "));
        Assert.Equal(SearchText.MaxWords, SearchText.Words("a b c d e f g h").Count);
    }

    [Fact]
    public void Like_patterns_escape_wildcards_and_short_words_match_the_start_of_a_word()
    {
        Assert.Equal(@"%50\%\_off\\%", SearchText.Pattern(@"50%_off\"));
        Assert.Equal("% 1%", SearchText.Pattern("1"));
        Assert.True(SearchText.Matches("list customer 1", "LX26000000146", "List Customer 1"));
        Assert.False(SearchText.Matches("list customer 1", "LX26000000146", "List Customer 2"));
    }

    [Theory]
    [InlineData("الاناقة", true)]
    [InlineData("دار الاناقه", true)]
    [InlineData("ahmed basra", true)]
    [InlineData("ahmed erbil", false)]
    public void Every_word_must_appear_in_one_of_the_fields(string query, bool found) =>
        Assert.Equal(found, SearchText.Matches(query, "دار الأناقة", "Ahmed Saleh", "Basra"));

    [Fact]
    public void The_sql_fold_uses_the_same_mapping_as_the_dotnet_fold()
    {
        Assert.Equal(SearchText.FoldTo.Length + 10, SearchText.FoldFrom.Length); // ten dropped marks
        Assert.Equal($"translate(lower(' ' || name), '{SearchText.FoldFrom}', '{SearchText.FoldTo}')", SearchText.Sql("name"));
    }
}
