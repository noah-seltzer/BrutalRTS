using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

/// <summary>Mouse/keyboard -> selection and World commands.</summary>
public sealed partial class GameSession
{
    public EntityKind? Placing;
    public bool AttackMovePending;
    public bool Dragging;
    public Vec2 DragStart;
    bool _miniDragging;
    float _lastClickTime = -10;
    int _lastClickId = -1;

    public const float DragThreshold = 5f;

    void HandleMouse(InputState input)
    {
        var L = Layout;
        var m = input.MousePos;
        bool inPlay = L.PlayArea.Contains(m);
        bool inMini = L.Minimap.Contains(m);

        HoveredButton = null;
        foreach (var b in Card)
            if (L.CardSlot(b.Slot).Contains(m)) HoveredButton = b;

        // ---- ongoing drags
        if (Dragging && (input.MouseReleased[0] || !input.MouseDown[0]))
        {
            Dragging = false;
            FinishDrag(DragStart, m, input);
        }
        if (_miniDragging)
        {
            if (input.MouseDown[0]) Camera.CenterOn(Minimap.ToWorld(L.Minimap, m, World));
            else _miniDragging = false;
        }

        // ---- left press
        if (input.MousePressed[0])
        {
            if (HoveredButton != null) ClickButton(HoveredButton);
            else if (inMini)
            {
                var wp = Minimap.ToWorld(L.Minimap, m, World);
                if (AttackMovePending) { AttackMove(wp); if (!input.Shift) AttackMovePending = false; }
                else if (Placing == null) { Camera.CenterOn(wp); _miniDragging = true; }
            }
            else if (L.InfoArea.Contains(m)) HandleInfoClick(m, input.Shift);
            else if (inPlay)
            {
                if (Placing is EntityKind k) TryPlace(k, input.Shift);
                else if (AttackMovePending) { AttackMove(MouseWorld); if (!input.Shift) AttackMovePending = false; }
                else { Dragging = true; DragStart = m; }
            }
        }

        // ---- right press
        if (input.MousePressed[1])
        {
            if (Placing != null) Placing = null;
            else if (AttackMovePending) AttackMovePending = false;
            else if (inMini) SmartCommand(Minimap.ToWorld(L.Minimap, m, World), pick: false);
            else if (inPlay) SmartCommand(MouseWorld, pick: true);
        }
    }

    void FinishDrag(Vec2 a, Vec2 b, InputState input)
    {
        if (Vec2.Distance(a, b) < DragThreshold) ClickSelect(a, input);
        else BoxSelect(RectF.FromCorners(a, b), input.Shift);
    }

    void ClickSelect(Vec2 screen, InputState input)
    {
        var e = World.PickAt(Camera.ScreenToWorld(screen));
        if (e == null || !IsVisible(e))
        {
            if (!input.Shift) Selection.Clear();
            return;
        }
        if (e.Owner == Me)
        {
            bool dbl = e.Id == _lastClickId && RealTime - _lastClickTime < 0.35f;
            _lastClickTime = RealTime; _lastClickId = e.Id;
            if (Selection.HasForeign(World, Me)) Selection.Clear();
            if (dbl || input.Ctrl)
            {
                var same = OwnOnScreen().Where(x => x.Kind == e.Kind).Select(x => x.Id);
                if (input.Shift) Selection.Add(same); else Selection.Set(same);
            }
            else if (input.Shift) Selection.Toggle(e.Id);
            else Selection.Set(e.Id);
        }
        else
        {
            Selection.Set(e.Id); // inspect only
            _lastClickId = -1;
        }
    }

    IEnumerable<Entity> OwnOnScreen()
    {
        var v = Camera.View;
        foreach (var e in World.Entities)
        {
            if (e.Owner != Me || !e.Alive) continue;
            if (v.Contains(Camera.WorldToScreen(e.Pos))) yield return e;
        }
    }

    void BoxSelect(RectF box, bool shift)
    {
        var inBox = new List<Entity>();
        foreach (var e in World.Entities)
        {
            if (e.Owner != Me || !e.Alive) continue;
            var s = Camera.WorldToScreen(e.Pos);
            float pad = e.Def.IsStructure ? 0 : e.Def.Radius * Camera.Zoom * 0.5f;
            if (s.X + pad >= box.X && s.X - pad <= box.Right && s.Y + pad >= box.Y && s.Y - pad <= box.Bottom) inBox.Add(e);
        }
        var units = inBox.Where(e => !e.Def.IsStructure).ToList();
        var pick = units.Count > 0 ? units : inBox;
        if (Selection.HasForeign(World, Me)) Selection.Clear();
        if (shift) Selection.Add(pick.Select(e => e.Id));
        else if (pick.Count > 0) Selection.Set(pick.Select(e => e.Id));
        else Selection.Clear();
    }

