using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OpenTail.Bullshark.Core.Tests.Support;

/// <summary>
/// Makes a file or a folder unreadable to the current user, for the store tests. Windows only: callers check
/// <see cref="OperatingSystem.IsWindows"/> first. Disposing the result restores the access rules the item had before, so the
/// test folder can be deleted afterwards. The rules are restored from the SDDL string taken before the change: a security object
/// that was only read is not written back by <c>SetAccessControl</c>, so restoring from the old object would silently do nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Unreadable
{
    /// <summary>
    /// The folder's access rules are replaced by one for SYSTEM only, and inheritance is cut. The current user then has no entry,
    /// so a file inside the folder cannot be opened and <see cref="File.Exists"/> is false for it. The file must exist before this call.
    /// </summary>
    public static IDisposable FolderWithoutMe(string folder)
    {
        var info = new DirectoryInfo(folder);
        var sddl = info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        var changed = info.GetAccessControl();
        changed.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in changed.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            changed.RemoveAccessRuleAll(rule);
        changed.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        info.SetAccessControl(changed);

        return new Restore(() =>
        {
            var restored = info.GetAccessControl();
            restored.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
            info.SetAccessControl(restored);
        });
    }

    /// <summary>
    /// The file denies the current user reading its data. <see cref="File.Exists"/> is still true for it, and opening it throws
    /// <see cref="UnauthorizedAccessException"/>. The folder's rules are not changed, so the file can still be replaced.
    /// </summary>
    public static IDisposable FileDenyingRead(string file)
    {
        var info = new FileInfo(file);
        var sddl = info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        var changed = info.GetAccessControl();
        changed.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        info.SetAccessControl(changed);

        return new Restore(() =>
        {
            var restored = info.GetAccessControl();
            restored.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
            info.SetAccessControl(restored);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
