using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SkyRoof
{
  // A private Windows high-resolution timer avoids changing the global
  // system timer resolution. Older Windows uses the ordinary timer.
  internal sealed class IcomScopePresentTimer : IDisposable
  {
    private const uint HighResolution = 2;
    private const uint TimerAccess = 0x001F0003;
    private readonly NativeTimer? timer;

    internal IcomScopePresentTimer()
    {
      SafeWaitHandle handle = CreateWaitableTimerEx(IntPtr.Zero, null, HighResolution, TimerAccess);
      if (handle.IsInvalid)
      {
        handle.Dispose();
        handle = CreateWaitableTimerEx(IntPtr.Zero, null, 0, TimerAccess);
      }
      if (handle.IsInvalid) handle.Dispose();
      else timer = new NativeTimer(handle);
    }

    internal void WaitUntil(long deadline)
    {
      long remaining = deadline - Stopwatch.GetTimestamp();
      if (remaining <= 0) return;
      long due = -(long)Math.Max(1, Math.Ceiling(remaining * 10_000_000d / Stopwatch.Frequency));
      if (timer != null && SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        timer.WaitOne();
      else Thread.Sleep((int)Math.Ceiling(-due / 10000d));
    }

    public void Dispose() => timer?.Dispose();

    private sealed class NativeTimer : WaitHandle
    {
      internal NativeTimer(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, string? name,
      uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period,
      IntPtr callback, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
  }
}
