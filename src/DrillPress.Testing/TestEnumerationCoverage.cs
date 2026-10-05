namespace DrillPress.Testing;

/// <summary>A source-bound synthetic advancement fact for testing enumeration policies without running collectors.</summary>
public sealed class TestEnumerationCoverage
{
    private readonly TestCallCoverage _state;

    internal TestEnumerationCoverage(TestCallCoverage state) => _state = state;

    /// <summary>Supplies synthetic first-advancement evidence, independently of collection or body execution.</summary>
    public TestCoverageFacts Started() => _state.Executed();

    /// <summary>Supplies a matching synthetic advancement point that was not reached.</summary>
    public TestCoverageFacts NotStarted() => _state.NotExecuted();

    /// <summary>Supplies inconclusive advancement evidence with explicit defined reasons.</summary>
    public TestCoverageFacts Unknown(params CoverageReason[] reasons) => _state.Unknown(reasons);
}
