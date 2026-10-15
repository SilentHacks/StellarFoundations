using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace StellarFoundations;

public sealed class PolicyTests
{
    [Theory]
    [InlineData(1234, true)] [InlineData(0.25, true)] [InlineData(1e8, false)] [InlineData(1e300, false)]
    public void NetworkCoordinatesMustBeRepresentableWithinTheLocalPhysicsBudget(double value, bool expected) =>
        Assert.Equal(expected, new PositionD(value, 0, 0).ValidLocal);
    [Theory]
    [InlineData(0.01, 0.001, 100, true)]
    [InlineData(0, 0.002, 100, false)]
    [InlineData(0, 0.02, 1, false)]
    [InlineData(0.16, 0, 0, false)]
    [InlineData(0, 0, 10000, true)]
    public void StillnessIncludesDistantEdgeMovement(double speed, double angular, double radius, bool expected) => Assert.Equal(expected, Policy.Still(speed, angular, radius));

    [Fact] public void InvalidMovementIsRejected()
    { Assert.False(Policy.Still(double.NaN, 0, 1)); Assert.False(Policy.Still(0, double.PositiveInfinity, 1)); Assert.False(Policy.Still(0, 0, -1)); }

    [Fact] public void ConfirmationCannotSurviveTargetChangeRevisionOrInterruption()
    {
        var confirmation = new Confirmation();
        for (var time = 0d; time <= 2; time += 0.2) Assert.Equal(time >= 1.5, confirmation.Observe("base", 1, time, true, 1.5));
        Assert.False(confirmation.Observe("base", 2, 2.1, true, 1.5));
        Assert.False(confirmation.Observe("other", 2, 2.2, true, 1.5));
        Assert.False(confirmation.Observe("other", 2, 5, true, 1.5));
        Assert.False(confirmation.Observe("other", 2, 5.1, false, 1.5));
        Assert.False(confirmation.Observe("other", 2, 5.2, true, 1.5));
    }

    [Fact] public void ReplayAndBurstRequestsCannotMutateTwice()
    {
        var window = new RequestWindow();
        Assert.True(window.Accept(1, 0)); Assert.False(window.Accept(1, 1));
        Assert.False(window.Accept(2, 0.01)); Assert.True(window.Accept(2, 0.1)); Assert.False(window.Accept(1, 0.3));
    }

    [Fact] public void ReleasingTheButtonAlwaysCancelsEvenInsideTheBuildRateLimit()
    {
        var window = new RequestWindow();
        Assert.True(window.Accept(1, 0)); Assert.True(window.Accept(2, 0.01, rateLimit: false));
        Assert.False(window.Accept(2, 1, rateLimit: false));
    }

    [Fact] public void TwoIndependentGroundsCannotJoinThroughAVisitingShip()
    { Assert.True(Policy.CanJoin(1, null)); Assert.True(Policy.CanJoin(1, 1)); Assert.False(Policy.CanJoin(1, 2)); }

    internal static GroundDocument Document() => new GroundDocument { Structures = new List<GroundRecord> { new GroundRecord
    { Id = Guid.NewGuid().ToString("N"), Ship = 42, Planet = ulong.MaxValue, Position = new PositionD(9000000.123456, 12, -5),
      Rotation = new RotationF(0, 0, 0, 1), Revision = 1, Piers = new List<PierRecord> { new PierRecord
      { Id = Guid.NewGuid().ToString("N"), Part = 9, LocalCap = new PositionD(2, 3, 4), LocalFoot = new PositionD(2, 1, 4), LocalFootNormal = new PositionD(0, 1, 0), Length = 2 } } } } };

    [Fact] public void SaveRetainsPlanetIdentityPrecisionAndSupportIdentity()
    {
        var original = Document(); var restored = GroundDocument.Decode(original.Encode());
        Assert.Equal(original.Structures[0].Position.X, restored.Structures[0].Position.X);
        Assert.Equal(ulong.MaxValue, restored.Structures[0].Planet);
        Assert.Equal(original.Structures[0].Piers[0].Id, restored.Structures[0].Piers[0].Id);
    }

    [Theory] [InlineData("duplicate")] [InlineData("length")] [InlineData("pose")] [InlineData("version")] [InlineData("zero-support")]
    [InlineData("normal")] [InlineData("inconsistent-length")] [InlineData("unbounded-part")]
    [InlineData("overflowing-pose")]
    public void InvalidSavedBasesFailClosed(string fault)
    {
        var document = Document(); var ground = document.Structures[0];
        if (fault == "duplicate") ground.Piers.Add(ground.Piers[0]);
        if (fault == "length") ground.Piers[0].Length = 7;
        if (fault == "pose") ground.Position.X = double.NaN;
        if (fault == "version") document.Format = 2;
        if (fault == "zero-support") ground.Piers.Clear();
        if (fault == "normal") ground.Piers[0].LocalFootNormal = default;
        if (fault == "inconsistent-length") ground.Piers[0].Length = 1;
        if (fault == "unbounded-part") ground.Piers[0].LocalFoot.X = 1e8;
        if (fault == "overflowing-pose") ground.Position.X = 1e300;
        Assert.Throws<InvalidDataException>(() => document.Encode());
    }

    [Theory] [InlineData("prepared")] [InlineData("world.json")] [InlineData("sdmodkit-content.json")]
    public void InterruptedSaveRestoresBothFiles(string checkpoint)
    {
        var directory = Path.Combine(Path.GetTempPath(), "StellarFoundationsTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "world.json"), "old world"); File.WriteAllText(Path.Combine(directory, "sdmodkit-content.json"), "old metadata");
            var files = new Dictionary<string, byte[]> { ["world.json"] = Encoding.UTF8.GetBytes("new world"), ["sdmodkit-content.json"] = Encoding.UTF8.GetBytes("new metadata") };
            Assert.Throws<IOException>(() => AtomicSaveBundle.Commit(directory, files, stage => { if (stage == checkpoint) throw new IOException("interrupted"); }));
            Assert.Equal("old world", File.ReadAllText(Path.Combine(directory, "world.json"))); Assert.Equal("old metadata", File.ReadAllText(Path.Combine(directory, "sdmodkit-content.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact] public void CompletedSaveCommitsBothFilesAndHasNoRecoveryPending()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StellarFoundationsTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            AtomicSaveBundle.Commit(directory, new Dictionary<string, byte[]> { ["world.json"] = Encoding.UTF8.GetBytes("new"), ["sdmodkit-content.json"] = Encoding.UTF8.GetBytes("paired") });
            Assert.Equal("new", File.ReadAllText(Path.Combine(directory, "world.json"))); Assert.False(AtomicSaveBundle.Recover(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
