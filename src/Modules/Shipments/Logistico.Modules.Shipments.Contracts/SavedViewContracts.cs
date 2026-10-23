using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>Lists that can have saved views.</summary>
public static class SavedViewLists
{
    public const string Shipments = "shipments";

    public static readonly IReadOnlyList<string> All = [Shipments];
}

public sealed record SavedViewItem(Guid Id, string List, string Name, string Query);

public sealed class SaveViewRequest : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    public string List { get; set; } = SavedViewLists.Shipments;

    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(60, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Name { get; set; } = "";

    /// <summary>The address query of the view, starting with "?" (empty for the list as it opens).</summary>
    [StringLength(1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Query { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!SavedViewLists.All.Contains(List))
        {
            yield return new(ValidationText.Choice, [nameof(List)]);
        }

        if (Query.Length > 0 && (Query[0] != '?' || Query.Any(char.IsWhiteSpace)))
        {
            yield return new(ValidationText.Format, [nameof(Query)]);
        }
    }
}
