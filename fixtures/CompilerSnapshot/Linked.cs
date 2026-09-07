namespace Linked;
public static class Shared
{
#if NET10_0
    public static string Value => string.Empty;
#else
    public static string Value => "";
#endif
}
