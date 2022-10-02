using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RCaron;
using RCaron.Classes;
using RCaron.LibrarySourceGenerator;

namespace RCaron.FunLibrary;

[Module("Json")]
public partial class JsonModule : IRCaronModule
{
    private readonly ClassInstanceWriteConverter _classInstanceWriteConverter = new();

    [Method("ConvertTo-Json")]
    public string ConvertToJson(Motor motor, object? obj, bool writeIndented = false)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            Converters =
            {
                _classInstanceWriteConverter
            }
        });
        return json;
    }

    [Method("ConvertFrom-Json")]
    public object? ConvertFromJson(Motor motor, string json, ClassDefinition? classDefinition = null,
        RCaronType? type = null)
    {
        if (classDefinition != null)
        {
            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new ClassInstanceReadConverter(classDefinition));
            return JsonSerializer.Deserialize<ClassInstance>(json, jsonOptions);
        }
        else if (type != null)
            return JsonSerializer.Deserialize(json, type.Type);
        return JsonNode.Parse(json);
    }
}

public class ClassInstanceWriteConverter : JsonConverter<ClassInstance>
{
    public override ClassInstance Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new Exception(
            $"can not read {nameof(ClassInstance)}, for reading use {nameof(ClassInstanceReadConverter)}");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ClassInstance classInstance,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (classInstance.Definition.PropertyNames != null && classInstance.PropertyValues != null)
            for (var i = 0; i < classInstance.Definition.PropertyNames.Length; i++)
            {
                var name = classInstance.Definition.PropertyNames[i];
                var value = classInstance.PropertyValues[i];
                writer.WritePropertyName(name);
                if (value != null)
                    JsonSerializer.Serialize(writer, value, value.GetType(), options);
                else
                    writer.WriteNullValue();
            }

        writer.WriteEndObject();
    }
}

public class ClassInstanceReadConverter : JsonConverter<ClassInstance>
{
    public ClassDefinition ClassDefinition { get; set; }

    public ClassInstanceReadConverter(ClassDefinition classDefinition)
    {
        ClassDefinition = classDefinition;
    }

    public override ClassInstance? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            throw new Exception(
                $"failed to read {nameof(ClassInstance)}, expected {JsonTokenType.StartObject} but got {reader.TokenType}");
        }

        var classInstance = new ClassInstance(ClassDefinition);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return classInstance;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            var propertyName = reader.GetString();
            if (propertyName == null)
                throw new JsonException();

            var propertyIndex = Array.IndexOf(ClassDefinition.PropertyNames!, propertyName);
            if (propertyIndex == -1)
                throw new JsonException();

            reader.Read();
            // var propertyType = ClassDefinition.PropertyTypes[propertyIndex];
            var propertyType = reader.TokenType switch
            {
                JsonTokenType.String => typeof(string),
                JsonTokenType.Number => typeof(double),
                JsonTokenType.True => typeof(bool),
                JsonTokenType.False => typeof(bool),
                JsonTokenType.Null => typeof(object),
                JsonTokenType.StartObject => throw new Exception(
                    "can not read objects inside of {nameof(ClassInstance)} currently"),
                JsonTokenType.StartArray => throw new Exception(
                    "can not read arrays inside of {nameof(ClassInstance)} currently"),
                _ => throw new JsonException()
            };
            var propertyValue = JsonSerializer.Deserialize(ref reader, propertyType, options);
            classInstance.PropertyValues![propertyIndex] = propertyValue;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ClassInstance value, JsonSerializerOptions options)
    {
        throw new Exception(
            $"can not write {nameof(ClassInstance)}, for writing use {nameof(ClassInstanceWriteConverter)}");
    }
}