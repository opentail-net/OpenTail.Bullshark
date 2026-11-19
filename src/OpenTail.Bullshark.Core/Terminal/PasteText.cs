using System.Text;

namespace OpenTail.Bullshark.Core.Terminal;

/// <summary>
/// Makes clipboard text safe to send inside a bracketed paste (<c>ESC[200~ ... ESC[201~</c>). A pasted ESC would let the text
/// end the paste early and type the rest into a session, so every control character goes, except the tab and the line breaks.
/// Line breaks become CR, as a terminal sends Enter. Other text, including non-ASCII and surrogate pairs, is kept as it is.
/// </summary>
public static class PasteText
{
    /// <summary>Removes control characters (C0 except tab, CR and LF; DEL; C1) and makes every line break a CR.</summary>
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                // CRLF and a lone CR both become one CR; a lone LF becomes CR too.
                builder.Append('\r');
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n')
            {
                builder.Append('\r');
            }
            else if (c == '\t' || (c >= ' ' && c != '\u007f' && (c < '\u0080' || c > '\u009f')))
            {
                builder.Append(c);
            }
            // Everything else (ESC, NUL, BEL, BS, VT, FF, other C0, DEL, C1 such as U+009B CSI) is dropped.
        }
        return builder.ToString();
    }
}
