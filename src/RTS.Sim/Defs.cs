namespace RTS.Sim;

/// <summary>Static stat tables for every entity kind.</summary>
public static class Defs
{
    public const int MaxPop = 100;
    public const int StartingCredits = 400;
    public const int HarvestPerTrip = 10;
    public const float HarvestTime = 1.5f;
    public const int MaxQueue = 5;

    private static readonly EntityDef[] _defs = Build();

    public static EntityDef Get(EntityKind k) => _defs[(int)k];
    public static IReadOnlyList<EntityDef> All => _defs;

    // Counter triangle (all pure data; HitDamage never branches on kind):
    // Soldier (bonus vs Light) > Ranger (bonus vs Armored) > Tank (high armor) > Soldier.
    private const EntityAttribute Light = EntityAttribute.Light, Armored = EntityAttribute.Armored;
    private const EntityAttribute Building = EntityAttribute.Armored | EntityAttribute.Structure;

    private static Dictionary<EntityAttribute, int> Bonus(params (EntityAttribute Attr, int Amount)[] bonuses)
        => bonuses.ToDictionary(b => b.Attr, b => b.Amount);

    private static EntityDef[] Build()
    {
        var structures = new[] { EntityKind.HQ, EntityKind.Barracks, EntityKind.Factory, EntityKind.SupplyDepot, EntityKind.Turret };
        var list = new List<EntityDef>
        {
            new() { Kind = EntityKind.Worker, Name = "Worker", Cost = 50, PopCost = 1, BuildTime = 10f, MaxHp = 60, Radius = 7f,
                Speed = 70f, Range = 18f, Damage = 4, Cooldown = 1.2f, Sight = 190f, Builds = structures,
                Attributes = Light, Armor = 0 },
            new() { Kind = EntityKind.Soldier, Name = "Soldier", Cost = 75, PopCost = 1, BuildTime = 12f, MaxHp = 120, Radius = 8f,
                Speed = 72f, Range = 40f, Damage = 8, Cooldown = 0.9f, Sight = 220f,
                Attributes = Light, Armor = 0, BonusDamage = Bonus((Light, 4)) },
            new() { Kind = EntityKind.Ranger, Name = "Ranger", Cost = 100, PopCost = 1, BuildTime = 15f, MaxHp = 70, Radius = 7f,
                Speed = 76f, Range = 150f, Damage = 10, Cooldown = 1.3f, Sight = 260f, Requires = EntityKind.Barracks,
                Attributes = Light, Armor = 0, BonusDamage = Bonus((Armored, 20)) },
            new() { Kind = EntityKind.Tank, Name = "Tank", Cost = 250, PopCost = 3, BuildTime = 25f, MaxHp = 420, Radius = 13f,
                Speed = 52f, Range = 130f, Damage = 34, Cooldown = 2.0f, Sight = 250f, Requires = EntityKind.Factory,
                Attributes = Armored, Armor = 5 },

            new() { Kind = EntityKind.HQ, Name = "HQ", IsStructure = true, Cost = 400, PopProvided = 10, BuildTime = 60f, MaxHp = 1500,
                FootprintTiles = 3, Sight = 300f, Trains = new[] { EntityKind.Worker }, Attributes = Building, Armor = 2 },
            new() { Kind = EntityKind.Barracks, Name = "Barracks", IsStructure = true, Cost = 150, BuildTime = 30f, MaxHp = 800,
                FootprintTiles = 3, Sight = 220f, Trains = new[] { EntityKind.Soldier, EntityKind.Ranger }, Attributes = Building, Armor = 2 },
            new() { Kind = EntityKind.Factory, Name = "Factory", IsStructure = true, Cost = 250, BuildTime = 40f, MaxHp = 1000,
                FootprintTiles = 3, Sight = 220f, Trains = new[] { EntityKind.Tank }, Requires = EntityKind.Barracks,
                Attributes = Building, Armor = 2 },
            new() { Kind = EntityKind.SupplyDepot, Name = "Supply Depot", IsStructure = true, Cost = 100, PopProvided = 8, BuildTime = 20f,
                MaxHp = 400, FootprintTiles = 2, Sight = 160f, Attributes = Building, Armor = 2 },
            new() { Kind = EntityKind.Turret, Name = "Turret", IsStructure = true, Cost = 125, BuildTime = 25f, MaxHp = 500,
                FootprintTiles = 2, Range = 170f, Damage = 12, Cooldown = 1.1f, Sight = 240f, Requires = EntityKind.Barracks,
                Attributes = Building, Armor = 2, BonusDamage = Bonus((Light, 10)) },

            new() { Kind = EntityKind.CrystalField, Name = "Crystal Field", IsStructure = true, MaxHp = 1, FootprintTiles = 1, Sight = 0f,
                Attributes = EntityAttribute.None, Armor = 0 },
        };
        var arr = new EntityDef[Enum.GetValues<EntityKind>().Length];
        foreach (var d in list) arr[(int)d.Kind] = d;
        return arr;
    }
}
