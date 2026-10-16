using System;

namespace StellarFoundations;

internal enum FoundationPhase : byte
{
    Hidden, Place, Anchor, AddSupport, Inspect, Blocked, Waiting, TerrainLoading, Settling,
    Anchoring, Removing, Releasing, Success, Released, Recycled, Materials
}

internal readonly struct FoundationPrompt
{
    internal const string ReleaseWarning = "The structure will be free to move.";
    internal readonly FoundationPhase Phase;
    internal readonly string Detail;
    internal readonly bool ReleaseRequired;
    internal FoundationPrompt(FoundationPhase phase, string detail = "", bool releaseRequired = false)
    { Phase = phase; Detail = detail; ReleaseRequired = releaseRequired; }
    internal bool HasProgress => Phase == FoundationPhase.Anchoring || Phase == FoundationPhase.Releasing;
    internal bool IsWarning => Phase == FoundationPhase.Releasing || (Phase == FoundationPhase.Removing && ReleaseRequired);
    // The native hologram, cost counter and tool hints already explain ordinary
    // building. Only a failed attempt or moving conversion needs extra text.
    internal string Notice(bool attempted, bool active) => Phase == FoundationPhase.Settling && active ? "Structure is moving."
        : attempted && (Phase == FoundationPhase.Blocked || Phase == FoundationPhase.TerrainLoading) ? Detail : "";
    internal static string ActionLabel(string action, string reason) => reason.Length == 0 ? action : action + ". " + reason;
    internal static string SupportLabel(int count) => count == 1 ? "1 support" : count + " supports";
}

internal static class RemovalPolicy
{
    // Native PartCount includes every hull and stateful part. A connected ship
    // necessarily has a non-foundation connector on this entity as well.
    internal static bool NeedsHold(int foundations, int nativeParts) => foundations == 1 && nativeParts > foundations;
    internal static bool InMountingSpot(double x, double z) => Math.Abs(x) <= 0.36 && Math.Abs(z) <= 0.36;
}

// Tracks only the current interaction. Late replies cannot resurrect cancelled
// progress, play a second success effect, or overwrite the next target's prompt.
internal sealed class FeedbackWindow
{
    private uint _first, _last, _received;
    private bool _open;
    private double _heard;
    internal void Observe(uint sequence, double now = 0)
    {
        if (sequence == 0) return;
        if (!_open) { _first = sequence; _received = sequence - 1; _open = true; _heard = now; }
        _last = sequence;
    }
    internal bool Accept(uint sequence, double now = 0)
    {
        if (!_open || unchecked((int)(sequence - _first)) < 0 || unchecked((int)(_last - sequence)) < 0
            || unchecked((int)(sequence - _received)) <= 0) return false;
        _received = sequence; _heard = now; return true;
    }
    // Sending more observations cannot substitute for hearing from the host.
    internal bool Expired(double now) => _open && now - _heard > 3;
    internal void Close() { _open = false; }
}

internal sealed class HostProgress
{
    private float _progress, _duration;
    private double _received;
    internal void Set(float progress, float duration, double now)
    { _progress = Math.Max(0, Math.Min(1, progress)); _duration = duration; _received = now; }
    internal float At(double now)
    {
        // Interpolate only within the next host observation interval. Never show
        // completed work or keep moving if authoritative updates stop arriving.
        if (_duration <= 0 || now < _received) return _progress;
        return (float)Math.Min(0.98, _progress + Math.Min(0.2, now - _received) / _duration);
    }
    internal void Reset() { _progress = _duration = 0; _received = 0; }
}
