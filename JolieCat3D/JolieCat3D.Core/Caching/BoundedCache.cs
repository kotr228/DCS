namespace JolieCat3D.Core.Caching
{
    /// <summary>
    /// A plain least-recently-used cache, bounded to <see cref="Capacity"/> entries -
    /// the actual fix for the kind of unbounded-growth memory leak an ordinary
    /// <c>Dictionary&lt;TKey,TValue&gt;</c> "cache" (one that's written to but never
    /// evicted from) becomes over a long-running session: <c>Engine.Geometry.MaterialFactory</c>'s
    /// own per-texture-path averaged-color caches used a plain, ever-growing Dictionary
    /// before this existed - harmless for a handful of textures, but a real, accumulating
    /// leak across a session that loads/hot-reloads many DIFFERENT texture files one
    /// after another (an editing session doesn't just re-save the SAME file - a real
    /// workflow tries many different images).
    ///
    /// A GET of an existing key counts as a use (moves that entry to the front, matching
    /// standard LRU semantics) - a texture referenced by an ACTIVE material stays cached
    /// as long as it keeps being asked for; only entries nobody has looked up recently
    /// are the ones eventually evicted to make room. Deliberately not thread-safe (no
    /// lock of its own) - every consumer today (<c>MaterialFactory</c>) is only ever
    /// touched from the UI/render thread, the same single-threaded assumption the rest of
    /// this project's own caches (e.g. <c>Service.Commands.CommandHistory</c>) already
    /// make.
    /// </summary>
    public sealed class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
        private readonly LinkedList<(TKey Key, TValue Value)> _order = new(); // front = most recently used, back = least

        public int Capacity { get; }

        public int Count => _map.Count;

        public BoundedCache(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "BoundedCache needs a capacity of at least 1.");
            Capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>();
        }

        /// <summary>Looks up <paramref name="key"/>, marking it most-recently-used if
        /// found - false (with <paramref name="value"/> left at its default) if it isn't
        /// cached at all (never inserted, or evicted since).</summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>Inserts or replaces <paramref name="key"/>'s own cached value,
        /// marking it most-recently-used - if this pushes <see cref="Count"/> past
        /// <see cref="Capacity"/>, the single LEAST-recently-used entry (never the one
        /// just inserted) is evicted to make room, keeping <see cref="Count"/> at or
        /// under <see cref="Capacity"/> permanently, no matter how many distinct keys are
        /// ever set over the cache's whole lifetime.</summary>
        public void Set(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing)) _order.Remove(existing);

            var node = new LinkedListNode<(TKey, TValue)>((key, value));
            _order.AddFirst(node);
            _map[key] = node;

            if (_map.Count > Capacity)
            {
                var least = _order.Last!;
                _order.RemoveLast();
                _map.Remove(least.Value.Key);
            }
        }

        /// <summary>Drops <paramref name="key"/>'s own entry, if any - false (a no-op) if
        /// it wasn't cached. Used to explicitly invalidate one entry (a texture file that
        /// no longer resolves, say) without waiting for LRU eviction to eventually get to
        /// it on its own.</summary>
        public bool Remove(TKey key)
        {
            if (!_map.TryGetValue(key, out var node)) return false;
            _order.Remove(node);
            _map.Remove(key);
            return true;
        }

        public void Clear()
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
