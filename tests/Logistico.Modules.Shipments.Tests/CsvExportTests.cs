using Logistico.Modules.Shipments.Endpoints;

namespace Logistico.Modules.Shipments.Tests;

public sealed class CsvExportTests
{
    [Theory]
    [InlineData("=2+2", "'=2+2")]
    [InlineData(" +SUM(A1:A2)", "' +SUM(A1:A2)")]
    [InlineData("@IMPORTDATA(\"https://example.test\")", "\"'@IMPORTDATA(\"\"https://example.test\"\")\"")]
    [InlineData("\t=cmd", "'\t=cmd")]
    public void Csv_neutralizes_spreadsheet_formulas(string input, string expected) =>
        Assert.Equal(expected, Api.Csv(input));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Baghdad", "Baghdad")]
    [InlineData("Baghdad, Iraq", "\"Baghdad, Iraq\"")]
    [InlineData("The \"fast\" lane", "\"The \"\"fast\"\" lane\"")]
    public void Csv_preserves_regular_values_and_escapes_csv_metacharacters(string? input, string expected) =>
        Assert.Equal(expected, Api.Csv(input));
}
