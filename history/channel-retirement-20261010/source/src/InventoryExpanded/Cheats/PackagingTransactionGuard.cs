namespace Backpack.Cheats;

/// <summary>
/// A small owner-scoped re-entrancy guard. It protects the repeated native
/// packaging call from recursively expanding its own PackSingleInstance hook.
/// </summary>
internal sealed class PackagingTransactionGuard<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> _activeOwners = new();

    public bool TryEnter(TKey owner, out Lease lease)
    {
        if (!_activeOwners.Add(owner))
        {
            lease = null;
            return false;
        }

        lease = new Lease(this, owner);
        return true;
    }

    public bool IsHeld(TKey owner) => _activeOwners.Contains(owner);

    private void Exit(TKey owner)
    {
        _activeOwners.Remove(owner);
    }

    internal sealed class Lease : IDisposable
    {
        private readonly PackagingTransactionGuard<TKey> _guard;
        private bool _disposed;

        internal Lease(PackagingTransactionGuard<TKey> guard, TKey owner)
        {
            _guard = guard;
            Owner = owner;
        }

        public TKey Owner { get; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _guard.Exit(Owner);
        }
    }
}
