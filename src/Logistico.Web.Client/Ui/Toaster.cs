namespace Logistico.Web.Client.Ui;

public sealed record ToastMessage(Guid Id, string Message, Intent Intent, string Icon);

/// <summary>Queues short notifications (Blueprint toasts) shown at the top of the screen.</summary>
public sealed class Toaster
{
    private readonly List<ToastMessage> _items = [];

    public IReadOnlyList<ToastMessage> Items => _items;

    public event Action? Changed;

    public void Success(string message) => Show(message, Intent.Success, "tick-circle");

    public void Error(string message) => Show(message, Intent.Danger, "error", TimeSpan.FromSeconds(7));

    public void Warning(string message) => Show(message, Intent.Warning, "warning-sign", TimeSpan.FromSeconds(6));

    public void Info(string message) => Show(message, Intent.Primary, "info-sign");

    public void Show(string message, Intent intent, string icon, TimeSpan? duration = null)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, intent, icon);
        _items.Add(toast);
        if (_items.Count > 4)
        {
            _items.RemoveAt(0);
        }

        Changed?.Invoke();
        _ = HideLaterAsync(toast.Id, duration ?? TimeSpan.FromSeconds(4));
    }

    public void Dismiss(Guid id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private async Task HideLaterAsync(Guid id, TimeSpan after)
    {
        await Task.Delay(after);
        Dismiss(id);
    }
}
