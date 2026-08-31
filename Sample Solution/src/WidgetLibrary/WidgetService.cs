namespace Sample.WidgetLibrary;

public sealed class WidgetService
{
    public bool Contains(IReadOnlyList<string> values, string candidate)
    {
        return values.Contains(candidate, StringComparer.Ordinal);
    }

    public string CurrentStamp()
    {
        return DateTime.Now.ToString("O");
    }

    public void WaitForStorage()
    {
        System.Threading.Thread.Sleep(10);
    }
}
