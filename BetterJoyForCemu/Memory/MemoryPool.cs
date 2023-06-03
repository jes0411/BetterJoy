using System;
using System.Buffers;

namespace BetterJoyForCemu.Memory {
    // For conveniance, Rented memory from System.Buffer.MemoryPool have a length attribute can be bigger than what we want
    public sealed partial class MemoryPool<T> {

        public static MemoryPool<T> Shared { get; } = new MemoryPool<T>();
        public static int MaxBufferSize => Array.MaxLength;

        private MemoryPool() { }

        public IMemoryOwner<T> Rent(int length) {
            return RentImpl(length);
        }

        public IMemoryOwner<T> RentCleared(int length) {
            var buffer = RentImpl(length);
            buffer.Memory.Span.Clear();

            return buffer;
        }

        private static MemoryPoolBuffer<T> RentImpl(int length) {
            if (length > MaxBufferSize) {
                throw new ArgumentOutOfRangeException(nameof(length), length, null);
            }
            return new MemoryPoolBuffer<T>(length);
        }
    }
}
