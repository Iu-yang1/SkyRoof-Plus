using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SkyRoof.Remote.Ipc;

[SupportedOSPlatform("windows")]
public static class WindowsPipeAccessPolicy
{
    public static PipeSecurity? Create(string variable)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured)) return null;
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Windows process has no user SID.");
        return CreateFor(owner, configured);
    }

    public static PipeSecurity CreateFor(SecurityIdentifier owner, string configured)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(owner,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        var callers = configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => new SecurityIdentifier(value)).Distinct();
        foreach (var caller in callers)
        {
            if (caller.Equals(owner)) continue;
            security.AddAccessRule(new PipeAccessRule(caller, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }
        return security;
    }
}
