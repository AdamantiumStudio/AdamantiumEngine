using Serilog;
using Serilog.Events;

namespace Adamantium.GameHost;

public static class Program
{
    public static int Main(string[] args)
    {
        GameHostOptions options;
        try
        {
            options = GameHostOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
            .CreateLogger();
        try
        {
            using var host = new GameHost(options);
            var stopAsked = false;
            Console.CancelKeyPress += (_, cancel) =>
            {
                cancel.Cancel = !stopAsked;
                stopAsked = true;
                host.Stop();
            };
            host.Run();
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
