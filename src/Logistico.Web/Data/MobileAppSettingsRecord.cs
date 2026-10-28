using System.Text.Json;
using Logistico.SharedKernel.Mobile;

namespace Logistico.Web.Data;

/// <summary>A company's mobile app settings (ADR-031), kept as one JSON document so the app can grow without migrations.</summary>
public class MobileAppSettingsRecord
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid TenantId { get; set; }
    public string Settings { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public uint Version { get; set; }

    public MobileAppSettings Read() => JsonSerializer.Deserialize<MobileAppSettings>(Settings, Json) ?? new MobileAppSettings();

    public void Write(MobileAppSettings settings, string? by, DateTimeOffset now)
    {
        Settings = JsonSerializer.Serialize(settings, Json);
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
