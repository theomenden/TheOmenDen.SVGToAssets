using DotNext.Threading;
using Meziantou.Framework;
using Serilog;
using SvgToAssets.Managers;
using SvgToAssets.Models;

namespace SvgToAssets.Generators;

/// <summary>
/// Renders MSIX visual asset PNGs into a directory.
/// </summary>
internal sealed class AssetsGenerator(SvgRasterizer rasterizer)
{
    /// <summary>
    /// Renders and writes every asset in <paramref name="assets"/> in parallel, overwriting existing files,
    /// and reports each asset to <paramref name="progress"/> (concurrently) once its file is written.
    /// </summary>
    /// <returns>The combined size of the written files.</returns>
    public async ValueTask<ByteSize> GenerateAsync(
        IEnumerable<AssetSpec> assets, FullPath outputDirectory, IProgress<AssetSpec>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assets);

        Directory.CreateDirectory(outputDirectory);

        // Atomic<T> gives atomic updates to a non-primitive struct, so the total stays a ByteSize throughout.
        var totalSize = new Atomic<ByteSize>();

        // Drawing is serialized by the rasterizer's document lock; PNG encoding and file I/O run in parallel.
        await Parallel.ForEachAsync(assets, cancellationToken, async (asset, ct) =>
        {
            await using var png = await rasterizer.RenderPngAsync(asset.Width, asset.Height, asset.ContentScale, ct);
            var size = await png.WriteToFileAsync(outputDirectory / asset.FileName, ct);
            totalSize.Accumulate(size);
            progress?.Report(asset);

            Log.Debug("Wrote {FileName} ({Width}x{Height}, {Size:G2})", asset.FileName, asset.Width, asset.Height, size);
        });

        WarnAboutMrtConflicts(outputDirectory);
        return totalSize.Value;
    }

    /// <summary>
    /// MakePri rejects an unqualified <c>Name.png</c> next to <c>Name.scale-*.png</c> (both mean scale-100),
    /// which happens when a full set is generated over the template's Assets folder, or vice versa.
    /// </summary>
    private static void WarnAboutMrtConflicts(FullPath outputDirectory)
    {
        foreach (var unqualified in Directory.EnumerateFiles(outputDirectory, "*.png").Select(FullPath.FromPath))
        {
            var baseName = unqualified.NameWithoutExtension;
            if (baseName.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.EnumerateFiles(outputDirectory, $"{baseName}.scale-*.png").Any())
            {
                Log.Warning(
                    "{File} conflicts with {BaseName}.scale-*.png in {Directory}; delete one or MakePri will fail",
                    unqualified.Name, baseName, outputDirectory);
            }
        }
    }
}
