namespace TraceDeckFE.Models;

/// <summary>Bounded logical edits. Snapshots share immutable image sources; never serialized.</summary>
public sealed class SessionHistory<T>
{
    public const int DefaultCapacity = 100;
    public static readonly TimeSpan BurstDelay = TimeSpan.FromMilliseconds(400);
    private readonly int _capacity;
    private readonly Func<T, T, bool> _equals;
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    private T _committed;
    private T _saved;
    private bool _hasSaved = true;
    private bool _grouping;
    private string? _burst;
    private DateTimeOffset _lastInput;

    public SessionHistory(T initial, Func<T, T, bool>? equals = null, int capacity = DefaultCapacity)
    {
        Current = _committed = _saved = initial;
        _equals = equals ?? EqualityComparer<T>.Default.Equals;
        _capacity = Math.Max(1, capacity);
    }
    public T Current { get; private set; }
    public bool IsDirty => !_hasSaved || !_equals(Current, _saved);
    public bool CanUndo => _undo.Count > 0 || !_equals(Current, _committed);
    public bool CanRedo => _redo.Count > 0 && _equals(Current, _committed);
    public int UndoCount => _undo.Count + (!_equals(Current, _committed) ? 1 : 0);
    public int RedoCount => _redo.Count;
    public bool IsGrouping => _grouping;
    public event EventHandler? Changed;

    public void Observe(T state)
    {
        if (_equals(Current, state)) return;
        Current = state;
        _redo.Clear();
        if (!_grouping) Commit();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void BeginGesture()
    {
        EndGesture();
        _grouping = true;
    }
    public void TouchBurst(string key, DateTimeOffset now)
    {
        if (_burst != key || now - _lastInput >= BurstDelay)
        {
            BeginGesture();
            _burst = key;
        }
        _lastInput = now;
    }
    public void CompleteBurst(DateTimeOffset now)
    {
        if (_burst is not null && now - _lastInput >= BurstDelay) EndGesture();
    }
    public void EndGesture()
    {
        Commit();
        _grouping = false;
        _burst = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Commit()
    {
        if (_equals(Current, _committed)) return;
        _undo.Add(_committed);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _committed = Current;
    }
    public T Undo()
    {
        EndGesture();
        if (_undo.Count == 0) return Current;
        _redo.Add(Current);
        Current = _committed = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }
    public T Redo()
    {
        EndGesture();
        if (_redo.Count == 0) return Current;
        _undo.Add(Current);
        Current = _committed = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }
    public void MarkSaved(T saved) { _saved = saved; _hasSaved = true; Changed?.Invoke(this, EventArgs.Empty); }
    public void Reset(T state, bool recovered = false)
    {
        _undo.Clear(); _redo.Clear(); _grouping = false; _burst = null;
        Current = _committed = _saved = state;
        _hasSaved = !recovered;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
