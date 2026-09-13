using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using DotNext.Threading;
using Meziantou.Framework;
using Serilog;
using Serilog.Events;
using Spectre.Console;
using SvgToAssets.Enums;
using SvgToAssets.Generators;
using SvgToAssets.Managers;
using SvgToAssets.Models;

namespace SvgToAssets.Commands;

/// <summary>
/// <c>SvgToAssets &lt;svg|glob&gt;... [--output dir] [--type Icon|Assets|All] [--category Basic|Required|Optional|All]</c>
/// </summary>
internal sealed class ConverterCommand : RootCommand
{
    private const string IconFileName = "AppIcon.ico";

    private readonly Argument<SvgInput[]> _svgArgument = new("svg")
    {
        Description = "SVG files or glob patterns (e.g. icons/**/*.svg). "
                      + "When several SVGs are converted, each gets an output subfolder named after it (e.g. dark/App Logo.svg -> dark-app-logo).",
        Arity = ArgumentArity.OneOrMore,
        CustomParser = static result => SvgInputResolver.Resolve(result.Tokens.Select(t => t.Value), FullPath.CurrentDirectory(), result.AddError),
    };

    private readonly Option<FullPath> _outputOption = new("--output", "-o")
    {
        Description = "Directory to write the assets to; created if missing.",
        CustomParser = ParseFullPath,
        DefaultValueFactory = _ => FullPath.CurrentDirectory() / "Assets",
    };

    private readonly Option<AssetsType> _typeOption = new("--type", "-t")
    {
        Description = $"What to generate: the PNG assets, {IconFileName}, or both.",
        DefaultValueFactory = _ => AssetsType.All,
    };

    private readonly Option<AssetCategory> _categoryOption = new("--category", "-c")
    {
        Description = "Basic: the WinUI template's 7 files. Required: every scale of the assets the default manifest references. "
                      + $"Optional: the unreferenced tiles. All: Required + Optional and a 14-size {IconFileName}.",
        DefaultValueFactory = _ => AssetCategory.Required,
    };

    public ConverterCommand() : base("Converts SVG images into the visual assets of a WinUI 3 app.")
    {
        Arguments.Add(_svgArgument);
        Options.Add(_outputOption);
        Options.Add(_typeOption);
        Options.Add(_categoryOption);
        SetAction(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var inputs = parseResult.GetRequiredValue(_svgArgument);
        var outputRoot = parseResult.GetRequiredValue(_outputOption);
        var type = parseResult.GetValue(_typeOption);
        var category = parseResult.GetValue(_categoryOption);

        var stopwatch = Stopwatch.StartNew();
        var totalSize = new Atomic<ByteSize>();
        var failures = new ConcurrentBag<SvgInput>();

        // One step per written file.
        var steps = (type.HasFlag(AssetsType.Assets) ? WinUiAssetCatalog.GetAssets(category).Count : 0)
                    + (type.HasFlag(AssetsType.Icon) ? 1 : 0);

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn(), new SpinnerColumn())
            .StartAsync(async context =>
            {
                // Every bar is added up front, so SVGs still waiting for a worker show as queued.
                var work = inputs.Select(input => (Input: input, Task: context.AddTask(Status(input, "grey", '·'), autoStart: false, steps))).ToList();

                // Each SVG has its own document and lock, so separate files render fully in parallel.
                await Parallel.ForEachAsync(work, cancellationToken, async (item, ct) =>
                {
                    var outputDirectory = inputs.Length == 1 ? outputRoot : outputRoot / item.Input.FolderName;
                    if (await ConvertWithStatusAsync(item.Input, outputDirectory, type, category, item.Task, ct) is { } size)
                    {
                        totalSize.Accumulate(size);
                    }
                    else
                    {
                        failures.Add(item.Input);
                    }
                });
            });

        if (inputs.Length > 1)
        {
            Log.Write(
                failures.IsEmpty ? LogEventLevel.Information : LogEventLevel.Warning,
                "Converted {Converted} of {Total} SVGs ({Size:G2}) into {OutputDirectory} in {Elapsed:0} ms",
                inputs.Length - failures.Count, inputs.Length, totalSize.Value, outputRoot, stopwatch.Elapsed.TotalMilliseconds);
        }

        return failures.IsEmpty ? 0 : 1;
    }

