using Serilog;
using Serilog.Events;

namespace Adamantium.UniverseHost;

/// <summary>Started with the name of the pipe to serve its viewer on; everything else comes from the viewer.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Adamantium.UniverseHost <pipe to serve the viewer on>");
            return 2;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
            .CreateLogger();
        try
        {
            using var host = new UniverseHost();
            var stopAsked = false;
            Console.CancelKeyPress += (_, cancel) =>
            {
                cancel.Cancel = !stopAsked;
                stopAsked = true;
                host.Stop();
            };
            host.Serve(args[0]);
            return host.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("The game could not play: " + exception);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
