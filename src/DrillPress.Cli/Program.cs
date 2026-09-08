using DrillPress.Cli;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    return (int)await new CliApplication().RunAsync(args, cancellationToken: cancellation.Token);
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("drillpress: Cancelled.");
    return (int)CliExitCode.Failure;
}
