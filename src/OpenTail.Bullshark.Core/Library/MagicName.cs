using System.Globalization;
using System.Text;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// The magic names (magic rename, D3): what Haiku's reply becomes as a session's library name. A reply is cleaned to one line of at most
/// <see cref="MaxLength"/> characters with no quotes, no emoji (the wand is added by the window) and no trailing punctuation. A name that
/// another session already has gets a short distinguishing part. Pure: nothing here reads a file or calls a model.
/// </summary>
public static class MagicName
{
    /// <summary>The longest magic name, in characters.</summary>
    public const int MaxLength = 40;

    /// <summary>What a cut name ends with. It is the only punctuation a name keeps, and only when the name had to be cut.</summary>
    public const string Ellipsis = "...";

    /// <summary>How many names of the same folder the prompt shows the model, so it can avoid them.</summary>
    public const int MaxUsedNames = 30;

    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', '!', '?', '-', '–', '—', '…', '*', '_', '~'];
    private static readonly char[] LeadingMarks = ['#', '*', '-', '>', '•', '~', '_'];

    /// <summary>
    /// The reply as a name: its first non-empty line, with no emoji, no quotes (an apostrophe inside a word stays), single spaces, no
    /// leading markdown mark, no trailing punctuation, and the first letter upper-case. Null when nothing is left (an empty or unusable reply).
    /// A longer name is cut at a word boundary to fit <see cref="MaxLength"/>, with <see cref="Ellipsis"/>.
    /// </summary>
    public static string? Clean(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var line = reply
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.Length > 0);
        if (line is null) return null;

        var runes = line.EnumerateRunes().ToList();
        var kept = new StringBuilder();
        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (IsEmojiOrInvisible(rune) || IsDoubleQuote(rune)) continue;
            // An apostrophe is a quote only at a word's edge: "users' login" loses it, "user's login" keeps it.
            if (IsSingleQuote(rune) && !(i > 0 && i + 1 < runes.Count && Rune.IsLetter(runes[i - 1]) && Rune.IsLetter(runes[i + 1]))) continue;
            kept.Append(rune.ToString());
        }

        var text = string.Join(' ', kept.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        text = text.TrimStart(LeadingMarks).TrimStart();
        text = text.TrimEnd(TrailingPunctuation).TrimEnd();
        if (text.Length == 0) return null;

        var first = text.EnumerateRunes().First();
        text = Rune.ToUpperInvariant(first).ToString() + text[first.Utf16SequenceLength..];

        return text.Length <= MaxLength ? text : CutToFit(text);
    }

    /// <summary>
    /// A name that is not in <paramref name="taken"/>. When it is, a dated part is added (" - 18 Nov"), then a number (" 2", " 3"), each
    /// within <see cref="MaxLength"/>. <paramref name="taken"/> must compare names without case (the caller's set does).
    /// </summary>
    public static string Distinct(string name, ISet<string> taken, DateTimeOffset when)
    {
        if (!taken.Contains(name)) return name;
        var dated = WithSuffix(name, " - " + when.ToString("d MMM", CultureInfo.InvariantCulture));
        if (!taken.Contains(dated)) return dated;
        for (var number = 2; ; number++)
        {
            var numbered = WithSuffix(name, " " + number.ToString(CultureInfo.InvariantCulture));
            if (!taken.Contains(numbered)) return numbered;
        }
    }

    /// <summary>
    /// The user message the model is given after the system prompt: the names already used in the folder, then the session's extract
    /// (<see cref="NameExtract"/>). The names are shown so the model avoids them; <see cref="Distinct"/> is the rule that enforces it.
    /// </summary>
    public static string PromptFor(IReadOnlyList<string> usedNames, string extract)
    {
        var text = new StringBuilder();
        if (usedNames.Count == 0)
        {
            text.Append("No name is used in this folder yet.\n");
        }
        else
        {
            text.Append("Names already used in this folder (do not repeat them):\n");
            foreach (var name in usedNames.Take(MaxUsedNames)) text.Append("- ").Append(name).Append('\n');
        }
        text.Append("\nThe session:\n").Append(extract);
        return text.ToString();
    }

    /// <summary>The name with a suffix added, cut at a word boundary so that the whole fits <see cref="MaxLength"/>.</summary>
    private static string WithSuffix(string name, string suffix)
    {
        var room = MaxLength - suffix.Length;
        var stem = name.EndsWith(Ellipsis, StringComparison.Ordinal) ? name[..^Ellipsis.Length] : name;
        if (stem.Length > room)
        {
            var cut = stem[..room];
            var space = cut.LastIndexOf(' ');
            stem = (space > 0 ? cut[..space] : cut).TrimEnd();
        }
        return stem + suffix;
    }

    /// <summary>The first characters of the name that fit with the ellipsis, cut at the last space, with no trailing punctuation.</summary>
    private static string CutToFit(string text)
    {
        var budget = MaxLength - Ellipsis.Length;
        var cut = text[..budget];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var space = cut.LastIndexOf(' ');
        if (space > 0) cut = cut[..space];
        cut = cut.TrimEnd(TrailingPunctuation).TrimEnd();
        return cut + Ellipsis;
    }

    /// <summary>Emoji, pictographs, modifiers and other symbols, surrogates, and the joiners and selectors that bind emoji together.</summary>
    private static bool IsEmojiOrInvisible(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.Surrogate) return true;
        return rune.Value is 0x200B or 0x200C or 0x200D or 0x20E3 or (>= 0xFE00 and <= 0xFE0F);
    }

    private static bool IsDoubleQuote(Rune rune) =>
        rune.Value is '"' or '`' or '“' or '”' or '„' or '«' or '»';

    private static bool IsSingleQuote(Rune rune) => rune.Value is '\'' or '‘' or '’';
}
