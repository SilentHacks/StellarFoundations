using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StellarFoundations;

internal struct PositionD
{
    public double X, Y, Z;
    internal PositionD(double x, double y, double z) { X = x; Y = y; Z = z; }
    internal bool Finite => Policy.Finite(X) && Policy.Finite(Y) && Policy.Finite(Z);
    internal double LengthSquared => X * X + Y * Y + Z * Z;
    internal bool ValidLocal => Finite && Policy.Finite(LengthSquared) && LengthSquared <= 1e12;
    internal double Distance(PositionD other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));
}

internal struct RotationF
{
    public float X, Y, Z, W;
    internal RotationF(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    internal bool Valid => Policy.Finite(X) && Policy.Finite(Y) && Policy.Finite(Z) && Policy.Finite(W)
        && Math.Abs(X * X + Y * Y + Z * Z + W * W - 1d) < 0.001;
}

internal sealed class PierRecord
{
    public string Id = string.Empty;
    public ushort Part;
    public PositionD LocalCap;
    public PositionD LocalFoot;
    public PositionD LocalFootNormal;
    public float Length;
}

internal sealed class GroundRecord
{
    public string Id = string.Empty;
    public uint Ship;
    public ulong Planet;
    public PositionD Position;
    public RotationF Rotation;
    public uint Revision;
    public List<PierRecord> Piers = new List<PierRecord>();
}

internal sealed class GroundDocument
{
    public int Format = 1;
    public List<GroundRecord> Structures = new List<GroundRecord>();

    internal void Validate()
    {
        if (Format != 1 || Structures == null || Structures.Count > Policy.MaximumStructures)
            throw new InvalidDataException("Unsupported or oversized foundation metadata.");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ships = new HashSet<uint>();
        foreach (var structure in Structures)
        {
            if (structure == null || !Guid.TryParseExact(structure.Id, "N", out _) || !identities.Add(structure.Id)
                || !ships.Add(structure.Ship) || !structure.Position.Finite || !Policy.Finite(structure.Position.LengthSquared)
                || structure.Position.LengthSquared < 1 || structure.Position.LengthSquared > 1e24
                || !structure.Rotation.Valid || structure.Revision == 0 || structure.Piers == null
                || structure.Piers.Count == 0 || structure.Piers.Count > Policy.MaximumPiersPerStructure)
                throw new InvalidDataException("Invalid or duplicate grounded structure; physics restoration was refused.");
            var parts = new HashSet<ushort>();
            foreach (var pier in structure.Piers)
                if (pier == null || !Guid.TryParseExact(pier.Id, "N", out _) || !identities.Add(pier.Id) || !parts.Add(pier.Part)
                    || !pier.LocalCap.ValidLocal || !pier.LocalFoot.ValidLocal || !pier.LocalFootNormal.Finite || !Policy.Finite(pier.Length)
                    || Math.Abs(pier.LocalFootNormal.LengthSquared - 1) > 0.01
                    || Math.Abs(pier.LocalCap.Distance(pier.LocalFoot) - pier.Length) > 0.005
                    || pier.Length < Policy.MinimumLength || pier.Length > Policy.MaximumLength + 0.001f)
                    throw new InvalidDataException("Invalid or duplicate foundation pier.");
        }
    }

    internal string Encode()
    {
        Validate();
        var json = JsonConvert.SerializeObject(this, Formatting.None);
        if (json.Length > Policy.MaximumMetadataCharacters) throw new InvalidDataException("Foundation metadata exceeds its supported size.");
        return json;
    }
    internal static GroundDocument Decode(string json)
    {
        if (json == null || json.Length > Policy.MaximumMetadataCharacters) throw new InvalidDataException("Foundation metadata is missing or too large.");
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 24, MissingMemberHandling = MissingMemberHandling.Error };
        var document = JsonConvert.DeserializeObject<GroundDocument>(json, settings) ?? throw new InvalidDataException("Empty foundation metadata.");
        document.Validate();
        return document;
    }
}

internal static class Policy
{
    internal const ushort Protocol = 1;
    internal const ushort PartSettingsId = 61240;
    internal const float MinimumLength = 0.2f;
    internal const float MaximumLength = 6f;
    internal const float BuildReach = 6f;
    internal const double StillSeconds = 0.5;
    internal const double ConvertHoldSeconds = 1.5;
    internal const double ReleaseHoldSeconds = 2.5;
    internal const double MaximumPointSpeed = 0.15;
    internal const double MaximumAngularSpeed = 0.01;
    internal const int MaximumStructures = 4096;
    internal const int MaximumPiersPerStructure = 4096;
    internal const int MaximumMetadataCharacters = 16 * 1024 * 1024;
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool Still(double linearSpeed, double angularSpeed, double radius) =>
        Finite(linearSpeed) && Finite(angularSpeed) && Finite(radius) && radius >= 0 && linearSpeed >= 0 && angularSpeed >= 0
        && angularSpeed <= MaximumAngularSpeed && linearSpeed + angularSpeed * radius <= MaximumPointSpeed;
    internal static bool CanJoin(uint? firstGround, uint? secondGround) => !firstGround.HasValue || !secondGround.HasValue || firstGround == secondGround;
}

// Confirmation is tied to an exact selection and revision, never a client-supplied bool.
internal sealed class Confirmation
{
    private string? _selection;
    private uint _revision;
    private double _began, _last;
    internal bool Observe(string selection, uint revision, double now, bool eligible, double duration)
    {
        if (!eligible || !Policy.Finite(now)) { Reset(); return false; }
        if (_selection != selection || _revision != revision || now < _last || now - _last > 0.4)
        { _selection = selection; _revision = revision; _began = now; }
        _last = now;
        return now - _began >= duration;
    }
    internal void Reset() { _selection = null; _revision = 0; _began = _last = 0; }
}

internal sealed class RequestWindow
{
    private uint _last;
    private double _lastAt = double.NegativeInfinity;
    internal bool Accept(uint sequence, double now, bool rateLimit = true)
    {
        if (sequence == 0 || !Policy.Finite(now) || (_last != 0 && unchecked((int)(sequence - _last)) <= 0) || (rateLimit && now - _lastAt < 0.08)) return false;
        _last = sequence; _lastAt = now; return true;
    }
}
