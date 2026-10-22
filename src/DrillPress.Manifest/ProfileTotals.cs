namespace DrillPress.Manifest;

// Wall and CPU milliseconds spent on some work.
internal readonly record struct ProfileTotals(double Wall, double UserCpu, double SystemCpu)
{
    public static ProfileTotals operator +(ProfileTotals left, ProfileTotals right) =>
        new(left.Wall + right.Wall, left.UserCpu + right.UserCpu, left.SystemCpu + right.SystemCpu);

    // Shared work runs inside the measured scope, so the difference is never meaningfully negative.
    public static ProfileTotals operator -(ProfileTotals left, ProfileTotals right) =>
        new(
            Math.Max(0, left.Wall - right.Wall),
            Math.Max(0, left.UserCpu - right.UserCpu),
            Math.Max(0, left.SystemCpu - right.SystemCpu)
        );
}
