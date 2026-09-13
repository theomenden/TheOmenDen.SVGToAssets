using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DotNext.Threading;
using Meziantou.Framework;
using Microsoft.IO;
using Svg;
using SvgToAssets.Managers;

namespace SvgToAssets.Generators;

/// <summary>
/// Renders one SVG document to PNGs of arbitrary canvas sizes.
/// </summary>
/// <remarks>
/// Safe to call concurrently: rendering resizes the shared document, so each resize-and-draw
/// runs under an <see cref="AsyncExclusiveLock"/>. PNG encoding happens outside the lock.
/// </remarks>
internal sealed class SvgRasterizer : IDisposable
{
    private readonly SvgDocument _document;
    private readonly AsyncExclusiveLock _documentLock = new();

    /// <summary>
    /// Wraps <paramref name="document"/>, giving it a view box when it has none so it scales instead of clipping.
    /// </summary>
    public SvgRasterizer(SvgDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.ViewBox.Width <= 0 || document.ViewBox.Height <= 0)
        {
            var size = document.GetDimensions();
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new InvalidDataException("The SVG has neither a viewBox nor a usable width and height.");
            }

            document.ViewBox = new SvgViewBox(0, 0, size.Width, size.Height);
        }

        _document = document;
    }

    /// <summary>
    /// Loads an SVG file. External images and elements may only come from the local file system.
    /// </summary>
    public static SvgRasterizer Open(FullPath path)
    {
        SvgDocument.ResolveExternalImages = ExternalType.Local;
        SvgDocument.ResolveExternalElements = ExternalType.Local;

        return new SvgRasterizer(SvgDocument.Open(path));
    }

    /// <summary>
    /// Renders the artwork centered on a transparent <paramref name="width"/> x <paramref name="height"/> canvas,
    /// preserving its aspect ratio within <paramref name="contentScale"/> of the canvas.
    /// </summary>
    /// <returns>A pooled stream holding the PNG, positioned at 0. The caller owns it.</returns>
    public async ValueTask<RecyclableMemoryStream> RenderPngAsync(
        int width, int height, float contentScale = 1f, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contentScale, 1f);

        // Whole-pixel box and offset keep edges crisp at small sizes.
        var boxWidth = Math.Max(1, (int)MathF.Round(width * contentScale));
        var boxHeight = Math.Max(1, (int)MathF.Round(height * contentScale));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        // Width/Height are shared document state: resizing and drawing must be one atomic step.
        await _documentLock.AcquireAsync(cancellationToken);
        try
        {
            // The SVG's own preserveAspectRatio (default xMidYMid meet) fits and centers the view box in this box.
            _document.Width = new SvgUnit(SvgUnitType.Pixel, boxWidth);
            _document.Height = new SvgUnit(SvgUnitType.Pixel, boxHeight);

            using var renderer = SvgRenderer.FromImage(bitmap);
            renderer.TranslateTransform((width - boxWidth) / 2, (height - boxHeight) / 2, MatrixOrder.Append);
            _document.Draw(renderer);
        }
        finally
        {
            _documentLock.Release();
        }

        // Encoding only touches this call's bitmap.
        var png = PooledStreamManager.GetStream($"{width}x{height}.png");
        try
        {
            bitmap.Save(png, ImageFormat.Png);
            png.Position = 0;
            return png;
        }
        catch
        {
            await png.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _documentLock.Dispose();
}
