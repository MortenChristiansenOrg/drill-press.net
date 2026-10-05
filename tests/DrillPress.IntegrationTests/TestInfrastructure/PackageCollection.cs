using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

[CollectionDefinition]
public sealed class PackageCollection : ICollectionFixture<PackageFixture>;