    /// <summary>
    /// Converts one SVG while its progress bar shows whether it is running, done, failed, or cancelled.
    /// </summary>
    /// <returns>The size of the written files, or <see langword="null"/> when the conversion failed and was logged.</returns>
    private static async Task<ByteSize?> ConvertWithStatusAsync(
        SvgInput input, FullPath outputDirectory, AssetsType type, AssetCategory category, ProgressTask task, CancellationToken cancellationToken)
    {
        task.Description = Status(input, "yellow", '›');
        task.StartTask();
        try
        {
            var size = await ConvertAsync(input.Path, outputDirectory, type, category, task, cancellationToken);
            task.Description = Status(input, "green", '✓');
            return size;
        }
        catch (OperationCanceledException)
        {
            task.Description = Status(input, "grey", '○');
            throw;
        }
        catch (Exception ex)
        {
            // One bad SVG shouldn't abort the rest of the batch.
            Log.Error(ex, "Failed to convert {SvgPath}", input.Path);
            task.Description = Status(input, "red", '✗');
            return null;
        }
        finally
        {
            task.StopTask();
        }
    }

    private static async Task<ByteSize> ConvertAsync(
        FullPath svgPath, FullPath outputDirectory, AssetsType type, AssetCategory category, ProgressTask progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("Reading {SvgPath} ({Size})", svgPath, ByteSize.FromFileLength(svgPath));

        using var rasterizer = SvgRasterizer.Open(svgPath);
        Directory.CreateDirectory(outputDirectory);

        var fileCount = 0;
        var totalSize = ByteSize.Zero;

        if (type.HasFlag(AssetsType.Assets))
        {
            var assets = WinUiAssetCatalog.GetAssets(category);
            Log.Information("Generating {Count} {Category} assets for {Svg}", assets.Count, category, svgPath.Name);

            totalSize = totalSize.Add(await new AssetsGenerator(rasterizer).GenerateAsync(assets, outputDirectory, new StepProgress(progress), cancellationToken));
            fileCount += assets.Count;
        }

        if (type.HasFlag(AssetsType.Icon))
        {
            var sizes = WinUiAssetCatalog.GetIconSizes(category);
            Log.Information("Generating {IconFileName} for {Svg} with sizes {Sizes}", IconFileName, svgPath.Name, sizes);

            await using var icon = new IconGenerator(rasterizer).CreateIcon(sizes);
            totalSize = totalSize.Add(await icon.WriteToFileAsync(outputDirectory / IconFileName, cancellationToken));
            progress.Increment(1);
            fileCount++;
        }

        Log.Information(
            "Wrote {FileCount} files ({Size:G2}) for {Svg} to {OutputDirectory} in {Elapsed:0} ms",
            fileCount, totalSize, svgPath.Name, outputDirectory, stopwatch.Elapsed.TotalMilliseconds);

        return totalSize;
    }

    /// <summary>
    /// A progress bar label: the SVG's path relative to the working directory, colored and marked by its state.
    /// </summary>
    private static string Status(SvgInput input, string style, char glyph) =>
        $"[{style}]{glyph} {Markup.Escape(input.Path.MakePathRelativeTo(FullPath.CurrentDirectory()))}[/]";

    /// <summary>
    /// Resolves a command-line token to an absolute path, reporting malformed paths as parse errors.
    /// </summary>
    private static FullPath ParseFullPath(ArgumentResult result)
    {
        var token = result.Tokens[0].Value;
        try
        {
            return FullPath.FromPath(token);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.AddError($"'{token}' is not a valid path: {ex.Message}");
            return FullPath.Empty;
        }
    }

    /// <summary>
    /// Advances a progress bar one step per written asset. Reports synchronously, unlike <see cref="Progress{T}"/>,
    /// so the bar is full by the time the generator returns.
    /// </summary>
    private sealed class StepProgress(ProgressTask task) : IProgress<AssetSpec>
    {
        public void Report(AssetSpec value) => task.Increment(1);
    }
}
