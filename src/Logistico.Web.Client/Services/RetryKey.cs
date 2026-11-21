using System.Text.Json;
using Logistico.SharedKernel;

namespace Logistico.Web.Client.Services;

/// <summary>
/// One Idempotency-Key per thing being sent. Sending the same content again (a retry after a lost answer) reuses the key,
/// so the server books it once; changed content is a new request and gets a new key; after a success the next send starts
/// afresh, even with the same content (booking the same customer twice on purpose).
/// </summary>
internal sealed class RetryKey
{
    private string? content;
    private string key = IdempotencyKey.New();

    public string For(object body)
    {
        var json = JsonSerializer.Serialize(body, body.GetType());
        if (json != content)
        {
            content = json;
            key = IdempotencyKey.New();
        }

        return key;
    }

    /// <summary>The request went through: the next one is a new intention.</summary>
    public void Done() => content = null;
}
