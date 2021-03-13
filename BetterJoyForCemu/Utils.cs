using System.Threading;
using System.Runtime.CompilerServices; 


class Lock {
    private int _lock;

    public Lock() {
		_lock = 0;
	}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void waitLock() {
        while(Interlocked.CompareExchange(ref _lock, 1, 0) != 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void unlock() {
        _lock = 0;
    }
}
