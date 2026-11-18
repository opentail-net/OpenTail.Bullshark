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

    /// <summary>How many tags in use the prompt shows the model, most used first (tags plan, Part B).</summary>
    public const int MaxKnownTags = 30;

    /// <summary>How many tags a session may hold in all. The owner's own tags are never dropped, even above this (tags plan, Part B).</summary>
    public const int MaxTags = 8;

    /// <summary>How many tags one reply may add to a session (tags plan, Part B).</summary>
    public const int MaxNewTags = 2;

    /// <summary>The shortest and the longest tag the magic rename adds, in characters (tags plan, Part B).</summary>
    public const int MinTagLength = 2;

    /// <summary>See <see cref="MinTagLength"/>.</summary>
    public const int MaxTagLength = 20;

    private const string TagsPrefix = "tags:";

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
    /// A reply read as a name and its tags (tags plan, Part B). A line that starts with "tags:" (any case, after a bullet mark if there is one)
    /// is the tags line; the name is the first other line, cleaned by <see cref="Clean"/>, so a reply of one line is still a name and a tags
    /// line may come first. The tags on it are split on commas and spaces, normalised with <see cref="SessionLibrary.TagsOf"/>, and kept when
    /// they are <see cref="MinTagLength"/> to <see cref="MaxTagLength"/> characters of a-z, 0-9 and hyphen. At most <see cref="MaxNewTags"/>,
    /// without repeats. The name is null when nothing usable is left; the caller then fails the reply, whatever tags it had. A reply with no tags
    /// line gives no tags.
    /// </summary>
    public static (string? Name, IReadOnlyList<string> Tags) ParseReply(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return (null, []);
        var lines = reply
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();

        string? nameLine = null;
        var tags = new List<string>();
        foreach (var line in lines)
        {
            var tagText = TagsTextOf(line);
            if (tagText is null)
            {
                nameLine ??= line;
                continue;
            }
            foreach (var tag in SessionLibrary.TagsOf(tagText))
            {
                if (tags.Count >= MaxNewTags) break;
                if (IsTag(tag) && !tags.Contains(tag, StringComparer.Ordinal)) tags.Add(tag);
            }
        }

        var name = nameLine is null ? null : Clean(nameLine);
        return (name, tags);
    }

    /// <summary>
    /// The tags a session holds after a magic rename (tags plan, Part B): the owner's tags first, in their order, then the added ones in
    /// their order, without a repeat (compared without case), and never more than <paramref name="cap"/> in all. Nothing is removed: the
    /// owner's tags stay even when they already number more than the cap, and blank ones are left out. Pure.
    /// </summary>
    public static IReadOnlyList<string> MergeTags(IReadOnlyList<string>? existing, IReadOnlyList<string> added, int cap = MaxTags)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existing ?? [])
        {
            if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag)) merged.Add(tag);
        }
        foreach (var tag in added)
        {
            if (merged.Count >= cap) break;
            if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag)) merged.Add(tag);
        }
        return merged;
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
    /// This overload gives no tags in use; see the one with <paramref name="knownTags"/>.
    /// </summary>
    public static string PromptFor(IReadOnlyList<string> usedNames, string extract) => PromptFor(usedNames, extract, []);

    /// <summary>
    /// <see cref="PromptFor(IReadOnlyList{string}, string)"/> with the tags in use in the library (tags plan, Part B): the first
    /// <see cref="MaxKnownTags"/> of them, in the order given (most used first), so the model reuses one when it fits.
    /// </summary>
    public static string PromptFor(IReadOnlyList<string> usedNames, string extract, IReadOnlyList<string> knownTags)
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
        var tags = knownTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(MaxKnownTags).ToList();
        text.Append(tags.Count == 0
            ? "No tag is in use in the library yet.\n"
            : "Tags already in use (reuse one when it fits): " + string.Join(", ", tags) + ".\n");
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

    /// <summary>The text after "tags:" when the line is the tags line (any case, after a bullet mark if there is one); otherwise null.</summary>
    private static string? TagsTextOf(string line)
    {
        var text = line.TrimStart(LeadingMarks).TrimStart();
        return text.StartsWith(TagsPrefix, StringComparison.OrdinalIgnoreCase) ? text[TagsPrefix.Length..] : null;
    }

    /// <summary>A tag the magic rename may add: 2 to 20 characters, each a-z, 0-9 or hyphen. The tag is already in lower case.</summary>
    private static bool IsTag(string tag)
    {
        if (tag.Length < MinTagLength || tag.Length > MaxTagLength) return false;
        foreach (var c in tag)
        {
            if (!((c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') || c == '-')) return false;
        }
        return true;
    }

    private static bool IsDoubleQuote(Rune rune) =>
        rune.Value is '"' or '`' or '“' or '”' or '„' or '«' or '»';

    private static bool IsSingleQuote(Rune rune) => rune.Value is '\'' or '‘' or '’';
}
