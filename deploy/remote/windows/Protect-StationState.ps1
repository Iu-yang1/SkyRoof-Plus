[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [string]$ServiceAccount = 'S-1-5-18'
)
$ErrorActionPreference = 'Stop'
$root = Get-Item -LiteralPath $Directory -Force
if (-not $root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Station state must be a real directory.'
}
$serviceSid = if ($ServiceAccount -match '^S-1-') {
    [Security.Principal.SecurityIdentifier]::new($ServiceAccount)
} else {
    [Security.Principal.NTAccount]::new($ServiceAccount).Translate([Security.Principal.SecurityIdentifier])
}
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$entries = @($root) + @(Get-ChildItem -LiteralPath $root.FullName -Recurse -Force)
if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Station state contains a reparse point; ACL update was not applied.'
}
foreach ($entry in $entries) {
    $acl = Get-Acl -LiteralPath $entry.FullName
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($rule in $acl.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier])) {
        $acl.RemoveAccessRuleSpecific($rule)
    }
    $inheritance = if ($entry.PSIsContainer) {
        [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    } else {
        [Security.AccessControl.InheritanceFlags]::None
    }
    foreach ($sid in @($system,$admins,$serviceSid) | Select-Object -Unique) {
        $rights = if ($sid -eq $system -or $sid -eq $admins) {
            [Security.AccessControl.FileSystemRights]::FullControl
        } else {
            [Security.AccessControl.FileSystemRights]::Modify
        }
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,$rights,$inheritance,[Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $entry.FullName -AclObject $acl
}
