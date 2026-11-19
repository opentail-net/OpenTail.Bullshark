namespace OpenTail.Bullshark.Core.Folders;

/// <summary>
/// A path as the owner reads it on a page: the user's profile folder is written as <c>~</c>, so an empty page can name where it looked without
/// showing the user name (firstrun batch B8). A path outside the profile, or a profile that is not a prefix of it, is returned unchanged.
/// </summary>
public static class UserPathText
{
    public static string Of(string path, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(path);
        var profile = (userProfile ?? string.Empty).TrimEnd('\\', '/');
        if (profile.Length == 0 || path.Length < profile.Length) return path;
        if (!path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)) return path;
        var rest = path[profile.Length..];
        return rest.Length == 0 || rest[0] is '\\' or '/' ? "~" + rest : path;
    }
}
