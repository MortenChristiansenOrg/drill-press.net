namespace DrillPress.Engine;

internal sealed class CoverageEvaluations
{
    private readonly Dictionary<string, string> _results = [];

    internal void Refresh() => _results.Clear();

    internal async Task<string> GetAsync(string key, Func<Task<string>> evaluate)
    {
        if (!_results.TryGetValue(key, out var result))
        {
            result = await evaluate();
            _results.Add(key, result);
        }
        return result;
    }
}
