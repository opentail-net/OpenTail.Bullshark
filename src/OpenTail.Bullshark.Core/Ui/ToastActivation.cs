namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// The activation argument of a toast that is about a session (behaviour batch, R2, rule 4). The window adds it to the toast, and reads it back when the
/// owner clicks the toast, so the click selects that session's row. The value is the session id, and no name, prompt or path.
/// </summary>
public static class ToastActivation
{
    /// <summary>The argument's key for the session id.</summary>
    public const string SessionKey = "session";
}
