using RTS.Core;
using RTS.Sim;
using Xunit.Abstractions;

namespace RTS.Sim.Tests;

public class CombatTests
{
    private readonly ITestOutputHelper _out;
    public CombatTests(ITestOutputHelper output) => _out = output;

    private static Entity With(int damage = 0, int armor = 0, EntityAttribute attrs = EntityAttribute.None,
        Dictionary<EntityAttribute, int>? bonus = null)
        => new() { Def = new EntityDef { Damage = damage, Armor = armor, Attributes = attrs, BonusDamage = bonus ?? new() } };

    [Fact]
    public void HitDamage_SubtractsTargetArmor()
    {
        Assert.Equal(7, World.HitDamage(With(damage: 10), With(armor: 3)));
    }

    [Fact]
    public void HitDamage_AddsBonus_OnlyWhenTargetHasAttribute()
    {
        var attacker = With(damage: 10, bonus: new() { [EntityAttribute.Armored] = 5 });
        Assert.Equal(15, World.HitDamage(attacker, With(attrs: EntityAttribute.Armored | EntityAttribute.Structure)));
        Assert.Equal(10, World.HitDamage(attacker, With(attrs: EntityAttribute.Light)));
        Assert.Equal(10, World.HitDamage(attacker, With()));
    }

    [Fact]
    public void HitDamage_AddsBonusBeforeArmor()
    {
        var attacker = With(damage: 4, bonus: new() { [EntityAttribute.Light] = 6 });
        Assert.Equal(8, World.HitDamage(attacker, With(armor: 2, attrs: EntityAttribute.Light)));
    }

    [Fact]
    public void HitDamage_SumsEveryMatchingBonus()
    {
        var attacker = With(damage: 10, bonus: new() { [EntityAttribute.Armored] = 5, [EntityAttribute.Structure] = 3, [EntityAttribute.Light] = 100 });
        Assert.Equal(18, World.HitDamage(attacker, With(attrs: EntityAttribute.Armored | EntityAttribute.Structure)));
    }

    [Fact]
    public void HitDamage_IsAtLeastOne()
    {
        Assert.Equal(1, World.HitDamage(With(damage: 3), With(armor: 10)));
    }

    [Fact]
    public void StatTable_DeclaresCounterTriangle()
    {
        const EntityAttribute Building = EntityAttribute.Armored | EntityAttribute.Structure;
        EntityDef D(EntityKind k) => World.DefOf(k);
        Assert.Equal(EntityAttribute.Light, D(EntityKind.Worker).Attributes);
        Assert.Equal(0, D(EntityKind.Worker).Armor);
        Assert.Equal(EntityAttribute.Light, D(EntityKind.Soldier).Attributes);
        Assert.True(D(EntityKind.Soldier).BonusDamage[EntityAttribute.Light] > 0);
        Assert.Equal(EntityAttribute.Light, D(EntityKind.Ranger).Attributes);
        Assert.True(D(EntityKind.Ranger).BonusDamage[EntityAttribute.Armored] > 0);
        Assert.Equal(EntityAttribute.Armored, D(EntityKind.Tank).Attributes);
        Assert.True(D(EntityKind.Tank).Armor > D(EntityKind.Soldier).Armor);
        Assert.True(D(EntityKind.Turret).BonusDamage[EntityAttribute.Light] > 0);
        foreach (var k in new[] { EntityKind.HQ, EntityKind.Barracks, EntityKind.Factory, EntityKind.SupplyDepot, EntityKind.Turret })
        {
            Assert.Equal(Building, D(k).Attributes);
            Assert.True(D(k).Armor > 0, $"{k} armor");
        }
        Assert.Equal(EntityAttribute.None, D(EntityKind.CrystalField).Attributes);
        Assert.Equal(0, D(EntityKind.CrystalField).Armor);
    }

    // ---- Equal-cost duels: one per edge of the counter triangle ----

