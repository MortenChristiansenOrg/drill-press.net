using DrillPress.Engine;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class TestSourceGeneratorCatalog(params CompiledSourceGenerator[] generators)
    : SourceGeneratorCatalog(generators);
