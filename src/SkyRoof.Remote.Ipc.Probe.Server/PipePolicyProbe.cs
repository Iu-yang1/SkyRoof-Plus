using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SkyRoof.Remote.Ipc;

internal static class PipePolicyProbe
{
    internal static void Verify()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Missing owner SID.");
        var caller = new SecurityIdentifier("S-1-5-21-123-456-789-1234");
        var security = WindowsPipeAccessPolicy.CreateFor(owner, caller.Value + ";" + caller.Value);
        using var stream = NamedPipeServerStreamAcl.Create("skyroof-acl-" + Guid.NewGuid().ToString("N"),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        var actual = stream.GetAccessControl();
        var rules = actual.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Require(rules.Length == 2, "Unexpected Windows pipe access rules.");
        var creator = rules.Single(rule => rule.IdentityReference.Equals(owner));
        var client = rules.Single(rule => rule.IdentityReference.Equals(caller));
        Require((creator.PipeAccessRights & PipeAccessRights.CreateNewInstance) != 0, "Owner cannot create instances.");
        Require((client.PipeAccessRights & PipeAccessRights.ReadWrite) == PipeAccessRights.ReadWrite,
            "Configured caller cannot read/write.");
        Require((client.PipeAccessRights & PipeAccessRights.CreateNewInstance) == 0,
            "Configured caller can create server instances.");
        Require(rules.All(rule => rule.AccessControlType == AccessControlType.Allow), "Unexpected deny entry.");
        Console.WriteLine("PASS: actual Windows pipe DACL grants server instance creation only to owner.");
    }

    private static void Require(bool valid, string error)
    {
        if (!valid) throw new InvalidOperationException(error);
    }
}
