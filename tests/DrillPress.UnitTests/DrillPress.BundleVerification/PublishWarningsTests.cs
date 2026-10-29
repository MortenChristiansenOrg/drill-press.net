using DrillPress.BundleVerification;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.BundleVerification;

public sealed class PublishWarningsTests
{
    [Theory]
    [InlineData(
        "/_/CommonCompiler.cs(175): warning IL3000: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation"
    )]
    [InlineData(
        @"C:\src\CommonCompiler.cs(175): warning IL3000: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation"
    )]
    [InlineData(
        "ILC : Trim analysis warning IL2091: Roslyn.Utilities.RoslynLazyInitializer.EnsureInitialized<T>(!!0&): explanation"
    )]
    [InlineData(
        "ILC : Trim analysis warning IL2050: Microsoft.DiaSymReader.SymUnmanagedFactory.CreateObject(Boolean,Boolean,Boolean,String&,Exception&): explanation"
    )]
    [InlineData(
        "ILC : Trim analysis warning IL2072: Microsoft.DiaSymReader.SymUnmanagedFactory.CreateObject(Boolean,Boolean,Boolean,String&,Exception&): explanation"
    )]
    public void Accepts_reviewed_origins(string output)
    {
        PublishWarnings.Validate(output);
    }

    [Theory]
    [InlineData("warning IL3000: Other.Type.Location(): explanation")]
    [InlineData(
        "warning IL2026: Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type): explanation"
    )]
    [InlineData("warning NU1605: Detected package downgrade")]
    [InlineData("warning IL2050: Other.Type.Interop(): explanation")]
    [InlineData("warning IL2072: Other.Type.Reflection(): explanation")]
    public void Rejects_unknown_origins_or_codes(string output)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishWarnings.Validate(output)
        );

        Assert.Equal($"Unexplained publication warning: {output}", exception.Message);
    }
}
