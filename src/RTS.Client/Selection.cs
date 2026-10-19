using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

/// <summary>Current selection + control groups (ids only; resolved against the World on use).</summary>
public sealed class SelectionManager
{
    public readonly List<int> Ids = new();
    readonly List<int>[] _groups = Enumerable.Range(0, 10).Select(_ => new List<int>()).ToArray();
    int _lastRecallGroup = -1;
    float _lastRecallTime = -10;

    public int Count => Ids.Count;
    public bool Contains(int id) => Ids.Contains(id);
    public IReadOnlyList<int> Group(int g) => _groups[g];

    public void Clear() => Ids.Clear();
    public void Set(int id) { Ids.Clear(); Ids.Add(id); }
    public void Set(IEnumerable<int> ids) { Ids.Clear(); foreach (var i in ids) if (!Ids.Contains(i)) Ids.Add(i); }
    public void Add(IEnumerable<int> ids) { foreach (var i in ids) if (!Ids.Contains(i)) Ids.Add(i); }
    public void Toggle(int id) { if (!Ids.Remove(id)) Ids.Add(id); }

    public static bool IsLive(Entity? e)
        => e != null && (e.Kind == EntityKind.CrystalField ? (e.Resources > 0 || e.Alive) : e.Alive);

    public void Prune(World w)
    {
        Ids.RemoveAll(id => !IsLive(w.Get(id)));
        foreach (var g in _groups) g.RemoveAll(id => !IsLive(w.Get(id)));
    }

    public IEnumerable<Entity> Entities(World w)
    {
        foreach (var id in Ids)
        {
            var e = w.Get(id);
            if (IsLive(e)) yield return e!;
        }
    }

    public List<Entity> Own(World w, int me) => Entities(w).Where(e => e.Owner == me).ToList();

    /// <summary>True if the selection contains anything not owned by me (inspection only).</summary>
    public bool HasForeign(World w, int me) => Entities(w).Any(e => e.Owner != me);

    public void Assign(int g, World w, int me)
    {
        _groups[g].Clear();
        _groups[g].AddRange(Own(w, me).Select(e => e.Id));
    }

    /// <summary>Selects group g. Returns true on a double-tap (caller centers the camera).</summary>
    public bool Recall(int g, float now)
    {
        bool dbl = _lastRecallGroup == g && now - _lastRecallTime < 0.4f;
        _lastRecallGroup = g; _lastRecallTime = now;
        if (_groups[g].Count > 0) Set(_groups[g]);
        return dbl && _groups[g].Count > 0;
    }

    public Vec2? Centroid(World w, IEnumerable<int> ids)
    {
        float x = 0, y = 0; int n = 0;
        foreach (var id in ids)
        {
            var e = w.Get(id);
            if (!IsLive(e)) continue;
            x += e!.Pos.X; y += e.Pos.Y; n++;
        }
        return n == 0 ? null : new Vec2(x / n, y / n);
    }
}
