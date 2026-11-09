using Vendor;

namespace Shop;

public static class Checkout
{
    public static string Stamp()
    {
        VendorClock clock = VendorClock.Create();
        return clock.Format(DateTime.Now) + string.Empty;
    }
}
