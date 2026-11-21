using System.Collections.Generic;

namespace WarScript
{
    /// <summary>
    /// The lexer's token cache (T1.9, DEC-19): source text → token list,
    /// least recently used entry evicted beyond <see cref="Capacity"/>.
    /// Every access takes the cache's own lock, so instances on different
    /// threads can share it; the lists it holds are read-only.
    /// </summary>
    internal sealed class TokenCache
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, IReadOnlyList<Token.Token>>>> _map =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, IReadOnlyList<Token.Token>>>>(System.StringComparer.Ordinal);
        // Most recently used first.
        private readonly LinkedList<KeyValuePair<string, IReadOnlyList<Token.Token>>> _order =
            new LinkedList<KeyValuePair<string, IReadOnlyList<Token.Token>>>();

        public int Capacity { get; }

        public TokenCache(int capacity)
        {
            if (capacity < 1) throw new System.ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Count
        {
            get { lock (_lock) return _map.Count; }
        }

        public bool TryGet(string source, out IReadOnlyList<Token.Token> tokens)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(source, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    tokens = node.Value.Value;
                    return true;
                }
            }
            tokens = null!;
            return false;
        }

        /// <summary>
        /// Caches <paramref name="tokens"/> for <paramref name="source"/> and
        /// returns the list now cached for it: when another thread cached the
        /// same source first, its list wins, so every caller shares one list.
        /// </summary>
        public IReadOnlyList<Token.Token> Add(string source, IReadOnlyList<Token.Token> tokens)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(source, out var existing))
                {
                    _order.Remove(existing);
                    _order.AddFirst(existing);
                    return existing.Value.Value;
                }
                var node = _order.AddFirst(new KeyValuePair<string, IReadOnlyList<Token.Token>>(source, tokens));
                _map[source] = node;
                while (_map.Count > Capacity)
                {
                    var last = _order.Last!;
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
                return tokens;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _order.Clear();
            }
        }
    }
}
