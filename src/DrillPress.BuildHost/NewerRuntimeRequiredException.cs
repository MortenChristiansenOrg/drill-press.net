namespace DrillPress.BuildHost;

/// <summary>The target's SDK belongs to a newer .NET than the runtime BuildHost runs on, so MSBuild can't load it.</summary>
internal sealed class NewerRuntimeRequiredException(SelectedSdk sdk, string directory)
    : InvalidOperationException(
        $"The .NET SDK {sdk.Version} selected for '{directory}' needs the .NET {sdk.Version.Major} runtime or newer, but BuildHost runs on .NET {Environment.Version}. Install the .NET {sdk.Version.Major} runtime."
    )
{
    /// <summary>The SDK whose runtime BuildHost needs.</summary>
    public SelectedSdk Sdk { get; } = sdk;
}
