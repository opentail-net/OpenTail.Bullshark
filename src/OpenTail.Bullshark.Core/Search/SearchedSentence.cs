using System.Globalization;

namespace OpenTail.Bullshark.Core.Search;

/// <summary>
/// The line that says how much a search read, when it found no session (firstrun batch B8): "Searched 195 sessions." so the owner knows the
/// search ran, and "No transcripts to search yet." when there were none. The count uses the current culture, as the other counts do.
/// </summary>
public static class SearchedSentence
{
    public static string Of(int? sessions) => sessions switch
    {
        null => string.Empty,
        0 => "No transcripts to search yet.",
        1 => "Searched 1 session.",
        var count => $"Searched {count.Value.ToString("N0", CultureInfo.CurrentCulture)} sessions.",
    };
}
