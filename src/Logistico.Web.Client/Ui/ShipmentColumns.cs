namespace Logistico.Web.Client.Ui;

/// <summary>Columns a <see cref="ShipmentTable"/> can show.</summary>
[Flags]
public enum ShipmentColumns
{
    None = 0,
    Tracking = 1,
    Status = 2,
    Seller = 4,
    Customer = 8,
    Destination = 16,
    Route = 32,
    Shelf = 64,
    Cod = 128,
    Eta = 256,
    Age = 512,
    Courier = 1024,
    Created = 2048,

    /// <summary>The main shipments list.</summary>
    List = Tracking | Status | Seller | Customer | Destination | Route | Cod | Eta,

    /// <summary>A hub's shelves.</summary>
    Hub = Shelf | Tracking | Status | Customer | Destination | Route | Cod | Age,

    /// <summary>Returns waiting or travelling back.</summary>
    Returns = Tracking | Status | Seller | Customer | Route | Shelf | Age,
}
