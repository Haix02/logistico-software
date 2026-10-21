namespace Logistico.Web.Client.Ui;

public sealed record ConfirmRequest(string Title, string Message, string ConfirmText, Intent Intent, string Icon, TaskCompletionSource<bool> Completion);

/// <summary>Asks the user to confirm an action with a Blueprint alert: <c>if (await Confirmer.AskAsync(...))</c>.</summary>
public sealed class Confirmer
{
    public ConfirmRequest? Current { get; private set; }

    public event Action? Changed;

    public Task<bool> AskAsync(string title, string message, string confirmText, Intent intent = Intent.Primary, string icon = "help")
    {
        Current?.Completion.TrySetResult(false);
        var request = new ConfirmRequest(title, message, confirmText, intent, icon, new TaskCompletionSource<bool>());
        Current = request;
        Changed?.Invoke();
        return request.Completion.Task;
    }

    public void Resolve(bool confirmed)
    {
        var request = Current;
        Current = null;
        Changed?.Invoke();
        request?.Completion.TrySetResult(confirmed);
    }
}
