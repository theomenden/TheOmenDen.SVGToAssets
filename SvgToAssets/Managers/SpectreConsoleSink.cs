using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Spectre.Console;

namespace SvgToAssets.Managers;

/// <summary>
/// Writes log events through a Spectre console, so they print above its live progress bars instead of tearing them.
/// </summary>
internal sealed class SpectreConsoleSink(IAnsiConsole console, IFormatProvider? formatProvider) : ILogEventSink
{
    private const string PropertyStyle = "aqua";

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var (level, levelStyle) = logEvent.Level switch
        {
            LogEventLevel.Verbose => ("VRB", "grey"),
            LogEventLevel.Debug => ("DBG", "grey"),
            LogEventLevel.Information => ("INF", "green"),
            LogEventLevel.Warning => ("WRN", "yellow"),
            LogEventLevel.Error => ("ERR", "red"),
            _ => ("FTL", "white on red"),
        };

        var line = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"[grey]{logEvent.Timestamp:HH:mm:ss}[/] [{levelStyle}]{level}[/] ");

        // Property values stand out from the template text; unmatched properties print as written.
        foreach (var token in logEvent.MessageTemplate.Tokens)
        {
            line.Append(token is PropertyToken property && logEvent.Properties.TryGetValue(property.PropertyName, out var value)
                ? $"[{PropertyStyle}]{Markup.Escape(Render(value, property.Format))}[/]"
                : Markup.Escape(token.ToString() ?? string.Empty));
        }

        console.MarkupLine(line.ToString());

        if (logEvent.Exception is not null)
        {
            console.WriteException(logEvent.Exception, ExceptionFormats.ShortenPaths);
        }
    }

    // Strings print unquoted, like Serilog's default {Message:lj} console output.
    private string Render(LogEventPropertyValue value, string? format) =>
        value is ScalarValue { Value: string text } ? text : value.ToString(format, formatProvider);
}
