using RTS.Sim;

namespace RTS.Client;

/// <summary>Combat stat strings shared by the command-card tooltip and the info panel.</summary>
public static class StatText
{
    static readonly EntityAttribute[] Order = { EntityAttribute.Light, EntityAttribute.Armored, EntityAttribute.Structure };

    /// <summary>e.g. "Armored · Structure · Armor 2".</summary>
    public static string Defense(EntityDef d)
    {
        var parts = Order.Where(a => (d.Attributes & a) != 0).Select(a => a.ToString()).ToList();
        parts.Add($"Armor {d.Armor}");
        return string.Join(" · ", parts);
    }

    /// <summary>e.g. "Dmg 30 (+15 vs Armored)".</summary>
    public static string Attack(EntityDef d)
    {
        var bonuses = Order.Where(a => d.BonusDamage.TryGetValue(a, out int v) && v != 0).Select(a => $"+{d.BonusDamage[a]} vs {a}").ToList();
        return bonuses.Count == 0 ? $"Dmg {d.Damage}" : $"Dmg {d.Damage} ({string.Join(", ", bonuses)})";
    }
}
