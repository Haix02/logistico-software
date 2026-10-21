namespace Logistico.Web.Client.Ui.Map;

/// <summary>How <see cref="IraqChoropleth"/> colours values.</summary>
public enum ChoroplethScale
{
    /// <summary>Amounts (orders, IQD): light → dark blue.</summary>
    Sequential,

    /// <summary>Rates where more is good (delivery success): red → green between the lowest and highest governorate.</summary>
    HigherIsBetter,

    /// <summary>Rates or durations where less is good (returns, delivery hours): green → red.</summary>
    LowerIsBetter,
}
