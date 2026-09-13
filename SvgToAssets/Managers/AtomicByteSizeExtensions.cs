using DotNext.Threading;
using Meziantou.Framework;

namespace SvgToAssets.Managers;

/// <summary>
/// Thread-safe running totals of <see cref="ByteSize"/>.
/// </summary>
internal static class AtomicByteSizeExtensions
{
    extension(ref Atomic<ByteSize> total)
    {
        /// <summary>
        /// Atomically adds <paramref name="size"/> to the total.
        /// </summary>
        public void Accumulate(ByteSize size) =>
            total.AccumulateAndGet(size, static (ref current, in added) => current = current.Add(added), out _);
    }
}
