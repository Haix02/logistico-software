namespace Logistico.Web.Client;

/// <summary>Marker type for the client's localised strings (Resources/UiText[.ar].resx), injected as <c>L</c> in every component.</summary>
public sealed class UiText
{
    /// <summary>Folder that holds the .resx files; must match the localization registration in Program.cs.</summary>
    public const string ResourcesPath = "Resources";
}
