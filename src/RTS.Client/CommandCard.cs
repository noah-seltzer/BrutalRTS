using RTS.Core;
using RTS.Sim;

namespace RTS.Client;

public sealed class CardButton
{
    public int Slot;
    public string Label = "";
    public string Title = "";      // tooltip heading
    public string Detail = "";     // tooltip body
    public Key Hotkey;
    public string? KeyLabel;       // overrides displayed key
    public int Cost;
    public bool Enabled = true;
    public string? DisabledReason;
    public EntityKind? Icon;
    public Action? OnClick;
    public string ShownKey => KeyLabel ?? Ui.KeyName(Hotkey);
}

public sealed partial class GameSession
{
    public readonly List<CardButton> Card = new();
    public CardButton? HoveredButton;

    public static Key DefaultHotkey(EntityKind k) => k switch
    {
        EntityKind.Worker => Key.W,
        EntityKind.Soldier => Key.E,
        EntityKind.Ranger => Key.R,
        EntityKind.Tank => Key.T,
        EntityKind.HQ => Key.Q,
        EntityKind.Barracks => Key.B,
        EntityKind.Factory => Key.F,
        EntityKind.SupplyDepot => Key.D,
        EntityKind.Turret => Key.T,
        _ => Key.Unknown,
    };

    static readonly Key[] FallbackKeys = { Key.Z, Key.X, Key.C, Key.V, Key.G, Key.R, Key.F, Key.Q, Key.E, Key.W, Key.T, Key.M };

    static Key PickKey(EntityKind k, HashSet<Key> used)
    {
        var key = DefaultHotkey(k);
        if (key != Key.Unknown && !used.Contains(key)) { used.Add(key); return key; }
        foreach (var f in FallbackKeys) if (!used.Contains(f)) { used.Add(f); return f; }
        return Key.Unknown;
    }

    void ClickButton(CardButton b)
    {
        if (!b.Enabled)
        {
            if (b.DisabledReason != null) Messages.Add(b.DisabledReason, Pal.Gold, RealTime, 1f);
            return;
        }
        b.OnClick?.Invoke();
    }

    CardButton? CostButton(int slot, EntityKind k, HashSet<Key> used, string verb)
    {
        var def = World.DefOf(k);
        bool tech = HasTech(def.Requires);
        bool afford = MyPlayer.Credits >= def.Cost;
        var b = new CardButton
        {
            Slot = slot, Label = ShortName(def), Title = $"{verb} {def.Name}", Hotkey = PickKey(k, used),
            Cost = def.Cost, Icon = k, Enabled = tech && afford,
            DisabledReason = !tech ? $"Requires {World.DefOf(def.Requires!.Value).Name}" : !afford ? "Not enough credits" : null,
        };
        var parts = new List<string> { $"Cost: {def.Cost}", $"Time: {def.BuildTime:0}s" };
        if (def.PopCost > 0) parts.Add($"Supply: {def.PopCost}");
        if (def.PopProvided > 0) parts.Add($"+{def.PopProvided} supply");
        b.Detail = string.Join("  ", parts) + $"\nHP {def.MaxHp}  {StatText.AttributesAndArmor(def)}";
        if (def.Damage > 0) b.Detail += $"\n{StatText.Attack(def)}, Rng {def.Range / TS:0.#}";
        return b;
    }

    public static string ShortName(EntityDef d) => d.Kind switch
    {
        EntityKind.SupplyDepot => "Depot",
        EntityKind.Barracks => "Barrcks",
        EntityKind.Factory => "Factory",
        _ => d.Name.Length > 8 ? d.Name[..8] : d.Name,
    };

    void BuildCard()
    {
        Card.Clear();
        var used = new HashSet<Key>();
        if (Placing is EntityKind pk)
        {
            Card.Add(new CardButton
            {
                Slot = 11, Label = "Cancel", Title = $"Cancel placing {World.DefOf(pk).Name}",
                Detail = "Left-click: place (Shift: queue more on the same worker). Right-click/Esc: cancel",
                KeyLabel = "Esc", OnClick = () => Placing = null,
            });
            return;
        }

        var own = Selection.Own(World, Me);
        if (own.Count == 0) return;

        var units = own.Where(e => !e.Def.IsStructure).ToList();
        if (units.Count > 0)
        {
            used.UnionWith(new[] { Key.A, Key.S, Key.H });
            Card.Add(new CardButton
            {
                Slot = 0, Label = "Attack", Title = "Attack-move", Detail = "Then left-click a destination. Units engage enemies on the way.\nShift-click: queue it and stay armed for more.",
                Hotkey = Key.A, OnClick = () => { AttackMovePending = true; Placing = null; },
            });
            Card.Add(new CardButton
            {
                Slot = 1, Label = "Stop", Title = "Stop", Detail = "Cancel current and queued orders.", Hotkey = Key.S,
                OnClick = () => World.CmdStop(OwnUnitIds()),
            });
            Card.Add(new CardButton
            {
                Slot = 2, Label = "Hold", Title = "Hold position", Detail = "Stay put and fire at enemies in range. Clears queued orders.", Hotkey = Key.H,
                OnClick = () => World.CmdHold(OwnUnitIds()),
            });

            var builds = units.Where(u => u.Def.Builds.Length > 0).SelectMany(u => u.Def.Builds).Distinct().Take(8).ToList();
            for (int i = 0; i < builds.Count; i++)
            {
                var k = builds[i];
                var b = CostButton(4 + i, k, used, "Build")!;
                b.Detail += "\nPlace with left-click. Shift: queue several on one worker (paid when queued).";
                b.OnClick = () => { Placing = k; AttackMovePending = false; };
                Card.Add(b);
            }
            return;
        }

        // Structures: act on all selected complete structures of the first selected kind.
        var first = own[0];
        var group = own.Where(e => e.Kind == first.Kind && !e.UnderConstruction).ToList();
        if (group.Count == 0) return;
        var trains = first.Def.Trains;
        for (int i = 0; i < trains.Length && i < 8; i++)
        {
            var k = trains[i];
            var b = CostButton(i, k, used, "Train")!;
            b.OnClick = () =>
            {
                var s = group.Where(x => x.Alive).OrderBy(x => x.TrainQueue.Count).FirstOrDefault();
                if (s != null) World.CmdTrain(s.Id, k);
            };
            Card.Add(b);
        }
        if (group.Any(g => g.TrainQueue.Count > 0))
        {
            used.Add(Key.X);
            Card.Add(new CardButton
            {
                Slot = 11, Label = "Cancel", Title = "Cancel last queued unit", Detail = "Refunds the unit's cost.", Hotkey = Key.X,
                OnClick = () =>
                {
                    var s = group.Where(x => x.Alive).OrderByDescending(x => x.TrainQueue.Count).FirstOrDefault();
                    if (s != null && s.TrainQueue.Count > 0) World.CmdCancelTrain(s.Id);
                },
            });
        }
    }
}
