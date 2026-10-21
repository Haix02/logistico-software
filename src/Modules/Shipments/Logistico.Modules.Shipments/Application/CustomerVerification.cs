using Logistico.Modules.Shipments.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace Logistico.Modules.Shipments.Application;

internal enum VerificationResult
{
    Verified,
    WrongDigits,
    Locked,
}

/// <summary>
/// Proves an anonymous caller on the tracking page is the customer: the last four digits of the order's mobile number.
/// Four digits are only ten thousand guesses and a correct guess can change the phone or address of a parcel, so each
/// tracking number accepts <see cref="MaxFailures"/> wrong answers per <see cref="Window"/> however many addresses they
/// come from (the per-address rate limit alone does not stop guesses spread over many addresses). Once locked, even
/// the right digits are refused until the window has passed; customer care can still help by phone.
/// </summary>
/// <remarks>Counts live in memory, so each server instance keeps its own; the limit multiplies by the instance count.</remarks>
internal sealed class CustomerVerification(IMemoryCache cache, TimeProvider clock)
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public VerificationResult Check(Shipment shipment, string phoneLast4)
    {
        var now = clock.GetUtcNow();
        var key = "customer-verification:" + shipment.TrackingNumber;
        var failures = cache.Get<Failures>(key);
        if (failures?.CountSince(now - Window) >= MaxFailures)
        {
            return VerificationResult.Locked;
        }

        if (shipment.ConsigneePhone is { Length: >= 4 } phone && phoneLast4.Length == 4 && phone.EndsWith(phoneLast4, StringComparison.Ordinal))
        {
            return VerificationResult.Verified;
        }

        failures ??= cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = Window;
            return new Failures();
        })!;
        failures.Add(now);
        return VerificationResult.WrongDigits;
    }

    private sealed class Failures
    {
        private readonly Lock _gate = new();
        private readonly List<DateTimeOffset> _at = [];

        public void Add(DateTimeOffset at)
        {
            lock (_gate)
            {
                _at.Add(at);
            }
        }

        public int CountSince(DateTimeOffset since)
        {
            lock (_gate)
            {
                _at.RemoveAll(t => t < since);
                return _at.Count;
            }
        }
    }
}
