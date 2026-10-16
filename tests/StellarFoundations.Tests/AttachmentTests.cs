using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Ships.Interface.Model.Placement;
using UnityEngine;
using Xunit;

namespace StellarFoundations;

public sealed class AttachmentTests
{
    [Theory]
    [InlineData(-0.1f, AttachmentKind.Contact)]
    [InlineData(-0.05f, AttachmentKind.Contact)]
    [InlineData(0f, AttachmentKind.Contact)]
    [InlineData(0.05f, AttachmentKind.Contact)]
    [InlineData(0.2f, AttachmentKind.Contact)]
    [InlineData(0.201f, AttachmentKind.Pier)]
    [InlineData(6f, AttachmentKind.Pier)]
    internal void TerrainContactSelectsTheShapeWithoutMovingTheFloor(float clearance, AttachmentKind expected)
    {
        Assert.True(Policy.ClassifyAttachment(clearance, out var kind, out var reason), reason);
        Assert.Equal(expected, kind);
        Assert.True(Policy.ValidAttachment(kind, Math.Abs(clearance), clearance, true, true));
    }

    [Theory]
    [InlineData(-0.101, "buried")]
    [InlineData(6.001, "six metre")]
    [InlineData(double.NaN, "Invalid")]
    [InlineData(double.PositiveInfinity, "Invalid")]
    public void InvalidTerrainGetsAnAccurateReason(double clearance, string message)
    {
        Assert.False(Policy.ClassifyAttachment(clearance, out _, out var reason));
        Assert.Contains(message, reason);
    }

    private static GroundDocument Contact(float clearance)
    {
        var document = PolicyTests.Document();
        var pier = document.Structures[0].Piers[0];
        pier.LocalCap = new PositionD(2, 2.45, 4);
        pier.LocalFoot = new PositionD(2, 2.45 - clearance, 4);
        pier.Length = Math.Abs(clearance); pier.Clearance = clearance;
        pier.Kind = AttachmentKind.Contact; pier.RequiresFloor = true; pier.HasCollar = true;
        return document;
    }

    [Theory]
    [InlineData(0f)] [InlineData(-0.05f)] [InlineData(0.15f)]
    public void FlushAndEmbeddedAttachmentsRoundTripWithTheirOriginalPose(float clearance)
    {
        var original = Contact(clearance);
        var restored = GroundDocument.Decode(original.Encode());
        Assert.Equal(2, restored.Format);
        Assert.Equal(original.Structures[0].Position.X, restored.Structures[0].Position.X);
        Assert.Equal(original.Structures[0].Rotation.W, restored.Structures[0].Rotation.W);
        var pier = restored.Structures[0].Piers[0];
        Assert.Equal(AttachmentKind.Contact, pier.Kind);
        Assert.Equal(clearance, pier.Clearance);
        Assert.Equal(Math.Abs(clearance), pier.Length);
        Assert.True(pier.RequiresFloor); Assert.True(pier.HasCollar);
    }

    [Theory]
    [InlineData("no-floor")] [InlineData("hidden-plate")] [InlineData("buried")]
    [InlineData("extended-contact")] [InlineData("unknown-kind")] [InlineData("signed-length")]
    [InlineData("negative-length")] [InlineData("off-grid")]
    public void MalformedContactMetadataFailsClosed(string fault)
    {
        var document = Contact(0);
        var pier = document.Structures[0].Piers[0];
        if (fault == "no-floor") pier.RequiresFloor = false;
        if (fault == "hidden-plate") pier.HasCollar = false;
        if (fault == "buried") { pier.Clearance = -0.11f; pier.Length = 0.11f; pier.LocalFoot.Y += 0.11; }
        if (fault == "extended-contact") { pier.Clearance = 1; pier.Length = 1; pier.LocalFoot.Y -= 1; }
        if (fault == "unknown-kind") pier.Kind = (AttachmentKind)9;
        if (fault == "signed-length") pier.Clearance = 0.1f;
        if (fault == "negative-length") pier.Length = -0.001f;
        if (fault == "off-grid") { pier.LocalCap.X += 0.2; pier.LocalFoot.X += 0.2; }
        Assert.Throws<InvalidDataException>(() => document.Encode());
    }

