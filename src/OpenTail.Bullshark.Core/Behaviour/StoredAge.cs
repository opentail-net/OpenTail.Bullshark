namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// The age of a time that was stored earlier (R5). A clock that went back gives a negative age. With <c>state.clampClockBackwards</c> on (the
/// default) such an age is zero; with it off, the raw negative age is returned. Every age that comes from a stored time goes through here, so the
/// rule is written once. The setting is read by <see cref="BehaviourStore"/>; this class only applies it.
/// </summary>
public static class StoredAge
{
    /// <summary>The age of <paramref name="then"/> at <paramref name="now"/>, clamped to zero when <paramref name="clampBackwards"/> is set.</summary>
    public static TimeSpan Of(DateTimeOffset now, DateTimeOffset then, bool clampBackwards = true) => Clamp(now - then, clampBackwards);

    /// <summary>An age already computed: a negative one is zero when <paramref name="clampBackwards"/> is set, and is returned as it is when not.</summary>
    public static TimeSpan Clamp(TimeSpan age, bool clampBackwards = true) =>
        clampBackwards && age < TimeSpan.Zero ? TimeSpan.Zero : age;
}
