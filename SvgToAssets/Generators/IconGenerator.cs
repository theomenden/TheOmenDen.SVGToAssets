using DotNext.Buffers.Binary;
using Microsoft.IO;
using SvgToAssets.Managers;

namespace SvgToAssets.Generators;

/// <summary>
/// Builds a multi-resolution <c>.ico</c> whose entries are PNG-compressed (supported since Windows Vista).
/// </summary>
internal sealed class IconGenerator(SvgRasterizer rasterizer)
{
    private const int IconDirSize = 6;
    private const int IconDirEntrySize = 16;
    private const int MaxIconSize = 256;

    /// <summary>
    /// Renders every size and assembles the icon.
    /// </summary>
    /// <returns>A pooled stream holding the icon, positioned at 0. The caller owns it.</returns>
    public async ValueTask<RecyclableMemoryStream> CreateIconAsync(int[] sizes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        if (sizes.Length == 0)
        {
            throw new ArgumentException("At least one size is required.", nameof(sizes));
        }

        var images = new RecyclableMemoryStream[sizes.Length];
        try
        {
            for (var i = 0; i < sizes.Length; i++)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(sizes[i], MaxIconSize, nameof(sizes));
                images[i] = await rasterizer.RenderPngAsync(sizes[i], sizes[i], cancellationToken: cancellationToken);
            }

            var icon = PooledStreamManager.GetStream("AppIcon.ico");
            WriteIcon(icon, sizes, images);
            icon.Position = 0;
            return icon;
        }
        finally
        {
            foreach (var image in images)
            {
                image?.Dispose();
            }
        }
    }

    /// <summary>
    /// Writes ICONDIR, one ICONDIRENTRY per image, then the PNG payloads.
    /// </summary>
    private static void WriteIcon(RecyclableMemoryStream icon, ReadOnlySpan<int> sizes, RecyclableMemoryStream[] images)
    {
        // RecyclableMemoryStream is an IBufferWriter<byte>, so DotNext writes the header straight into its pooled blocks.
        icon.WriteLittleEndian<ushort>(0); // reserved
        icon.WriteLittleEndian<ushort>(1); // type: icon
        icon.WriteLittleEndian((ushort)sizes.Length);

        var offset = (uint)(IconDirSize + (IconDirEntrySize * sizes.Length));
        for (var i = 0; i < sizes.Length; i++)
        {
            var dimension = (byte)(sizes[i] == MaxIconSize ? 0 : sizes[i]); // 0 means 256
            var length = (uint)images[i].Length;

            icon.WriteLittleEndian(dimension); // width
            icon.WriteLittleEndian(dimension); // height
            icon.WriteLittleEndian<byte>(0); // palette colors
            icon.WriteLittleEndian<byte>(0); // reserved
            icon.WriteLittleEndian<ushort>(1); // color planes
            icon.WriteLittleEndian<ushort>(32); // bits per pixel
            icon.WriteLittleEndian(length);
            icon.WriteLittleEndian(offset);

            offset += length;
        }

        foreach (var image in images)
        {
            image.WriteTo(icon);
        }
    }
}
