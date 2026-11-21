namespace Logistico.SharedKernel;

/// <summary>
/// Safe retries for requests that create something (IETF draft "The Idempotency-Key HTTP Header Field"). A client sends
/// the same key with every attempt of one intention (one booking, one bulk import); the server runs it once and answers
/// later attempts with the first answer. Shared by the API and its clients.
/// </summary>
public static class IdempotencyKey
{
    public const string Header = "Idempotency-Key";

    /// <summary>Set on an answer that repeats the stored answer of an earlier attempt.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    public const int MaxLength = 255;

    /// <summary>A new key for one intention: 32 hex characters.</summary>
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>1–255 visible ASCII characters (an RFC 8941 string without quotes or backslashes).</summary>
    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaxLength } && key.All(c => c is >= '!' and <= '~' and not '"' and not '\\');
}
