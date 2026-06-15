using System.Text.Json;
using System.Text.Json.Serialization;
using GroupTasker.Domain.Entities;

namespace GroupTasker.Infrastructure.Configuration;

/// <summary>
/// Serialises <see cref="HotkeyBinding"/> as a single string (e.g. <c>"Ctrl+Alt+G"</c>) so
/// the on-disk format stays compact and human-readable inside <c>launcher.json</c>.
/// </summary>
public sealed class HotkeyBindingJsonConverter : JsonConverter<HotkeyBinding>
{
    public override HotkeyBinding? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var text = reader.GetString();
        return HotkeyBinding.TryParse(text, out var binding) ? binding : null;
    }

    public override void Write(Utf8JsonWriter writer, HotkeyBinding value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
