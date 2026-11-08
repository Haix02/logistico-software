namespace Logistico.Web.Client.Ui;

/// <param name="DismissText">The button that changes nothing, when "Cancel" would be confused with the action (cancelling an order).</param>
public sealed record ConfirmRequest(string Title, string Message, string ConfirmText, Intent Intent, string Icon, TaskCompletionSource<bool> Completion, string? DismissText = null);

/// <summary>Asks the user to confirm an action with a Blueprint alert: <c>if (await Confirmer.AskAsync(...))</c>.</summary>
public sealed class Confirmer
{
    public ConfirmRequest? Current { get; private set; }

    public event Action? Changed;

    public Task<bool> AskAsync(string title, string message, string confirmText, Intent intent = Intent.Primary, string icon = "help", string? dismissText = null)
    {
        Current?.Completion.TrySetResult(false);
        var request = new ConfirmRequest(title, message, confirmText, intent, icon, new TaskCompletionSource<bool>(), dismissText);
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