    /// <summary>Two groups attack-move at each other on open ground; returns the survivors of each side.</summary>
    private (int a, int b) Duel(EntityKind kindA, int countA, EntityKind kindB, int countB)
    {
        Assert.Equal(countA * World.DefOf(kindA).Cost, countB * World.DefOf(kindB).Cost);
        var w = World.CreateSkirmish(1);
        w.AiEnabled = false;
        for (int x = 34; x <= 62; x++) for (int y = 38; y <= 58; y++) w.SetTerrain(x, y, Terrain.Grass);
        var a = Enumerable.Range(0, countA).Select(i => w.SpawnUnit(kindA, 0, World.TileCenter(38 + i % 2, 42 + i))).ToList();
        var b = Enumerable.Range(0, countB).Select(i => w.SpawnUnit(kindB, 1, World.TileCenter(58 - i % 2, 42 + i))).ToList();
        w.CmdMove(a.Select(e => e.Id), World.TileCenter(58, 48), attackMove: true);
        w.CmdMove(b.Select(e => e.Id), World.TileCenter(38, 48), attackMove: true);
        for (int i = 0; i < 120 * 30 && a.Any(e => e.Alive) && b.Any(e => e.Alive); i++) w.Step(1f / 30f);
        var result = (a.Count(e => e.Alive), b.Count(e => e.Alive));
        _out.WriteLine($"{countA} {kindA} vs {countB} {kindB}: {result.Item1}/{countA} vs {result.Item2}/{countB} left after {w.Time:F0}s");
        return result;
    }

    [Fact]
    public void Duel_TanksBeatSoldiers()
    {
        var (tanks, soldiers) = Duel(EntityKind.Tank, 3, EntityKind.Soldier, 10);
        Assert.True(tanks > 0 && soldiers == 0, $"tanks={tanks} soldiers={soldiers}");
    }

    [Fact]
    public void Duel_RangersBeatTanks()
    {
        var (rangers, tanks) = Duel(EntityKind.Ranger, 5, EntityKind.Tank, 2);
        Assert.True(rangers > 0 && tanks == 0, $"rangers={rangers} tanks={tanks}");
    }

    [Fact]
    public void Duel_SoldiersBeatRangers()
    {
        var (soldiers, rangers) = Duel(EntityKind.Soldier, 4, EntityKind.Ranger, 3);
        Assert.True(soldiers > 0 && rangers == 0, $"soldiers={soldiers} rangers={rangers}");
    }

    [Fact]
    public void Veterancy_IsCosmetic()
    {
        var w = World.CreateSkirmish(1);
        w.AiEnabled = false;
        for (int x = 40; x <= 56; x++) for (int y = 40; y <= 56; y++) w.SetTerrain(x, y, Terrain.Grass);
        var veteran = w.SpawnUnit(EntityKind.Tank, 0, World.TileCenter(42, 48));
        var rookie = new Entity { Kind = EntityKind.Tank, Def = World.DefOf(EntityKind.Tank) };
        var target = new Entity { Kind = EntityKind.Soldier, Def = World.DefOf(EntityKind.Soldier) };
        for (int i = 0; i < 3; i++)
        {
            if (i == 2) veteran.Hp = 100; // a rank-up heal would push this up
            var victim = w.SpawnUnit(EntityKind.Worker, 1, World.TileCenter(46, 46));
            w.CmdAttack(new[] { veteran.Id }, victim.Id);
            for (int s = 0; s < 10 * 30 && victim.Alive; s++) w.Step(1f / 30f);
            Assert.False(victim.Alive);
        }
        Assert.Equal(1, veteran.Rank);
        Assert.Equal(0, rookie.Rank);
        Assert.Equal(World.MaxHpOf(rookie), World.MaxHpOf(veteran));
        Assert.Equal(veteran.Def.MaxHp, World.MaxHpOf(veteran));
        Assert.Equal(World.HitDamage(rookie, target), World.HitDamage(veteran, target));
        Assert.True(veteran.Hp <= 100, $"rank-up healed to {veteran.Hp}");
    }
}
