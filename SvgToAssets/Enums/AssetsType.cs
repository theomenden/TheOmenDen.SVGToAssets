namespace SvgToAssets.Enums;

/// <summary>
/// Which outputs to generate.
/// </summary>
[Flags]
public enum AssetsType : byte
{
    /// <summary>
    /// <c>AppIcon.ico</c> for the window title bar and taskbar.
    /// </summary>
    Icon = 1,

    /// <summary>
    /// The MSIX visual asset PNGs referenced by <c>Package.appxmanifest</c>.
    /// </summary>
    Assets = 2,

    /// <summary>
    /// Both the icon and the PNG assets.
    /// </summary>
    All = Icon | Assets,
}
