using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SkyRoof.Remote.SignalBus;

[SupportedOSPlatform("windows")]
internal static class GlobalSignalRingObjects
{
    internal static (MemoryMappedFile Mapping, Mutex Mutex) Open(string name, long capacity, bool producer)
    {
        string mapName = "Global\\SkyRoofRemoteBus_" + name;
        string lockName = "Global\\SkyRoofRemoteBusLock_" + name;
        Mutex? mutex = null;
        try
        {
            mutex = producer ? CreateMutex(lockName) :
                MutexAcl.OpenExisting(lockName, MutexRights.Modify | MutexRights.Synchronize);
            var mapping = producer ? CreateMapping(mapName, capacity) :
                MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            return (mapping, mutex);
        }
        catch (Exception error) when (!producer && (error is FileNotFoundException or WaitHandleCannotBeOpenedException))
        {
            mutex?.Dispose();
            throw new InvalidDataException("Global signal ring producer is not ready.", error);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    private static Mutex CreateMutex(string name)
    {
        var (owner, readers) = AccessIdentities();
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(owner, MutexRights.FullControl, AccessControlType.Allow));
        foreach (var reader in readers)
            security.AddAccessRule(new MutexAccessRule(reader,
                MutexRights.Modify | MutexRights.Synchronize, AccessControlType.Allow));
        return MutexAcl.Create(false, name, out _, security);
    }

    private static MemoryMappedFile CreateMapping(string name, long capacity)
    {
        var (owner, readers) = AccessIdentities();
        var sddl = "D:P(A;;0x000F001F;;;" + owner.Value + ")" +
            string.Concat(readers.Select(sid => "(A;;0x00000004;;;" + sid.Value + ")"));
        var descriptor = new RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                Descriptor = pinned.AddrOfPinnedObject(),
                InheritHandle = 0
            };
            using var handle = CreateFileMapping(new IntPtr(-1), ref attributes, 0x04,
                (uint)((ulong)capacity >> 32), (uint)capacity, name);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Global SignalBus mapping creation failed.");
            return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        }
        finally { pinned.Free(); }
    }

    private static (SecurityIdentifier Owner, SecurityIdentifier[] Readers) AccessIdentities()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Windows process has no user SID.");
        var configured = Environment.GetEnvironmentVariable("SKYROOF_SIGNALBUS_READER_SIDS") ?? "";
        var readers = configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => new SecurityIdentifier(value)).Where(sid => !sid.Equals(owner)).Distinct().ToArray();
        return (owner, readers);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileMappingW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeMemoryMappedFileHandle CreateFileMapping(IntPtr file,
        ref SecurityAttributes attributes, uint protection, uint sizeHigh, uint sizeLow, string name);
}