    private static JObject LegacyDocument()
    {
        var json = JObject.Parse(PolicyTests.Document().Encode());
        json["Format"] = 1;
        var pier = (JObject)json["Structures"]![0]!["Piers"]![0]!;
        foreach (var property in new[] { "Kind", "Clearance", "RequiresFloor", "HasCollar" }) pier.Remove(property);
        return json;
    }

    [Fact]
    public void ExistingPiersMigrateWithoutChangingTheirPoseGeometryOrFloorDependency()
    {
        var legacy = LegacyDocument();
        var migrated = GroundDocument.Decode(legacy.ToString());
        Assert.Equal(2, migrated.Format);
        var pier = migrated.Structures[0].Piers[0];
        Assert.Equal(AttachmentKind.Pier, pier.Kind);
        Assert.Equal(2f, pier.Clearance);
        Assert.False(pier.RequiresFloor); Assert.False(pier.HasCollar);
        Assert.Equal(3, pier.LocalCap.Y);
        Assert.Equal(1, pier.LocalFoot.Y);
        Assert.Equal(9000000.123456, migrated.Structures[0].Position.X);
        Assert.Equal(2f, GroundDocument.Decode(migrated.Encode()).Structures[0].Piers[0].Length);
    }

    [Fact]
    public void CorruptLegacyPiersCannotBecomeContactAnchorsThroughMigration()
    {
        var legacy = LegacyDocument();
        legacy["Structures"]![0]!["Piers"]![0]!["Length"] = 0;
        Assert.Throws<InvalidDataException>(() => GroundDocument.Decode(legacy.ToString()));
    }

    [Fact]
    public void AConvertedAnchorDependsOnItsExactFloor()
    {
        var pier = Contact(0).Structures[0].Piers[0];
        Assert.True(Geometry.SupportsFloor(pier, new HullPartPosition(new Vector3Int(2, 3, 4), VoxelPartType.Floor)));
        Assert.False(Geometry.SupportsFloor(pier, new HullPartPosition(new Vector3Int(2, 4, 4), VoxelPartType.Floor)));
        Assert.False(Geometry.SupportsFloor(pier, new HullPartPosition(new Vector3Int(2, 3, 4), VoxelPartType.RightWall)));
        pier.RequiresFloor = false;
        Assert.False(Geometry.SupportsFloor(pier, Geometry.FloorAtCap(Geometry.Vector(pier.LocalCap))));
    }

    [Fact]
    public void NewFloorsSnapToTheExposedCollarAndTheOriginalMountingPlate()
    {
        Assert.True(Policy.CanSnapFirstFloor(0, 0.2, 0, 1, true));
        Assert.True(Policy.CanSnapFirstFloor(0, 0, 0, 1, false));
        Assert.False(Policy.CanSnapFirstFloor(0, 0.2, 0, 1, false));
        Assert.False(Policy.CanSnapFirstFloor(0, 0.2, 0, 0, true));
        Assert.False(Policy.CanSnapFirstFloor(0.5, 0.2, 0, 1, true));
        Assert.False(Policy.CanSnapFirstFloor(0, 0.6, 0, 1, true));
    }

    [Theory]
    [InlineData(3)] [InlineData(-17)] [InlineData(100000)] [InlineData(-100000)]
    public void FloorMountValidationRetainsNativeFloatPrecisionAcrossLargeGrids(int y)
    {
        var cap = new PositionD(2, (float)y - 0.5f - 0.05f, 4);
        Assert.True(Policy.IsFloorMount(cap));
        Assert.Equal(y, Geometry.FloorAtCap(Geometry.Vector(cap)).VoxelCoord.y);
    }
}
