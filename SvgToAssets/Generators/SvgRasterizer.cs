using Meziantou.Framework;
using Microsoft.IO;
using SkiaSharp;
using Svg;
using Svg.Skia;
using SvgToAssets.Managers;

namespace SvgToAssets.Generators;

/// <summary>
/// Renders one SVG document to PNGs of arbitrary canvas sizes.
/// </summary>
/// <remarks>
/// Safe to call concurrently: the SVG is recorded once into an immutable <see cref="SKPicture"/>,
/// which each render plays back onto its own bitmap.
/// </remarks>
internal sealed class SvgRasterizer : IDisposable
{
    private readonly SKSvg _svg;
    private readonly SKPicture _picture;

    /// <summary>
    /// Takes ownership of the loaded <paramref name="svg"/>.
    /// </summary>
    public SvgRasterizer(SKSvg svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        // Svg.Skia sizes the picture from width/height, else the view box, else the content bounds.
        if (svg.Picture is not { CullRect: { Width: > 0, Height: > 0 } } picture)
        {
            throw new InvalidDataException("The SVG has nothing to render.");
        }

        _svg = svg;
        _picture = picture;
    }

    /// <summary>
    /// Loads an SVG file. External images and elements may only come from the local file system.
    /// </summary>
    public static SvgRasterizer Open(FullPath path)
    {
        SvgDocument.ResolveExternalImages = ExternalType.Local;
        SvgDocument.ResolveExternalElements = ExternalType.Local;

        var svg = new SKSvg();

        // Read at load time: an unresolvable image should leave a gap, not a grey placeholder in every asset.
        svg.Settings.EnableBrokenImagePlaceholders = false;

        try
        {
            svg.Load(path);
            return new SvgRasterizer(svg);
        }
        catch
        {
            svg.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Renders the artwork centered on a transparent <paramref name="width"/> x <paramref name="height"/> canvas,
    /// preserving its aspect ratio within <paramref name="contentScale"/> of the canvas.
    /// </summary>
    /// <returns>A pooled stream holding the PNG, positioned at 0. The caller owns it.</returns>
    public RecyclableMemoryStream RenderPng(int width, int height, float contentScale = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contentScale, 1f);

        // Whole-pixel box and offset keep edges crisp at small sizes.
        var boxWidth = Math.Max(1, (int)MathF.Round(width * contentScale));
        var boxHeight = Math.Max(1, (int)MathF.Round(height * contentScale));

        // Fit and center within the box, as the default preserveAspectRatio (xMidYMid meet) does.
        var bounds = _picture.CullRect;
        var scale = Math.Min(boxWidth / bounds.Width, boxHeight / bounds.Height);

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(
                ((width - boxWidth) / 2) + ((boxWidth - (bounds.Width * scale)) / 2),
                ((height - boxHeight) / 2) + ((boxHeight - (bounds.Height * scale)) / 2));
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(_picture);
        }

        var png = PooledStreamManager.GetStream($"{width}x{height}.png");
        try
        {
            if (!bitmap.Encode(png, SKEncodedImageFormat.Png, quality: 100))
            {
                throw new InvalidOperationException($"Encoding the {width}x{height} PNG failed.");
            }

            png.Position = 0;
            return png;
        }
        catch
        {
            png.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _svg.Dispose();
}