    void HandleInfoClick(Vec2 m, bool shift)
    {
        var ents = Selection.Entities(World).ToList();
        if (ents.Count < 2) return;
        for (int i = 0; i < ents.Count; i++)
        {
            if (!Hud.InfoCell(Layout, i, out var rc)) break;
            if (!rc.Contains(m)) continue;
            if (shift) Selection.Ids.Remove(ents[i].Id); else Selection.Set(ents[i].Id);
            return;
        }
    }

    // ---------------------------------------------------------------- commands

    List<int> OwnUnitIds() => Selection.Own(World, Me).Where(e => !e.Def.IsStructure).Select(e => e.Id).ToList();

    void SmartCommand(Vec2 wp, bool pick)
    {
        var own = Selection.Own(World, Me);
        var units = own.Where(e => !e.Def.IsStructure).Select(e => e.Id).ToList();
        if (units.Count > 0)
        {
            int? tid = null;
            var col = Pal.MoveMarker;
            if (pick)
            {
                var t = World.PickAt(wp);
                if (t != null && IsVisible(t))
                {
                    tid = t.Id;
                    if (t.Owner != Me && t.Owner >= 0) col = Pal.AttackMarker;
                    else if (t.Kind == EntityKind.CrystalField) col = Pal.Neutral;
                }
            }
            World.CmdSmart(units, wp, tid);
            Effects.Marker(wp, col);
            return;
        }
        var producers = own.Where(e => e.Def.IsStructure && e.Def.Trains.Length > 0).ToList();
        if (producers.Count > 0)
        {
            foreach (var s in producers) World.CmdRally(s.Id, wp);
            Effects.Marker(wp, Pal.Gold);
        }
    }

    void AttackMove(Vec2 wp)
    {
        var ids = OwnUnitIds();
        if (ids.Count == 0) return;
        World.CmdMove(ids, wp, attackMove: true);
        Effects.Marker(wp, Pal.AttackMarker);
    }

    /// <summary>Top-left tile of the footprint for a structure ghost under the mouse.</summary>
    public (int tx, int ty) PlacementTile(EntityKind kind)
    {
        int n = Math.Max(1, World.DefOf(kind).FootprintTiles);
        var (tx, ty) = World.FootprintOriginFromCenter(kind, MouseWorld);
        return (Math.Clamp(tx, 0, Math.Max(0, World.Width - n)), Math.Clamp(ty, 0, Math.Max(0, World.Height - n)));
    }

    void TryPlace(EntityKind kind, bool shift)
    {
        var workers = Selection.Own(World, Me).Where(e => e.Def.Builds.Contains(kind)).ToList();
        if (workers.Count == 0) { Placing = null; return; }
        var (tx, ty) = PlacementTile(kind);
        if (!World.CanPlace(Me, kind, tx, ty))
        {
            Messages.Add("Can't build there", Pal.AttackMarker, RealTime, 0.7f);
            return;
        }
        var center = World.FootprintCenter(kind, tx, ty);
        var worker = workers.OrderBy(w => w.Order == UnitOrder.Build ? 1 : 0).ThenBy(w => Vec2.DistanceSq(w.Pos, center)).First();
        if (World.CmdBuild(worker.Id, kind, tx, ty))
        {
            Effects.Marker(center, Pal.MoveMarker);
            if (!shift) Placing = null;
        }
    }

    void HandleHotkeys(InputState input)
    {
        for (int g = 0; g <= 9; g++)
        {
            if (!input.Pressed(Key.Num0 + g)) continue;
            if (input.Ctrl)
            {
                Selection.Assign(g, World, Me);
                Messages.Add($"Control group {g} set", Pal.TextDim, RealTime);
            }
            else if (Selection.Recall(g, RealTime))
            {
                var c = Selection.Centroid(World, Selection.Group(g));
                if (c != null) Camera.CenterOn(c.Value);
            }
        }

        if (input.Pressed(Key.Escape))
        {
            if (Placing != null) Placing = null;
            else if (AttackMovePending) AttackMovePending = false;
            else Selection.Clear();
        }

        if (input.Pressed(Key.Space) && _lastAlertPos is Vec2 alert) Camera.CenterOn(alert);

        if (input.Ctrl) return;
        var handled = new HashSet<Key>();
        foreach (var b in Card.ToList())
        {
            if (b.Hotkey == Key.Unknown || handled.Contains(b.Hotkey) || !input.Pressed(b.Hotkey)) continue;
            handled.Add(b.Hotkey);
            ClickButton(b);
        }
    }
}
