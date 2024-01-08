using System;
using System.Buffers;
using System.Threading;

namespace BetterJoy.Memory
{
    public sealed partial class MemoryPool<T>
    {
        private sealed class MemoryPoolBuffer<U> : IMemoryOwner<U>
        {
            private readonly int _length;
            private U[] _array;

            public MemoryPoolBuffer(int length)
            {
                _array = ArrayPool<U>.Shared.Rent(length);
                _length = length;
            }

            public Memory<U> Memory
            {
                get
                {
                    var array = _array;

                    ObjectDisposedException.ThrowIf(array is null, this);

                    return new Memory<U>(array, 0, _length);
                }
            }

            public void Dispose()
            {
                var array = Interlocked.Exchange(ref _array, null);

                if (array != null)
                {
                    ArrayPool<U>.Shared.Return(array);
                }
            }
        }
    }
}
