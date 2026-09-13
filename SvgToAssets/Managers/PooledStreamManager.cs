using DotNext.IO;
using Meziantou.Framework;
using Microsoft.IO;

namespace SvgToAssets.Managers;

/// <summary>
/// Process-wide <see cref="RecyclableMemoryStreamManager"/> for encoded images.
/// </summary>
internal static class PooledStreamManager
{
    private static readonly RecyclableMemoryStreamManager StreamManager = new(new RecyclableMemoryStreamManager.Options
    {
        ThrowExceptionOnToArray = true,
        AggressiveBufferReturn = true,
    });

    /// <summary>
    /// Rents a pooled stream; dispose it to return its buffers.
    /// </summary>
    public static RecyclableMemoryStream GetStream(string tag) => StreamManager.GetStream(tag);

    /// <summary>
    /// Writes the whole stream to <paramref name="path"/> segment by segment, without copying into a contiguous buffer.
    /// </summary>
    /// <returns>The size of the written file.</returns>
    public static async ValueTask<ByteSize> WriteToFileAsync(this RecyclableMemoryStream stream, FullPath path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous,
            BufferSize = 0, // segments are already buffered; write them straight through
            PreallocationSize = stream.Length,
        });

        await file.WriteAsync(stream.GetReadOnlySequence(), cancellationToken);
        return ByteSize.FromBytes(stream.Length);
    }
}
