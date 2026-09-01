using Fixture.Library;

namespace Fixture.Library.Tests;

public static class FeatureServiceTests
{
    public static void Generated_and_packaged_dependencies_are_available()
    {
        var service = new FeatureService();
        var result = service.Describe(2);
        if (result != "two:generated-from-additional-file")
        {
            throw new InvalidOperationException(result);
        }
    }
}
