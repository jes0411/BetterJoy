using System.Threading;
using System.Runtime.CompilerServices; 
using System.IO;

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

class Utils {
    public static string getApplicationFullPath() {
        string uriPath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;
        System.Uri uri = new System.Uri(Path.GetDirectoryName(uriPath) + @"\" + Path.GetFileName(uriPath));
        return uri.LocalPath;
    }
}
