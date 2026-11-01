namespace Packata.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--python-host", var source])
            return PythonHost.RunAsync(source).GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try { return Run(args, cancellation.Token); }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static int Run(string[] args, CancellationToken cancellationToken)
        => CliApplication.Run(args, cancellationToken);
}
