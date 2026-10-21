namespace Logistico.Web.Client.Ui;

/// <summary>Current sort of a <see cref="DataTable{TItem}"/>; cascaded to its <see cref="SortHeader"/> cells.</summary>
public sealed class SortState(Func<string, bool, Task> onChanged)
{
    public string? Key { get; private set; }
    public bool Desc { get; private set; }

    public void Set(string? key, bool desc)
    {
        Key = key;
        Desc = desc;
    }

    public Task ToggleAsync(string key)
    {
        Desc = Key == key ? !Desc : key is "createdAt" or "cod" or "eta";
        Key = key;
        return onChanged(key, Desc);
    }
}
