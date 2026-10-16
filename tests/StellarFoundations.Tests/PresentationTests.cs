using System;
using System.Reflection;
using FishNet.Serializing;
using TMPro;
using Tools.Build;
using UI.ToolBar;
using UnityEngine;
using Xunit;

namespace StellarFoundations;

public sealed class PresentationTests
{
    [Fact]
    public void LastSupportHasADistinctWarningAndAdditionalSupportsStayOrdinary()
    {
        var last = new FoundationPrompt(FoundationPhase.Removing, releaseRequired: true);
        Assert.True(last.IsWarning);
        var redundant = new FoundationPrompt(FoundationPhase.Removing);
        Assert.False(redundant.IsWarning);
        Assert.Equal("1 support", FoundationPrompt.SupportLabel(1));
        Assert.Equal("2 supports", FoundationPrompt.SupportLabel(2));
    }
    [Fact]
    public void CancelledAndOutOfOrderRepliesCannotResurrectAnInteraction()
    {
        var window = new FeedbackWindow(); window.Observe(10); window.Observe(11);
        Assert.False(window.Accept(9)); Assert.False(window.Accept(12));
        Assert.True(window.Accept(11)); Assert.False(window.Accept(10)); Assert.False(window.Accept(11));
        window.Close(); Assert.False(window.Accept(11));
        window.Observe(13); Assert.False(window.Accept(11)); Assert.True(window.Accept(13));
    }
    [Fact]
    public void ContinuedRequestsCannotKeepAnUnresponsiveHostAlive()
    {
        var window = new FeedbackWindow(); window.Observe(1, 10);
        for (uint sequence = 2; sequence <= 20; sequence++) window.Observe(sequence, 10 + sequence * 0.2);
        Assert.True(window.Expired(13.1));
        window.Close(); Assert.False(window.Expired(14));
    }
    [Fact]
    public void OnlyAcceptedFeedbackExtendsTheResponseDeadline()
    {
        var window = new FeedbackWindow(); window.Observe(1, 10); window.Observe(2, 10.2);
        Assert.True(window.Accept(2, 12.8)); Assert.False(window.Expired(15.7));
        Assert.False(window.Accept(1, 15.7)); Assert.True(window.Expired(15.9));
        Assert.False(window.Accept(2, 15.95)); window.Observe(3, 16);
        Assert.True(window.Expired(16)); Assert.True(window.Accept(3, 16)); Assert.False(window.Expired(16.1));
    }
    [Fact]
    public void ProgressStopsWhenHostUpdatesStopAndCannotShowCompletionEarly()
    {
        var progress = new HostProgress(); progress.Set(0.4f, 2, 10);
        Assert.InRange(progress.At(10.1), 0.449f, 0.451f);
        Assert.InRange(progress.At(30), 0.499f, 0.501f);
        progress.Set(0.95f, 1.5f, 40); Assert.Equal(0.98f, progress.At(50));
        progress.Reset(); Assert.Equal(0, progress.At(51));
    }
    [Fact]
    public void HostProgressResetsWhenTargetOrRevisionChanges()
    {
        var hold = new Confirmation();
        Assert.False(hold.Observe("anchor", 1, 1, true, 1.5));
        Assert.False(hold.Observe("anchor", 1, 1.3, true, 1.5));
        Assert.InRange(hold.Fraction(1.3, 1.5), 0.199f, 0.201f);
        Assert.False(hold.Observe("anchor", 2, 1.4, true, 1.5)); Assert.Equal(0, hold.Fraction(1.4, 1.5));
        Assert.Equal(0, hold.Fraction(3, 1.5));
        hold.Reset(); Assert.Equal(0, hold.Fraction(3, 1.5));
    }
    [Fact]
    public void TypedFeedbackRoundTripsWithoutInferringAStateFromEnglishMessages()
    {
        var value = new FoundationReply { Sequence = 123, Message = "Custom or localized text", Phase = FoundationPhase.Releasing,
            Progress = 0.4f, Duration = 2.5f, Supports = 1, ReleaseRequired = true };
        var writer = new Writer(); FoundationNetwork.WriteReply(writer, value);
        var read = FoundationNetwork.ReadReply(new Reader(writer.GetArraySegment(), null));
        Assert.Equal(value.Phase, read.Phase); Assert.Equal(value.Progress, read.Progress);
        Assert.Equal(value.Duration, read.Duration); Assert.Equal(value.Message, read.Message); Assert.Equal(1, read.Supports);
        Assert.True(read.ReleaseRequired);
    }
    [Theory]
    [InlineData("progress")] [InlineData("duration")] [InlineData("count")] [InlineData("phase")]
    public void InvalidFeedbackCannotCorruptTheHud(string fault)
    {
        var value = new FoundationReply { Message = "", Phase = FoundationPhase.Anchoring, Duration = 1.5f };
        if (fault == "progress") value.Progress = float.NaN;
        if (fault == "duration") value.Duration = -1;
        if (fault == "count") value.Supports = -1;
        if (fault == "phase") value.Phase = (FoundationPhase)255;
        var writer = new Writer(); FoundationNetwork.WriteReply(writer, value);
        Assert.Throws<FormatException>(() => FoundationNetwork.ReadReply(new Reader(writer.GetArraySegment(), null)));
    }
    [Fact]
    public void NativeHudAndPreviewAdaptersBindTheInspectedPrefabFields()
    {
        Assert.Equal(typeof(ToolKeyTipUi), Compatibility.Field(typeof(ToolKeyTipsUpdater), "toolKeyTipPrefab").FieldType);
        Assert.Equal(typeof(TMP_Text), Compatibility.Field(typeof(ToolKeyTipUi), "label").FieldType);
        Assert.Equal(typeof(Material), Compatibility.Field(typeof(PartPreview), "hologramMat").FieldType);
        Assert.Equal(typeof(Color), Compatibility.Field(typeof(PartPreview), "validColor").FieldType);
        Assert.NotNull(typeof(ToolKeyTipsUpdater).GetMethod("GenerateTips", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(ToolKeyTipsUpdater).GetMethod("UpdateKeyTips", BindingFlags.NonPublic | BindingFlags.Instance));
        var palette = Compatibility.Field(typeof(UI.BuildCostTip.BuildCostItemTip), "colorPalette").FieldType;
        Assert.Equal(typeof(Color), Compatibility.Field(palette, "uiBackgroundColor").FieldType);
        Assert.NotNull(typeof(Tools.Recycler.RecyclerTool).GetMethod("UpdateHoveredObject", BindingFlags.NonPublic | BindingFlags.Instance));
    }
    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, true)]
    [InlineData(1, 5557, true)]
    [InlineData(2, 2, false)]
    [InlineData(2, 5557, false)]
    public void OnlyFinalFoundationsWithActualContentsNeedConfirmation(int supports, int parts, bool expected)
    {
        Assert.Equal(expected, RemovalPolicy.NeedsHold(supports, parts));
        var prompt = new FoundationPrompt(FoundationPhase.Removing, releaseRequired: expected);
        Assert.Equal(expected, prompt.IsWarning);
    }
    [Theory]
    [InlineData(0, 0, true)] [InlineData(0.35, -0.35, true)]
    [InlineData(0.37, 0, false)] [InlineData(0, 1, false)]
    public void FloorFallbackOnlyCoversTheVisibleMountingSpot(double x, double z, bool expected) =>
        Assert.Equal(expected, RemovalPolicy.InMountingSpot(x, z));
    [Theory]
    [InlineData("Hold to anchor structure", "Stops movement and propulsion.", "Hold to anchor structure. Stops movement and propulsion.")]
    [InlineData("Hold to remove foundation", "Last support. Releases the structure.", "Hold to remove foundation. Last support. Releases the structure.")]
    [InlineData("Place foundation", "", "Place foundation")]
    public void NativeActionHintsSeparateSentencesWithoutExtraPunctuation(string action, string reason, string expected)
    {
        Assert.Equal(expected, FoundationPrompt.ActionLabel(action, reason));
    }
    [Fact]
    public void NativeVisualFeedbackNeverProducesAnExplanation()
    {
        foreach (var phase in new[] { FoundationPhase.Place, FoundationPhase.Anchor, FoundationPhase.AddSupport, FoundationPhase.Inspect,
            FoundationPhase.Materials, FoundationPhase.Waiting, FoundationPhase.Success, FoundationPhase.Released,
            FoundationPhase.Recycled, FoundationPhase.Removing, FoundationPhase.Releasing, FoundationPhase.Anchoring })
        {
            var prompt = new FoundationPrompt(phase, "This must not become a dialogue.", true);
            Assert.Empty(prompt.Notice(false, false)); Assert.Empty(prompt.Notice(true, true));
        }
    }
    [Fact]
    public void ObstructionTextRequiresAnAttemptAndMovingConversionsRequireActiveInput()
    {
        var blocked = new FoundationPrompt(FoundationPhase.Blocked, "Ground is out of reach.");
        Assert.Empty(blocked.Notice(false, false)); Assert.Equal(blocked.Detail, blocked.Notice(true, false));
        var moving = new FoundationPrompt(FoundationPhase.Settling);
        Assert.Empty(moving.Notice(false, false)); Assert.Equal("Structure is moving.", moving.Notice(false, true));
        Assert.False(moving.HasProgress);
    }
    [Fact]
    public void ShortConfirmationStillRequiresContinuousObservationsAndAFullFreshHold()
    {
        Assert.InRange(Policy.ReleaseHoldSeconds, 0.5, 1);
        Assert.InRange(Policy.ConvertHoldSeconds, Policy.StillSeconds, 1);
        var hold = new Confirmation();
        foreach (var time in new[] { 0, 0.2, 0.4, 0.6 })
            Assert.False(hold.Observe("last", 1, time, true, Policy.ReleaseHoldSeconds));
        hold.Reset();
        foreach (var time in new[] { 0.7, 0.9, 1.1, 1.3 })
            Assert.False(hold.Observe("last", 1, time, true, Policy.ReleaseHoldSeconds));
        Assert.True(hold.Observe("last", 1, 1.6, true, Policy.ReleaseHoldSeconds));
    }
}
