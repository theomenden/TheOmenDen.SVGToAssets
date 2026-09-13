namespace SvgToAssets.Models;

/// <summary>
/// One PNG to render.
/// </summary>
/// <param name="FileName">MRT-qualified file name, e.g. <c>Square44x44Logo.scale-200.png</c>.</param>
/// <param name="Width">Canvas width in physical pixels.</param>
/// <param name="Height">Canvas height in physical pixels.</param>
/// <param name="ContentScale">Fraction of the canvas the artwork may occupy; the rest is transparent padding.</param>
internal readonly record struct AssetSpec(string FileName, int Width, int Height, float ContentScale);
