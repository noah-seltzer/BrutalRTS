using RTS.Core;

namespace RTS.Sim;

public sealed partial class World
{
    /// <summary>Test/editor hook: overwrite a terrain tile.</summary>
    internal void SetTerrain(int tx, int ty, Terrain t) => _terrain[tx, ty] = t;
    internal void RefreshVisibility() { RebuildLists(); RecomputeVisibility(); }
}
