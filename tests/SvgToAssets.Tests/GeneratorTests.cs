using System.Buffers.Binary;
using System.CommandLine;
using System.Drawing;
using System.Globalization;
using Bogus;
using Bogus.Text;
using Meziantou.Framework;
using Serilog;
using Shouldly;
using Spectre.Console;
using Svg;
using SvgToAssets.Commands;
using SvgToAssets.Enums;
using SvgToAssets.Generators;
using SvgToAssets.Managers;
using SvgToAssets.Models;
using Xunit;

namespace SvgToAssets.Tests;

public sealed class GeneratorTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Square44x44Logo.scale-125.png", 55, 55)]
    [InlineData("Square150x150Logo.scale-125.png", 188, 188)]
    [InlineData("Wide310x150Logo.scale-125.png", 388, 188)]
    [InlineData("StoreLogo.scale-125.png", 63, 63)]
    [InlineData("Square44x44Logo.targetsize-256_altform-unplated.png", 256, 256)]
    public void RequiredAssets_ShouldMatchDocumentedSizes_WhenLookedUpInCatalog(string fileName, int width, int height)
    {
        // Act
        var assets = WinUiAssetCatalog.GetAssets(AssetCategory.Required);

        // Assert
        assets.Where(a => a.FileName == fileName).Select(a => (a.Width, a.Height)).ShouldBe([(width, height)]);
    }

    [Theory]
    [InlineData(AssetCategory.Basic)]
    [InlineData(AssetCategory.All)]
    public void AssetCategory_ShouldNotMixUnqualifiedAndScaledVariants_WhenAssetsAreListed(AssetCategory category)
    {
        // Act
        var names = WinUiAssetCatalog.GetAssets(category).Select(a => a.FileName).ToList();

        // Assert
        names.ShouldSatisfy(
        [
            n => n.ShouldBeUnique(StringComparer.OrdinalIgnoreCase),
            n => n.Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal).ShouldAllBe(
                baseName => !(n.Contains($"{baseName}.png") && n.Any(other => other.StartsWith($"{baseName}.scale-", StringComparison.Ordinal))))
        ]);
    }

    [Fact]
    public async Task SvgRasterizer_ShouldRenderEachSizeFullBleed_WhenRendersRunConcurrently()
    {
        // Arrange
        var faker = CreateFaker();
        var sizes = faker.Make(128, () => faker.Random.Int(16, 256));
        using var rasterizer = CreateRasterizer(faker);

        // Act
        var renders = await Task.WhenAll(sizes.Select(size => Task.Run(async () =>
        {
            await using var png = await rasterizer.RenderPngAsync(size, size, cancellationToken: CancellationToken);
            using var bitmap = new Bitmap(png);

            // The test SVG is one full-bleed rect: a render sized for another call leaves a transparent corner.
            return (Size: size, Opaque: bitmap.GetPixel(0, 0).A == 255 && bitmap.GetPixel(size - 1, size - 1).A == 255);
        })));

        // Assert
        renders.ShouldAllBe(r => r.Opaque);
    }

    [Fact]
    public async Task IconGenerator_ShouldPointDirectoryAtPngEntriesOfRequestedSizes_WhenCreatingIcon()
    {
        // Arrange
        var faker = CreateFaker();
        int[] sizes = [.. faker.Make(faker.Random.Int(1, 4), () => faker.Random.Int(16, 255)), 256];
        using var rasterizer = CreateRasterizer(faker);
        var generator = new IconGenerator(rasterizer);

        // Act
        await using var icon = await generator.CreateIconAsync(sizes, CancellationToken);
        var bytes = new byte[icon.Length];
        icon.ReadExactly(bytes);

        // Assert
        bytes.ShouldSatisfy(
        [
            b => BinaryPrimitives.ReadUInt16LittleEndian(b).ShouldBe((ushort)0),
            b => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2)).ShouldBe((ushort)1),
            b => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)).ShouldBe((ushort)sizes.Length),
            b => ReadIconEntries(b).Select(e => e.Width).ShouldBe(sizes.Select(size => (byte)(size % 256))), // 256 is stored as 0
            b => ReadIconEntries(b).Select(e => e.PngSize).ShouldBe(sizes.Select(size => (size, size))),
            b => ReadIconEntries(b)[^1].End.ShouldBe(b.Length)
        ]);
    }

    [Fact]
    public async Task AssetsGenerator_ShouldWriteExactPixelSizesAndReportTotalSize_WhenGeneratingBasicAssets()
    {
        // Arrange
        var faker = CreateFaker();
        await using var directory = TemporaryDirectory.Create();
        var assets = WinUiAssetCatalog.GetAssets(AssetCategory.Basic);
        using var rasterizer = CreateRasterizer(faker);
        var generator = new AssetsGenerator(rasterizer);

        // Act
        var totalSize = await generator.GenerateAsync(assets, directory.FullPath, progress: null, CancellationToken);

        // Assert
        Should.Satisfy(
        [
            () => assets.Select(asset => ReadPngSize(File.ReadAllBytes(directory / asset.FileName))).ShouldBe(assets.Select(asset => (asset.Width, asset.Height))),
            () => totalSize.ShouldBe(assets.Aggregate(ByteSize.Zero, (sum, asset) => sum.Add(ByteSize.FromFileLength(directory / asset.FileName))))
        ]);
    }

    [Fact]
    public async Task ConverterCommand_ShouldWriteRequestedSetToOutputDirectory_WhenGivenSingleSvg()
    {
        // Arrange
        var faker = CreateFaker();
        await using var directory = TemporaryDirectory.Create();
        var svgPath = directory / $"{faker.Lorem.Word()}.svg";
        await WriteSvgAsync(faker, svgPath);
        var outputDirectory = directory / faker.Lorem.Word() / faker.Lorem.Word();
        var expectedFiles = WinUiAssetCatalog.GetAssets(AssetCategory.Basic).Select(a => a.FileName).Append("AppIcon.ico");
        var command = new ConverterCommand();

        // Act
        var exitCode = await command
            .Parse([svgPath, "--output", outputDirectory, "--category", nameof(AssetCategory.Basic)])
            .InvokeAsync(cancellationToken: CancellationToken);

        // Assert
        Should.Satisfy(
        [
            () => exitCode.ShouldBe(0),
            () => Directory.EnumerateFiles(outputDirectory).Select(file => FullPath.FromPath(file).Name).ShouldBe(expectedFiles, ignoreOrder: true)
        ]);
    }

    [Fact]
    public async Task ConverterCommand_ShouldConvertEachMatchIntoSlugNamedFolder_WhenGivenGlob()
    {
        // Arrange
        var faker = CreateFaker();
        await using var source = TemporaryDirectory.Create();
        await using var output = TemporaryDirectory.Create(); // Separate, so a random folder name can never land inside the source tree.
        var artDirectory = source / faker.Lorem.Word();
        var subfolder = faker.Lorem.Word();
        var (svgFileName, svgSlug) = CreateSvgName(faker);
        await WriteSvgAsync(faker, artDirectory / svgFileName);
        await WriteSvgAsync(faker, artDirectory / subfolder / svgFileName);
        var expectedFileCount = WinUiAssetCatalog.GetAssets(AssetCategory.Basic).Count + 1;
        var command = new ConverterCommand();

        // Act
        var exitCode = await command
            .Parse([artDirectory / "**" / "*.svg", "-o", output.FullPath, "-c", nameof(AssetCategory.Basic)])
            .InvokeAsync(cancellationToken: CancellationToken);

        // Assert
        Should.Satisfy(
        [
            () => exitCode.ShouldBe(0),
            () => Directory.EnumerateDirectories(output.FullPath).Select(dir => FullPath.FromPath(dir).Name).ShouldBe([svgSlug, $"{subfolder}-{svgSlug}"], ignoreOrder: true),
            () => Directory.EnumerateDirectories(output.FullPath).ShouldAllBe(dir => Directory.EnumerateFiles(dir).Count() == expectedFileCount)
        ]);
    }

    [Fact]
    public async Task SvgInputResolver_ShouldDeduplicateMatchesAndNameFoldersFromPathBelowGlob_WhenInputsOverlap()
    {
        // Arrange
        var faker = CreateFaker();
        await using var directory = TemporaryDirectory.Create();
        var iconsFolder = faker.Lorem.Word();
        var subfolder = faker.Lorem.Word();
        var (svgFileName, svgSlug) = CreateSvgName(faker);
        await WriteSvgAsync(faker, directory / iconsFolder / svgFileName);
        await WriteSvgAsync(faker, directory / iconsFolder / subfolder / svgFileName);
        await File.WriteAllTextAsync(directory / iconsFolder / $"{faker.Lorem.Word()}.txt", faker.Literature().CommonSense(), CancellationToken);
        var errors = new List<string>();

        // Act
        // An absolute glob plus a relative literal path that names one of its matches again.
        var inputs = SvgInputResolver.Resolve([directory / iconsFolder / "**" / "*.svg", $"{iconsFolder}/{svgFileName}"], directory.FullPath, errors.Add);

        // Assert
        Should.Satisfy(
        [
            () => errors.ShouldBeEmpty(),
            () => inputs.Select(i => i.FolderName).ShouldBe([svgSlug, $"{subfolder}-{svgSlug}"], ignoreOrder: true)
        ]);
    }

    [Fact]
    public async Task SvgInputResolver_ShouldReportEveryError_WhenFilesAreMissingGlobsUnmatchedOrFoldersCollide()
    {
        // Arrange
        var faker = CreateFaker();
        await using var directory = TemporaryDirectory.Create();
        var (svgFileName, svgSlug) = CreateSvgName(faker);
        await WriteSvgAsync(faker, directory / svgFileName);
        await WriteSvgAsync(faker, directory / $"{svgSlug}.svg");
        var missingFileName = $"{faker.Lorem.Word()}.svg";
        var unmatchedPattern = $"{faker.Lorem.Word()}/*.svg";
        var errors = new List<string>();

        // Act
        SvgInputResolver.Resolve(["*.svg", missingFileName, unmatchedPattern], directory.FullPath, errors.Add);

        // Assert
        errors.ShouldSatisfy(
        [
            e => e.ShouldHaveCount(3),
            e => e.ShouldContain(error => error.Contains($"'{svgSlug}' output folder", StringComparison.Ordinal)),
            e => e.ShouldContain(error => error.Contains(missingFileName, StringComparison.Ordinal)),
            e => e.ShouldContain(error => error.Contains(unmatchedPattern, StringComparison.Ordinal))
        ]);
    }

    [Fact]
    public async Task ConverterCommand_ShouldReportParseError_WhenSvgIsMissing()
    {
        // Arrange
        var faker = CreateFaker();
        await using var directory = TemporaryDirectory.Create();
        var missingFileName = $"{faker.Lorem.Word()}.svg";
        var command = new ConverterCommand();

        // Act
        var parseResult = command.Parse([directory / missingFileName]);

        // Assert
        parseResult.Errors.ShouldContain(error => error.Message.Contains(missingFileName, StringComparison.Ordinal));
    }

    [Fact]
    public void SpectreConsoleSink_ShouldWriteLevelAndFormattedPropertiesAsLiteralText_WhenMessageContainsMarkupBrackets()
    {
        // Arrange
        var faker = CreateFaker();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output) });
        var path = $"[{faker.Lorem.Word()}]/{faker.System.FileName("svg")}"; // Unescaped, Spectre would parse the brackets as a style and throw.
        var size = ByteSize.FromBytes(faker.Random.Long(1, 10_000_000));
        using var logger = new LoggerConfiguration()
            .Destructure.AsScalar<ByteSize>() // As in Program.cs, so {Size:G2} formats the value.
            .WriteTo.Sink(new SpectreConsoleSink(console, CultureInfo.InvariantCulture))
            .CreateLogger();

        // Act
        logger.Warning("Wrote [{Path}] ({Size:G2})", path, size);

        // Assert
        output.ToString().TrimEnd().ShouldEndWith($"WRN Wrote [{path}] ({size.ToString("G2", CultureInfo.InvariantCulture)})");
    }

    private static Faker CreateFaker()
    {
        // Fresh data every run; the seed is logged so a failure can be replayed with `new Randomizer(seed)`.
        var seed = Random.Shared.Next();
        TestContext.Current.TestOutputHelper?.WriteLine($"Bogus seed: {seed}");
        return new Faker { Random = new Randomizer(seed) };
    }

    /// <summary>One opaque rect filling a square view box, so every square render is covered corner to corner.</summary>
    private static string CreateSvg(Faker faker)
    {
        var side = faker.Random.Int(16, 1024);
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {side} {side}"><rect width="{side}" height="{side}" fill="{faker.Internet.Color()}"/></svg>""";
    }

    /// <summary>A title-cased, space-separated file name such as <c>Dolor Amet.svg</c>, and the folder slug it maps to (<c>dolor-amet</c>).</summary>
    /// <remarks>Lorem words are lowercase ASCII, so joining them with hyphens is exactly the slug.</remarks>
    private static (string FileName, string Slug) CreateSvgName(Faker faker)
    {
        var words = faker.Lorem.Words(2);
        return ($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(' ', words))}.svg", string.Join('-', words));
    }

    private static SvgRasterizer CreateRasterizer(Faker faker) => new(SvgDocument.FromSvg<SvgDocument>(CreateSvg(faker)));

    private static async Task WriteSvgAsync(Faker faker, FullPath path)
    {
        path.CreateParentDirectory();
        await File.WriteAllTextAsync(path, CreateSvg(faker), CancellationToken);
    }

    // ICONDIR is 6 bytes (image count at 4), then one 16-byte ICONDIRENTRY per image: width at 0, PNG length at 8, PNG offset at 12.
    private static List<(byte Width, (int Width, int Height) PngSize, int End)> ReadIconEntries(byte[] icon) =>
        Enumerable.Range(0, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4)))
            .Select(i =>
            {
                var entry = icon.AsSpan(6 + (16 * i), 16);
                var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
                var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
                return (entry[0], ReadPngSize(icon.AsSpan(offset, length)), offset + length);
            })
            .ToList();

    private static (int Width, int Height) ReadPngSize(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(PngSignature))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        // IHDR is always first: 8-byte signature, 4-byte chunk length, 4-byte chunk type, then width and height.
        return (BinaryPrimitives.ReadInt32BigEndian(png[16..]), BinaryPrimitives.ReadInt32BigEndian(png[20..]));
    }
}
