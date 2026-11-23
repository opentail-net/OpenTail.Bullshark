using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Which platform writes a resume's summary (RS-D9, RS-D28). Normally the source writes it. The target writes it when the source is out of quota (Claude at
/// its limit, RS-O5), or when the owner asks the target to write it after the source's summary failed. The words the screens show are here too, so they
/// are the same on each screen. Pure: the tests need no window.
/// </summary>
public static class SummaryWriterRules
{
    /// <summary>
    /// The platform that writes the summary: the target when the owner asked it to (<paramref name="writeByTarget"/>) and the target is another platform,
    /// else the source. A target that is the source writes its own summary.
    /// </summary>
    public static ProviderId WriterOf(ProviderId source, ProviderId target, bool writeByTarget) =>
        writeByTarget && target != source ? target : source;

    /// <summary>The <c>WrittenBy</c> a kept summary carries: null when the source wrote it (the default, RS-D28), else the writer.</summary>
    public static ProviderId? WrittenByOf(ProviderId source, ProviderId writer) => writer == source ? null : writer;

    /// <summary>
    /// True when a Claude source at its limit leaves the summary to the target (RS-D28): Claude is the source, its limit is in force, and the target is
    /// another platform. With the target Claude itself there is no other platform to write it, so Claude makes the call as it always does.
    /// </summary>
    public static bool ClaudeAtLimitWritesByTarget(ProviderId source, ProviderId target, bool claudeAtLimit) =>
        source == ProviderId.Claude && claudeAtLimit && target != ProviderId.Claude;

    /// <summary>The cost line of a summary the target writes because Claude is at its limit (RS-D28), shown before the call.</summary>
    public static string AtLimitCostLine(ProviderId target) =>
        $"Claude is at its limit, so {NameOf(target)} writes the summary (one call, made by {NameOf(target)}).";

    /// <summary>The cost line of a summary the target writes after the source's failed (RS-D28), shown before the call.</summary>
    public static string OneCallLine(ProviderId writer) => $"One call, made by {NameOf(writer)}.";

    /// <summary>The button that asks the target to write the summary after the source's summary failed (RS-D28).</summary>
    public static string ButtonOf(ProviderId target) => "Make summary with " + NameOf(target);

    /// <summary>The preview's line when the summary was written by a platform other than the source (RS-D28).</summary>
    public static string WrittenByLine(ProviderId writer) => "Summary written by " + NameOf(writer) + ".";

    /// <summary>
    /// The platform's name as the screens say it. It is <see cref="ContinueDecision.NameOf"/>, the one name the resume screens use.
    /// </summary>
    private static string NameOf(ProviderId provider) => ContinueDecision.NameOf(provider);
}
