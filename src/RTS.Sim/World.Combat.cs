namespace RTS.Sim;

public sealed partial class World
{
    // Pure stat data: no per-kind branching, so counters are retuned in Defs alone.
    private static int HitDamageImpl(Entity attacker, Entity target)
    {
        int dmg = DamageOf(attacker);
        foreach (var (attr, bonus) in attacker.Def.BonusDamage)
            if ((target.Def.Attributes & attr) != 0) dmg += bonus;
        return Math.Max(1, dmg - target.Def.Armor);
    }
}
