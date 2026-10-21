namespace Logistico.Web.Client.Pages.Dashboard;

public sealed record DashboardAttentionItem(
    string Icon,
    string Text,
    int Count,
    string Href,
    string Tone,
    int Priority);
