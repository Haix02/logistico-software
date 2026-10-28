using System.ComponentModel.DataAnnotations;
using Logistico.SharedKernel;

namespace Logistico.Modules.Shipments.Contracts;

/// <summary>A note staff left on an order: who (e-mail and name), when, and what.</summary>
public sealed record ShipmentCommentItem(Guid Id, string Author, string? AuthorName, string Text, DateTimeOffset CreatedAt);

public sealed class AddCommentRequest
{
    [Required(ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.Required))]
    [StringLength(1000, ErrorMessageResourceType = typeof(ValidationText), ErrorMessageResourceName = nameof(ValidationText.MaxLength))]
    public string Text { get; set; } = "";
}
