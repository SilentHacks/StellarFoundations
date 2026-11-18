using System;
using Xunit;

namespace StellarFoundations;

public sealed class SaveCompatibilityTests
{
    [Theory]
    [InlineData("975042ef-976a-4f51-b398-11c29df2285f", "KeySeat.SeatConfigs", "AfterSave", "SaveWorld")]
    [InlineData("975042ef-976a-4f51-b398-11c29df2285f", "KeySeat.SeatConfigs", "AfterLoad", "LoadWorld")]
    [InlineData("349cf038-28ef-43da-b2bb-8f8852d8ef08", "Arsenal.Damage.DamageSaves", "AfterSave", "SaveWorld")]
    [InlineData("349cf038-28ef-43da-b2bb-8f8852d8ef08", "Arsenal.Damage.DamageSaves", "AfterLoad", "LoadWorld")]
    [InlineData("d8ef13b4-2e4e-4b2e-9325-f2b4f30b537b", "ShipSchematics.BlueprintSavePatch", "Postfix", "SaveRequest")]
    public void ApprovalRequiresTheInspectedModuleCallbackAndTarget(string moduleId, string type, string callback, string targetName)
    {
        var target = Enum.Parse<SaveCompatibility.Target>(targetName);
        var module = Guid.Parse(moduleId);
        Assert.True(SaveCompatibility.QualifiedPostfix(target, module, type, callback));
        Assert.False(SaveCompatibility.QualifiedPostfix(target, Guid.Empty, type, callback));
        Assert.False(SaveCompatibility.QualifiedPostfix(target, module, type, "OtherCallback"));
        Assert.False(SaveCompatibility.QualifiedPostfix(target, module, null, callback));
        Assert.False(SaveCompatibility.QualifiedPostfix(SaveCompatibility.Target.WriteWorld, module, type, callback));
    }

    [Fact]
    public void LegacyWorldDataWritersRemainUnqualified()
    {
        Assert.False(SaveCompatibility.QualifiedPostfix(SaveCompatibility.Target.SaveWorld,
            Guid.Parse("c9b92993-c6a0-4a90-a742-1c7eb532e6e3"), "BuildZoop.Pairing.SharedVoxel", "AfterSave"));
        Assert.False(SaveCompatibility.QualifiedPostfix(SaveCompatibility.Target.WriteWorld,
            Guid.Parse("f36e75b9-fc56-446e-8a3c-b783105732b4"), "StellarStriderLib.Runtime.PartStateSave", "Saved"));
        Assert.False(SaveCompatibility.QualifiedPostfix(SaveCompatibility.Target.SaveWorld,
            Guid.Parse("2e16133b-f4d0-46c2-a3e0-5a9692f6efdd"), "SafetyRails.Tether", "SavePostfix"));
    }
}
