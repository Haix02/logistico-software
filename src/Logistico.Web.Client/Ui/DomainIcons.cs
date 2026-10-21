using Logistico.Modules.Shipments.Contracts;

namespace Logistico.Web.Client.Ui;

/// <summary>Blueprint icon names for domain concepts, so every page shows the same icon for the same thing.</summary>
public static class DomainIcons
{
    public static string Of(RequestType type) => type switch
    {
        RequestType.Reschedule => "calendar",
        RequestType.ChangeAddress => "map-marker",
        RequestType.ChangePhone => "phone",
        RequestType.Cancel => "disable",
        RequestType.Return => "undo",
        _ => "chat",
    };

    public static string Of(AlertKind kind) => kind switch
    {
        AlertKind.PickupOverdue => "shopping-cart",
        AlertKind.WarehouseDwell => "box",
        AlertKind.OutForDeliveryOverdue => "truck",
        AlertKind.PromiseMissed => "time",
        AlertKind.TransferOverdue => "drive-time",
        AlertKind.MissingInTransfer => "search-around",
        AlertKind.Misrouted => "route",
        AlertKind.ReturnNotCollected => "undo",
        _ => "inbox",
    };
}
