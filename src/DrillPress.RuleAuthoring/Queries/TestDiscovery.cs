using Microsoft.CodeAnalysis;

namespace DrillPress;

internal static class TestDiscovery
{
    private static readonly CodeType[] _markers =
    [
        CodeType.Named("Xunit.FactAttribute", "xunit.core"),
        CodeType.Named("Xunit.FactAttribute", "xunit.v3.core"),
        CodeType.Named("Xunit.FactAttribute", "xunit.v3.core.aot"),
        CodeType.Named("NUnit.Framework.TestAttribute", "nunit.framework"),
        CodeType.Named("NUnit.Framework.TestCaseAttribute", "nunit.framework"),
        CodeType.Named("NUnit.Framework.TestCaseSourceAttribute", "nunit.framework"),
        CodeType.Named("NUnit.Framework.TheoryAttribute", "nunit.framework"),
        CodeType.Named(
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
            "Microsoft.VisualStudio.TestPlatform.TestFramework"
        ),
        CodeType.Named(
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
            "MSTest.TestFramework"
        ),
    ];

    internal static CodeQuery<CodeMethod> TestMethods { get; } =
        Code.Methods.Where(method => method.Symbol is { } symbol && IsTest(symbol));

    internal static CodeQuery<CodeTypeDefinition> TestClasses { get; } =
        Classes(abstractBases: false);

    internal static CodeQuery<CodeTypeDefinition> AbstractTestClasses { get; } =
        Classes(abstractBases: true);

    private static CodeQuery<CodeTypeDefinition> Classes(bool abstractBases) =>
        Code.Types.Where(type =>
            type.Source.Project.IsTestProject
            && type.Symbol.TypeKind == TypeKind.Class
            && type.Symbol.IsAbstract == abstractBases
            && HasTests(type.Symbol)
        );

    private static bool HasTests(INamedTypeSymbol type)
    {
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (!overridden.Contains(method.OriginalDefinition) && IsTest(method))
                    return true;
                for (
                    var ancestor = method.OverriddenMethod;
                    ancestor is not null;
                    ancestor = ancestor.OverriddenMethod
                )
                    overridden.Add(ancestor.OriginalDefinition);
            }
        return type
            .AllInterfaces.SelectMany(contract => contract.GetMembers().OfType<IMethodSymbol>())
            .Any(method =>
                !method.IsAbstract
                && !method.IsStatic
                && method.DeclaredAccessibility == Accessibility.Public
                && IsTest(method, reflectionOnly: true)
            );
    }

    private static bool IsTest(IMethodSymbol method, bool reflectionOnly = false)
    {
        var inherited = false;
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            foreach (var attribute in current.GetAttributes())
                if (
                    attribute.AttributeClass is { } type
                    && IsMarker(type, reflectionOnly)
                    && (!inherited || IsInherited(type))
                )
                    return true;
            inherited = true;
        }
        return false;
    }

    private static bool IsMarker(INamedTypeSymbol type, bool reflectionOnly)
    {
        if (
            reflectionOnly
                ? type.IsOrDerivesFrom(CodeType.Named("Xunit.FactAttribute", "xunit.v3.core"))
                : _markers.Any(type.IsOrDerivesFrom)
        )
            return true;
        if (!Symbols.Implements(type, CodeType.Named("Xunit.v3.IFactAttribute", "xunit.v3.core")))
            return false;
        for (var current = type; current is not null; current = current.BaseType)
            if (
                current.HasAttribute(
                    CodeType.Named("Xunit.v3.XunitTestCaseDiscovererAttribute", "xunit.v3.core")
                )
            )
                return true;
        return false;
    }

    private static bool IsInherited(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var attribute in current.GetAttributes())
                if (
                    attribute.AttributeClass?.SpecialType == SpecialType.None
                    && CodeType.Of<AttributeUsageAttribute>().Matches(attribute.AttributeClass)
                )
                    return !attribute.NamedArguments.Any(argument =>
                        argument.Key == "Inherited" && argument.Value.Value is false
                    );
        return true;
    }
}
