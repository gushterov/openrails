// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orts.Common
{
    /// <summary>
    /// Stores command data without running constructors or persisting static receivers.
    /// Legacy BinaryFormatter files are read as NRBF records, never deserialized as objects.
    /// Only concrete commands from the simulation and viewer assemblies are accepted.
    /// </summary>
    public static class ReplayLogSerializer
    {
        const int FormatVersion = 1;
        const int MaximumCommands = 1000000;
        static readonly ConcurrentDictionary<Type, FieldInfo[]> CommandFields = new ConcurrentDictionary<Type, FieldInfo[]>();
        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public static void Write(Stream stream, IReadOnlyList<ICommand> commands)
        {
            if (commands.Count > MaximumCommands)
                throw new InvalidDataException("Replay command log is too large.");
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteNumber("Version", FormatVersion);
            writer.WriteStartArray("Commands");
            foreach (var command in commands)
            {
                var type = GetCommandType(command.GetType().FullName);
                writer.WriteStartObject();
                writer.WriteString("Type", type.FullName);
                writer.WriteStartObject("Fields");
                foreach (var field in GetFields(type))
                {
                    writer.WritePropertyName(FieldName(field));
                    JsonSerializer.Serialize(writer, field.GetValue(command), field.FieldType, JsonOptions);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public static List<ICommand> Read(Stream stream)
        {
            // BinaryFormatter payloads start with a zero byte; JSON files start with '{'.
            long start = stream.Position;
            int first = stream.ReadByte();
            stream.Position = start;
            try
            {
                return first == 0 ? ReadLegacy(stream) : ReadJson(stream);
            }
            catch (Exception error) when (error is JsonException || error is SerializationException
                || error is InvalidOperationException || error is ArgumentException || error is KeyNotFoundException)
            {
                throw new InvalidDataException("Invalid replay command log.", error);
            }
        }

        static List<ICommand> ReadJson(Stream stream)
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported replay command log version.");
            var records = root.GetProperty("Commands");
            if (records.GetArrayLength() > MaximumCommands)
                throw new InvalidDataException("Replay command log is too large.");
            var commands = new List<ICommand>();
            foreach (var record in records.EnumerateArray())
            {
                var type = GetCommandType(record.GetProperty("Type").GetString());
                var command = (ICommand)RuntimeHelpers.GetUninitializedObject(type);
                var fields = GetFields(type).ToDictionary(FieldName);
                foreach (var value in record.GetProperty("Fields").EnumerateObject())
                {
                    if (!fields.TryGetValue(value.Name, out var field))
                        throw new InvalidDataException("Unknown replay command field: " + value.Name);
                    field.SetValue(command, value.Value.Deserialize(field.FieldType, JsonOptions));
                }
                commands.Add(command);
            }
            return commands;
        }

        static List<ICommand> ReadLegacy(Stream stream)
        {
            var root = NrbfDecoder.DecodeClassRecord(stream, leaveOpen: true);
            if (!root.TypeNameMatches(typeof(List<ICommand>)))
                throw new InvalidDataException("Expected a replay command list.");
            int count = root.GetInt32("_size");
            if (count < 0 || count > MaximumCommands
                || root.GetArrayRecord("_items") is not SZArrayRecord<SerializationRecord> items
                || items.Length < count || items.Length > MaximumCommands)
                throw new InvalidDataException("Invalid replay command array.");
            var records = items.GetArray();
            var commands = new List<ICommand>();
            for (int index = 0; index < count; index++)
            {
                if (records[index] is not ClassRecord record)
                    throw new InvalidDataException("Invalid replay command record.");
                var type = GetCommandType(record.TypeName.FullName);
                var command = (ICommand)RuntimeHelpers.GetUninitializedObject(type);
                foreach (var field in GetFields(type))
                {
                    // BinaryFormatter qualifies inherited private fields with their declaring
                    // type; inherited protected fields can also appear with a qualified alias.
                    string name = record.HasMember(field.Name) ? field.Name : field.DeclaringType.Name + "+" + field.Name;
                    if (!record.HasMember(name)) continue;
                    object value = record.GetRawValue(name);
                    var valueType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                    if (valueType.IsEnum && value is ClassRecord enumRecord && enumRecord.TypeNameMatches(valueType))
                        value = Enum.ToObject(valueType, enumRecord.GetRawValue("value__"));
                    if (value != null && !valueType.IsInstanceOfType(value))
                        throw new InvalidDataException("Invalid replay field type: " + name);
                    field.SetValue(command, value);
                }
                commands.Add(command);
            }
            return commands;
        }

        static Type GetCommandType(string name)
        {
            Type type = name == null ? null : typeof(Command).Assembly.GetType(name);
            if (type == null && name != null && name.StartsWith("Orts.Viewer3D.", StringComparison.Ordinal))
            {
                // This is a fixed application assembly, never an assembly name from the file.
                type = Assembly.Load("RunActivity").GetType(name);
            }
            if (type == null || type.IsAbstract || !typeof(Command).IsAssignableFrom(type))
                throw new InvalidDataException("Unknown replay command type: " + name);
            return type;
        }

        static string FieldName(FieldInfo field) => field.DeclaringType.Name + "." + field.Name;

        static FieldInfo[] GetFields(Type type) => CommandFields.GetOrAdd(type, commandType =>
        {
            var fields = new List<FieldInfo>();
            for (var declaringType = commandType; declaringType != null && declaringType != typeof(object); declaringType = declaringType.BaseType)
            {
                foreach (var field in declaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    var valueType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                    if (!valueType.IsPrimitive && !valueType.IsEnum && valueType != typeof(string))
                        throw new InvalidDataException("Unsupported replay command field: " + FieldName(field));
                    fields.Add(field);
                }
            }
            return fields.ToArray();
        });
    }
}
