namespace RockfaxDesk;

/// <summary>
/// Back/forward history with browser semantics, extracted so the edge cases are testable.
/// Opening the page you are already on refreshes its payload in place (F5-style) instead
/// of pushing a duplicate entry, so back never lands on the page you are looking at.
/// </summary>
internal sealed class NavHistory
{
    private readonly List<NavEntry> _back = new();
    private readonly List<NavEntry> _forward = new();

    private const int Cap = 100; // deep histories just waste memory

    public NavEntry? Current { get; private set; }
    public IReadOnlyList<NavEntry> BackStack => _back;
    public IReadOnlyList<NavEntry> ForwardStack => _forward;
    public bool CanBack => _back.Count > 0;
    public bool CanForward => _forward.Count > 0;

    /// <summary>Records an opened page. Same-page re-opens keep the position but adopt
    /// the new payload (a later open usually carries richer details than the entry it supersedes).</summary>
    public void Push(NavEntry entry)
    {
        if (Current is { } cur && cur.Kind == entry.Kind && cur.UkcId == entry.UkcId)
        {
            Current = entry;
            return;
        }
        if (Current is { } previous)
        {
            _back.Add(previous);
            if (_back.Count > Cap) _back.RemoveAt(0);
        }
        _forward.Clear();
        Current = entry;
    }

    /// <summary>Steps back; null when the stack is empty. The current page moves to the forward stack.</summary>
    public NavEntry? Back()
    {
        if (_back.Count == 0) return null;
        NavEntry entry = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        if (Current is { } cur) _forward.Insert(0, cur);
        Current = entry;
        return entry;
    }

    /// <summary>Steps forward; null when the stack is empty. The current page moves to the back stack.</summary>
    public NavEntry? Forward()
    {
        if (_forward.Count == 0) return null;
        NavEntry entry = _forward[0];
        _forward.RemoveAt(0);
        if (Current is { } cur) _back.Add(cur);
        Current = entry;
        return entry;
    }
}
