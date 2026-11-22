using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Continue;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// Stores a session's <see cref="ContinueMode"/> mark (<see cref="LibraryEntry.ResumeMode"/>) in library.json as its setting text (<c>full</c>,
/// <c>lastTurns</c> or <c>summary</c>), not as the enum's number, so a reorder of the enum cannot change a stored mark. Reading never throws: a text
/// that is not a mode, and any other JSON value, reads as null (no mark). One bad field therefore cannot make the whole library unreadable.
/// </summary>
public sealed class ResumeModeJsonConverter : JsonConverter<ContinueMode?>
{
    /// <summary>The mode of the stored text, or null when the value is not a known mode text.</summary>
    public override ContinueMode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return ContinueModeText.Parse(reader.GetString());

        // A value that is not a string is no mark. A number, true, false or null is one token, already read; an object or an array is skipped whole.
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        return null;
    }

    /// <summary>The setting text of the mode, or a JSON null when there is no mark.</summary>
    public override void Write(Utf8JsonWriter writer, ContinueMode? value, JsonSerializerOptions options)
    {
        if (value is { } mode) writer.WriteStringValue(ContinueModeText.ToText(mode));
        else writer.WriteNullValue();
    }
}
