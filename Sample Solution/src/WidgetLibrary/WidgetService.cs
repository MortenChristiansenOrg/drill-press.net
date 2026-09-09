namespace Sample.WidgetLibrary;

public sealed class WidgetService
{
    public bool Contains(IReadOnlyList<string> values, string candidate)
    {
        return values.Contains(candidate, StringComparer.Ordinal);
    }
}
