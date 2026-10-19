using RTS.Core;

namespace RTS.Sim;

/// <summary>A unit order waiting in (or at the head of) a unit's shift-queue.
/// Target is the point for Move/AttackMove/Build; TargetId the entity for Attack/Harvest/help-build.
/// Build entries with no TargetId are new placements (already paid) at the top-left tile (BuildTx, BuildTy).</summary>
public readonly record struct QueuedOrder(UnitOrder Order, Vec2? Target = null, int? TargetId = null,
    EntityKind BuildKind = default, int BuildTx = 0, int BuildTy = 0);

public sealed partial class World
{
    /// <summary>Orders queued after a unit's current one, in execution order (read-only; empty for structures).</summary>
    public IReadOnlyList<QueuedOrder> QueueOf(Entity e) => e.X.Queue;

    /// <summary>A unit's current order in the same shape as its queue entries (for listing/drawing).
    /// A build whose site exists carries the site as TargetId; one not yet placed has none.</summary>
    public QueuedOrder CurrentOrderOf(Entity e)
    {
        var x = e.X;
        return e.Order == UnitOrder.Build
            ? new QueuedOrder(UnitOrder.Build, e.OrderTarget, x.BuildSiteId >= 0 ? x.BuildSiteId : null, x.BuildKind, x.BuildTx, x.BuildTy)
            : new QueuedOrder(e.Order, e.OrderTarget, e.TargetId);
    }

    /// <summary>Gives the order now (clearing the queue), or with queue=true appends it if the unit is busy.</summary>
    private void Issue(Entity e, QueuedOrder o, bool queue)
    {
        if (!queue) ClearQueue(e);
        else if (!IsFree(e)) { e.X.Queue.Add(o); return; }
        ResetOrder(e);
        StartOrder(e, o);
    }

    /// <summary>Idle or holding with nothing queued: a queued order starts right away.</summary>
    private static bool IsFree(Entity e) => e.Order is UnitOrder.Idle or UnitOrder.Hold && e.X.Queue.Count == 0;

    /// <summary>A new structure placement (paid when queued), as opposed to helping build an existing site.</summary>
    public static bool IsPlacement(QueuedOrder o) => o.Order == UnitOrder.Build && o.TargetId == null;

    /// <summary>Drops every queued order, refunding builds that never became a construction site.</summary>
    private void ClearQueue(Entity e)
    {
        foreach (var o in e.X.Queue)
            if (IsPlacement(o)) Refund(e.Owner, o.BuildKind);
        e.X.Queue.Clear();
    }

    /// <summary>Refunds the current build if it was paid for but never became a construction site.</summary>
    private void RefundUnplacedBuild(Entity e)
    {
        if (e.Order == UnitOrder.Build && e.X.BuildSiteId < 0 && e.X.BuildPaid) Refund(e.Owner, e.X.BuildKind);
        e.X.BuildPaid = false;
    }

    private void Refund(int owner, EntityKind k)
    {
        if (owner is 0 or 1) Players[owner].Credits += Defs.Get(k).Cost;
    }

    /// <summary>Starts queued orders in turn, skipping any whose target is gone.</summary>
    private void StartNextQueued(Entity e)
    {
        var q = e.X.Queue;
        while (q.Count > 0)
        {
            var o = q[0];
            q.RemoveAt(0);
            if (StartOrder(e, o)) return;
        }
    }

    /// <summary>Applies an order to a unit whose current order was already reset. Returns false if its target is gone.</summary>
    private bool StartOrder(Entity e, QueuedOrder o)
    {
        switch (o.Order)
        {
            case UnitOrder.Move:
            case UnitOrder.AttackMove:
                e.Order = o.Order;
                e.OrderTarget = o.Target;
                SetPath(e, o.Target!.Value);
                return true;
            case UnitOrder.Attack:
                {
                    var t = FindEntity(o.TargetId!.Value);
                    if (t == null || !t.Alive) return false;
                    e.Order = UnitOrder.Attack;
                    e.TargetId = t.Id;
                    return true;
                }
            case UnitOrder.Harvest:
                {
                    var f = FindEntity(o.TargetId!.Value);
                    if (f == null || !f.Alive || f.Resources <= 0) return false;
                    e.Order = UnitOrder.Harvest;
                    e.TargetId = f.Id;
                    e.X.LastField = f.Id;
                    return true;
                }
            case UnitOrder.ReturnCargo:
                if (e.Cargo <= 0) return false;
                e.Order = UnitOrder.ReturnCargo;
                return true;
            case UnitOrder.Build when IsPlacement(o):
                // site validity is re-checked on arrival (UpdateBuild refunds a blocked one)
                e.Order = UnitOrder.Build;
                e.X.BuildKind = o.BuildKind; e.X.BuildTx = o.BuildTx; e.X.BuildTy = o.BuildTy;
                e.X.BuildSiteId = -1; e.X.BuildPaid = true;
                e.OrderTarget = o.Target;
                e.X.RepathTimer = 0;
                return true;
            case UnitOrder.Build: // help build an existing site
                {
                    var site = FindEntity(o.TargetId!.Value);
                    if (site == null || !site.Alive || !site.UnderConstruction) return false;
                    e.Order = UnitOrder.Build;
                    e.X.BuildKind = site.Kind; e.X.BuildTx = site.X.TileX; e.X.BuildTy = site.X.TileY;
                    e.X.BuildSiteId = site.Id;
                    e.OrderTarget = site.Pos;
                    return true;
                }
        }
        return false;
    }
}
