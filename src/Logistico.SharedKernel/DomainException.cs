namespace Logistico.SharedKernel;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is stable and machine-readable (e.g. <c>shipment.invalid_transition</c>);
/// <see cref="Args"/> fill the localised message for that code. The exception message stays English for logs.
/// </summary>
public sealed class DomainException(string code, string message, params object?[] args) : Exception(message)
{
    public string Code { get; } = code;
    public object?[] Args { get; } = args;
}
