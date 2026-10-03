namespace DrillPress.Engine;

internal sealed record CoverageTestRun(string ProjectPath, string Framework);

internal sealed record CoveragePlan(
    CoverageTestRun[] Tests,
    string[] InputPaths,
    string SdkIdentity
);
