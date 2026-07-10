using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// System.Text.Json converter for ExpandoObject.
/// Handles serialization and deserialization of ExpandoObject (used by JS script settings in task groups).
/// Keys are preserved as-is (no camelCase transformation) since they come from JS script definitions.
/// </summary>
public class ExpandoObjectJsonConverter : JsonConverter<ExpandoObject>
{
    public override ExpandoObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StartObject token, got {reader.TokenType}");
        }

        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return expando;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName token, got {reader.TokenType}");
            }

            var propertyName = reader.GetString()!;
            reader.Read();
            dict[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, ExpandoObject value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        var dict = (IDictionary<string, object?>)value;

        foreach (var kvp in dict)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value);
        }

        writer.WriteEndObject();
    }

    private object? ReadValue(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.StartObject => ReadNestedObject(ref reader),
            JsonTokenType.StartArray => ReadArray(ref reader),
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    private static object ReadNumber(ref Utf8JsonReader reader)
    {
        // Try integer first, fall back to double
        if (reader.TryGetInt64(out var longValue))
        {
            // If the value fits in int range and has no decimal, use long
            // But check if the raw text contains a decimal point
            if (reader.TryGetDouble(out var doubleValue) && longValue != doubleValue)
            {
                return doubleValue;
            }
            return longValue;
        }

        return reader.GetDouble();
    }

    private ExpandoObject ReadNestedObject(ref Utf8JsonReader reader)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return expando;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected PropertyName token, got {reader.TokenType}");
            }

            var propertyName = reader.GetString()!;
            reader.Read();
            dict[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON while reading nested object");
    }

    private List<object?> ReadArray(ref Utf8JsonReader reader)
    {
        var list = new List<object?>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return list;
            }

            list.Add(ReadValue(ref reader));
        }

        throw new JsonException("Unexpected end of JSON while reading array");
    }

    private void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case ExpandoObject nested:
                Write(writer, nested, null!);
                break;
            case IDictionary<string, object?> dict:
                writer.WriteStartObject();
                foreach (var kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteValue(writer, kvp.Value);
                }
                writer.WriteEndObject();
                break;
            case IList<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            case IEnumerable<object?> enumerable:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                // Fallback: serialize using JsonSerializer for unknown types
                JsonSerializer.Serialize(writer, value, value.GetType());
                break;
        }
    }
}
