using System;
using System.Buffers;
using System.Threading;

namespace BetterJoy.Memory
{
    public sealed partial class MemoryPool<T>
    {
#pragma warning disable CS0693 // Type parameter has the same name as the type parameter from outer type
        private sealed class MemoryPoolBuffer<T> : IMemoryOwner<T>
#pragma warning restore CS0693 // Type parameter has the same name as the type parameter from outer type
        {
            private readonly int _length;
            private T[] _array;

            public MemoryPoolBuffer(int length)
            {
                _array = ArrayPool<T>.Shared.Rent(length);
                _length = length;
            }

            public Memory<T> Memory
            {
                get
                {
                    var array = _array;

                    ObjectDisposedException.ThrowIf(array is null, this);

                    return new Memory<T>(array, 0, _length);
                }
            }

            public void Dispose()
            {
                var array = Interlocked.Exchange(ref _array, null);

                if (array != null)
                {
                    ArrayPool<T>.Shared.Return(array);
                }
            }
        }
    }
}
