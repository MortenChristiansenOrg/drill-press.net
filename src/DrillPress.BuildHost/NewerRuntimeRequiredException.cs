namespace DrillPress.BuildHost;

/// <summary>The target's SDK belongs to a newer .NET than the runtime BuildHost runs on, so MSBuild can't load it.</summary>
internal sealed class NewerRuntimeRequiredException(Version sdkVersion, string directory)
    : InvalidOperationException(
        $"The .NET SDK {sdkVersion} selected for '{directory}' needs the .NET {sdkVersion.Major} runtime or newer, but BuildHost runs on .NET {Environment.Version}. Install the .NET {sdkVersion.Major} runtime."
    );
