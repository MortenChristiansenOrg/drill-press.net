namespace Sample.WidgetLibrary;

public interface IWidgetStore
{
    string Read();
}

public sealed class FileWidgetStore : IWidgetStore
{
    public string Read() => string.Empty;
}

public interface IWidgetFormatter
{
    string Format(string value);
}

public sealed class PlainWidgetFormatter : IWidgetFormatter
{
    public string Format(string value) => value;
}

public sealed class UpperWidgetFormatter : IWidgetFormatter
{
    public string Format(string value) => value.ToUpperInvariant();
}
