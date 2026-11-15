using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Mathematics;

public sealed class MathJsonConverterFactory : JsonConverterFactory
{
    public static readonly MathJsonConverterFactory Instance = new();

    private static readonly Dictionary<Type, Func<JsonConverter>> Converters = new()
    {
        [typeof(Vector2)] = () => new Vector2Converter(),
        [typeof(Vector3)] = () => new Vector3Converter(),
        [typeof(Vector4)] = () => new Vector4Converter(),
        [typeof(Quaternion)] = () => new QuaternionConverter(),
        [typeof(Color)] = () => new ColorConverter(),
    };

    public override bool CanConvert(Type typeToConvert) => Converters.ContainsKey(typeToConvert);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        Converters.TryGetValue(typeToConvert, out Func<JsonConverter>? factory) ? factory() : null;

    private sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] values = ReadArray(ref reader, 2);
            return new Vector2(Value(values, 0), Value(values, 1));
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
            WriteArray(writer, [value.X, value.Y]);
    }

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] values = ReadArray(ref reader, 3);
            return new Vector3(Value(values, 0), Value(values, 1), Value(values, 2));
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
            WriteArray(writer, [value.X, value.Y, value.Z]);
    }

    private sealed class Vector4Converter : JsonConverter<Vector4>
    {
        public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] values = ReadArray(ref reader, 4);
            return new Vector4(Value(values, 0), Value(values, 1), Value(values, 2), Value(values, 3));
        }

        public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options) =>
            WriteArray(writer, [value.X, value.Y, value.Z, value.W]);
    }

    private sealed class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] values = ReadArray(ref reader, 4);
            return new Quaternion(Value(values, 0), Value(values, 1), Value(values, 2), Value(values, 3));
        }

        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) =>
            WriteArray(writer, [value.X, value.Y, value.Z, value.W]);
    }

    private sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            float[] values = ReadArray(ref reader, 4);
            return new Color(Value(values, 0), Value(values, 1), Value(values, 2), Value(values, 3));
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            WriteArray(writer, [value.R, value.G, value.B, value.A]);
    }

    private static float[] ReadArray(ref Utf8JsonReader reader, int expectedCount)
    {
        List<float> values = [];
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.Number)
                    values.Add(reader.GetSingle());
            }
        }
        return [.. values];
    }

    private static float Value(float[] values, int index) =>
        index < values.Length ? values[index] : 0f;

    private static void WriteArray(Utf8JsonWriter writer, float[] values)
    {
        writer.WriteStartArray();
        foreach (float value in values)
            writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }
}
