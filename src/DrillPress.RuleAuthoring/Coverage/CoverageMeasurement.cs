namespace DrillPress;

internal sealed record CoverageMeasurement(int Covered, int Total, bool Complete)
{
    public double Percentage => Total == 0 ? 0 : 100.0 * Covered / Total;
}
