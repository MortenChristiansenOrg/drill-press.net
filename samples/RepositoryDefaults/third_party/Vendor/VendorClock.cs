namespace Vendor;

// Vendored as-is from an upstream package; local conventions do not apply.
public sealed class VendorClock
{
    public static VendorClock Create() => new();

    public string Format(DateTime time)
    {
        string text = time.ToString("O");
        // drillpress-allow SHOP001
        return text + string.Empty;
    }
}
