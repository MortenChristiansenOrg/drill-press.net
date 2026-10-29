using System.Diagnostics.CodeAnalysis;

namespace DrillPress.Engine;

/// <summary>Identifies a bundle's statically linked source generators without changing its rule entry point.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SourceGeneratorCatalogAttribute : Attribute
{
    /// <summary>Names a concrete catalogue with a public parameterless constructor; its constructor remains available under NativeAOT trimming.</summary>
    public SourceGeneratorCatalogAttribute(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
            Type catalogType
    )
    {
        if (
            catalogType.IsAbstract
            || catalogType.ContainsGenericParameters
            || !catalogType.IsAssignableTo(typeof(SourceGeneratorCatalog))
            || catalogType.GetConstructor(Type.EmptyTypes) is null
        )
            throw new ArgumentException(
                "Expected a concrete source generator catalogue with a public parameterless constructor.",
                nameof(catalogType)
            );
        CatalogType = catalogType;
    }

    /// <summary>The bundle-owned catalogue instantiated for one generation session.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type CatalogType { get; }

    internal SourceGeneratorCatalog Create() =>
        (SourceGeneratorCatalog)Activator.CreateInstance(CatalogType)!;
}
