namespace Logistico.Modules.Shipments.Contracts.Rules;

/// <summary>
/// Default delivery promise for domestic orders: next day inside the same governorate, three days elsewhere.
/// Fridays are skipped (couriers do not deliver on Friday).
/// </summary>
public static class DeliveryPromise
{
    public const int SameGovernorateDays = 1;
    public const int OtherGovernorateDays = 3;

    private static readonly TimeZoneInfo Baghdad = FindBaghdad();

    public static DateTimeOffset Estimate(string originGovernorate, string destinationGovernorate, DateTimeOffset bookedAt)
    {
        var days = string.Equals(originGovernorate, destinationGovernorate, StringComparison.OrdinalIgnoreCase)
            ? SameGovernorateDays
            : OtherGovernorateDays;

        var local = TimeZoneInfo.ConvertTime(bookedAt, Baghdad).Date;
        var date = local;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek != DayOfWeek.Friday)
            {
                days--;
            }
        }

        // End of the promised day (20:00 Baghdad time).
        var promised = new DateTimeOffset(date.AddHours(20), Baghdad.GetUtcOffset(date));
        return promised.ToUniversalTime();
    }

    /// <summary>End of the next working day (20:00 Baghdad time), skipping Friday.</summary>
    public static DateTimeOffset NextWorkingDay(DateTimeOffset from)
    {
        var date = TimeZoneInfo.ConvertTime(from, Baghdad).Date.AddDays(1);
        if (date.DayOfWeek == DayOfWeek.Friday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(date.AddHours(20), Baghdad.GetUtcOffset(date)).ToUniversalTime();
    }

    /// <summary>End of the given calendar day in Baghdad (20:00), used when a customer picks a delivery date.</summary>
    public static DateTimeOffset EndOfDay(DateTimeOffset day)
    {
        var date = TimeZoneInfo.ConvertTime(day, Baghdad).Date;
        return new DateTimeOffset(date.AddHours(20), Baghdad.GetUtcOffset(date)).ToUniversalTime();
    }

    /// <summary>The calendar date at the company's operating timezone.</summary>
    public static DateTime BaghdadDate(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Baghdad).Date;

    /// <summary>True when the date falls on a Friday in Baghdad (no deliveries).</summary>
    public static bool IsFriday(DateTimeOffset day) => TimeZoneInfo.ConvertTime(day, Baghdad).DayOfWeek == DayOfWeek.Friday;

    private static TimeZoneInfo FindBaghdad()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Baghdad", TimeSpan.FromHours(3), "Baghdad", "Baghdad");
        }
    }
}
