using System.IO.Pipes;
using System.Security.Principal;

namespace SkyRoof.Remote.Ipc;

public static class LocalPipeClient
{
    public static NamedPipeClientStream Create(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            // GENERIC_WRITE includes FILE_CREATE_PIPE_INSTANCE. A trusted
            // non-server client needs only explicit read/write/synchronize.
            return new NamedPipeClientStream(".", name,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous, TokenImpersonationLevel.None, HandleInheritability.None);
        }
        return new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.None);
    }
}
