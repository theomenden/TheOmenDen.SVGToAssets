using SvgToAssets.Enums;

namespace SvgToAssets.Models;

/// <summary>
/// File names and pixel sizes of WinUI 3 / MSIX visual assets.
/// </summary>
/// <remarks>
/// Sizes follow https://learn.microsoft.com/windows/apps/design/iconography/app-icon-construction.
/// </remarks>
internal static class WinUiAssetCatalog
{
    /// <summary>Icons ship their own design padding, so they fill the canvas.</summary>
    private const float Fill = 1f;

    /// <summary>Tiles and the splash screen center the artwork at half the canvas.</summary>
    private const float TileContent = 0.5f;

    private static readonly int[] Scales = [100, 125, 150, 200, 400];

    /// <summary>The minimum icon sizes Windows recommends.</summary>
    public static readonly int[] StandardIconSizes = [16, 24, 32, 48, 256];

    /// <summary>Every size Windows may request, for pixel-perfect icons at all scale factors.</summary>
    public static readonly int[] AllIconSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];

    private static readonly AssetSpec[] BasicAssets =
    [
        new("LockScreenLogo.scale-200.png", 48, 48, Fill),
        new("SplashScreen.scale-200.png", 1240, 600, TileContent),
        new("Square150x150Logo.scale-200.png", 300, 300, TileContent),
        new("Square44x44Logo.scale-200.png", 88, 88, Fill),
        new("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24, Fill),
        new("StoreLogo.png", 50, 50, Fill),
        new("Wide310x150Logo.scale-200.png", 620, 300, TileContent),
    ];

    private static readonly AssetSpec[] RequiredAssets =
    [
        .. Scaled("Square44x44Logo", 44, 44, Fill),
        .. AllIconSizes.SelectMany(size => new AssetSpec[]
        {
            new($"Square44x44Logo.targetsize-{size}.png", size, size, Fill),
            new($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size, size, Fill),
        }),
        .. Scaled("Square150x150Logo", 150, 150, TileContent),
        .. Scaled("Wide310x150Logo", 310, 150, TileContent),
        .. Scaled("StoreLogo", 50, 50, Fill),
        .. Scaled("SplashScreen", 620, 300, TileContent),
    ];

    private static readonly AssetSpec[] OptionalAssets =
    [
        .. Scaled("Square71x71Logo", 71, 71, TileContent),
        .. Scaled("Square310x310Logo", 310, 310, TileContent),
        .. Scaled("LockScreenLogo", 24, 24, Fill),
    ];

    /// <summary>
    /// Gets the PNG assets that belong to <paramref name="category"/>.
    /// </summary>
    public static IReadOnlyList<AssetSpec> GetAssets(AssetCategory category) => category switch
    {
        AssetCategory.Basic => BasicAssets,
        AssetCategory.Required => RequiredAssets,
        AssetCategory.Optional => OptionalAssets,
        AssetCategory.All => [.. RequiredAssets, .. OptionalAssets],
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, message: null),
    };

    /// <summary>
    /// Gets the sizes embedded in <c>AppIcon.ico</c> for <paramref name="category"/>.
    /// </summary>
    public static int[] GetIconSizes(AssetCategory category) =>
        category is AssetCategory.All ? AllIconSizes : StandardIconSizes;

    private static IEnumerable<AssetSpec> Scaled(string baseName, int width, int height, float contentScale) =>
        Scales.Select(scale => new AssetSpec(
            $"{baseName}.scale-{scale}.png",
            ScaleDimension(width, scale),
            ScaleDimension(height, scale),
            contentScale));

    // Away-from-zero matches the documented table: 150 @ 125% = 188, 50 @ 125% = 63.
    private static int ScaleDimension(int baseSize, int scale) =>
        (int)Math.Round(baseSize * scale / 100d, MidpointRounding.AwayFromZero);
}
