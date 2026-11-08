namespace Backpack.Cheats;

/// <summary>
/// Tracks synchronous temporary capacity overrides by owner. Nested calls reuse
/// the outer effective capacity and only the outermost exit restores the base.
/// </summary>
internal sealed class TemporaryCapacityOverrideScopes<TKey> where TKey : notnull
{
    internal sealed class Frame
    {
        public Frame(int originalCapacity, int effectiveCapacity, bool changed)
        {
            OriginalCapacity = originalCapacity;
            EffectiveCapacity = effectiveCapacity;
            Changed = changed;
            Depth = 1;
        }

        public int OriginalCapacity { get; }
        public int EffectiveCapacity { get; }
        public bool Changed { get; }
        public int Depth { get; set; }
    }

    internal sealed class Lease
    {
        internal readonly Frame Frame;
        internal bool Exited;

        internal Lease(TKey owner, Frame frame)
        {
            Owner = owner;
            Frame = frame;
        }

        public TKey Owner { get; }
        public int EffectiveCapacity => Frame.EffectiveCapacity;
        public bool ShouldApply => Frame.Changed;
    }

    private readonly Dictionary<TKey, Frame> _frames = new();

    public Lease Enter(TKey owner, int currentCapacity, int multiplier, bool enabled)
    {
        if (_frames.TryGetValue(owner, out var currentFrame))
        {
            currentFrame.Depth++;
            return new Lease(owner, currentFrame);
        }

        var effective = ProductionQuantityPolicy.GetEffectiveMaximum(currentCapacity, multiplier, enabled);
        var frame = new Frame(currentCapacity, effective, effective > currentCapacity);
        _frames.Add(owner, frame);
        return new Lease(owner, frame);
    }

    public bool Exit(Lease lease, out int originalCapacity)
    {
        originalCapacity = 0;
        if (lease == null || lease.Exited)
            return false;

        lease.Exited = true;
        if (!_frames.TryGetValue(lease.Owner, out var currentFrame)
            || !ReferenceEquals(currentFrame, lease.Frame))
        {
            return false;
        }

        currentFrame.Depth--;
        if (currentFrame.Depth > 0)
            return false;

        _frames.Remove(lease.Owner);
        if (!currentFrame.Changed)
            return false;

        originalCapacity = currentFrame.OriginalCapacity;
        return true;
    }
}
