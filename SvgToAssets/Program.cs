using System.CommandLine;
using System.Globalization;
using System.Text;
using Meziantou.Framework;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;
using Spectre.Console;
using SvgToAssets.Commands;
using SvgToAssets.Managers;

// Before Spectre reads the console profile, so it draws Unicode bars, spinners and status glyphs.
Console.OutputEncoding = Encoding.UTF8;

// A live progress display owns the cursor, so console logs must print through Spectre to land above it.
var liveConsole = AnsiConsole.Profile.Capabilities.Interactive;

var logging = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Information)
    .MinimumLevel.Override(nameof(System), LogEventLevel.Warning)
    .MinimumLevel.Override(nameof(Microsoft), LogEventLevel.Warning)
    // Keep ByteSize as a value so message formats like {Size:G2} apply instead of a pre-rendered ToString().
    .Destructure.AsScalar<ByteSize>()
    .Enrich.FromLogContext()
    .Enrich.WithMemoryUsage()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Async(a =>
    {
        // Redirected output gets no live display and keeps Serilog's own console formatting.
        if (!liveConsole)
        {
            a.Console(theme: AnsiConsoleTheme.Code, formatProvider: CultureInfo.InvariantCulture);
        }

        a.Debug(formatter: new CompactJsonFormatter());
    });

if (liveConsole)
{
    // Synchronous: queued through Async, lines trail the bars and print after they have already finished.
    logging.WriteTo.Sink(new SpectreConsoleSink(AnsiConsole.Console, CultureInfo.InvariantCulture));
}

Log.Logger = logging.CreateLogger();

try
{
    // Exceptions reach the catch blocks below so they are logged through Serilog, not printed raw.
    return await new ConverterCommand()
        .Parse(args)
        .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (OperationCanceledException)
{
    Log.Warning("Cancelled");
    return 130;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Conversion failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
