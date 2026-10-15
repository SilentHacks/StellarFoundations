using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StellarFoundations;

internal sealed class SaveRotationSnapshot : JsonConverter<Quaternion>
{
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new ConcurrentDictionary<Type, FieldInfo[]>();
    private readonly Dictionary<Quaternion, Vector3> _angles;

    private SaveRotationSnapshot(Dictionary<Quaternion, Vector3> angles) => _angles = angles;

    internal static SaveRotationSnapshot Capture(object world, Func<Quaternion, Vector3>? readAngles = null)
    {
        var angles = new Dictionary<Quaternion, Vector3>();
        Visit(world, angles, readAngles ?? (rotation => rotation.eulerAngles));
        return new SaveRotationSnapshot(angles);
    }

    private static void Visit(object? value, Dictionary<Quaternion, Vector3> angles, Func<Quaternion, Vector3> readAngles)
    {
        if (value == null) return;
        if (value is Quaternion rotation)
        {
            if (!angles.ContainsKey(rotation)) angles.Add(rotation, readAngles(rotation));
            return;
        }
        if (value is Array array)
        {
            foreach (var element in array) Visit(element, angles, readAngles);
            return;
        }
        var fields = Fields.GetOrAdd(value.GetType(), type => type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => ContainsQuaternion(field.FieldType, new HashSet<Type>())).ToArray());
        foreach (var field in fields) Visit(field.GetValue(value)!, angles, readAngles);
    }

    private static bool ContainsQuaternion(Type type, HashSet<Type> visited)
    {
        if (type == typeof(Quaternion)) return true;
        if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
        if (type.IsArray) return ContainsQuaternion(type.GetElementType()!, visited);
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null) return ContainsQuaternion(nullable, visited);
        return type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(field => ContainsQuaternion(field.FieldType, visited));
    }

    public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
    {
        if (!_angles.TryGetValue(value, out var angles)) throw new JsonSerializationException("Rotation changed after its save snapshot was captured.");
        writer.WriteStartObject();
        Write(writer, "x", value.x);
        Write(writer, "y", value.y);
        Write(writer, "z", value.z);
        Write(writer, "w", value.w);
        writer.WritePropertyName("eulerAngles");
        writer.WriteStartObject();
        Write(writer, "x", angles.x);
        Write(writer, "y", angles.y);
        Write(writer, "z", angles.z);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var value = JObject.Load(reader);
        return new Quaternion(value.Value<float>("x"), value.Value<float>("y"), value.Value<float>("z"), value.Value<float>("w"));
    }

    private static void Write(JsonWriter writer, string name, float value)
    {
        writer.WritePropertyName(name);
        writer.WriteValue(value);
    }
}
