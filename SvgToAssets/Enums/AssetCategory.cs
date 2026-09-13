namespace SvgToAssets.Enums;

/// <summary>
/// Which set of PNG assets to generate. The sets are mutually exclusive on purpose:
/// <c>StoreLogo.png</c> (Basic) and <c>StoreLogo.scale-100.png</c> (Required) resolve to the
/// same MRT candidate, and MakePri fails when both are present.
/// </summary>
public enum AssetCategory : byte
{
    /// <summary>
    /// Exactly the seven files shipped by the WinUI 3 project template; a drop-in replacement.
    /// </summary>
    Basic = 0,

    /// <summary>
    /// Every scale and target size of the assets the default WinUI 3 manifest references.
    /// </summary>
    Required,

    /// <summary>
    /// Every scale of the assets the default manifest does not reference (small/large tile, lock screen).
    /// </summary>
    Optional,

    /// <summary>
    /// <see cref="Required"/> and <see cref="Optional"/>, plus a 14-size <c>AppIcon.ico</c>.
    /// </summary>
    All,
}
