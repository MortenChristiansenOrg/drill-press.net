namespace Xunit;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute;

public static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
    }

    public static void True(bool value)
    {
    }

    public static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
