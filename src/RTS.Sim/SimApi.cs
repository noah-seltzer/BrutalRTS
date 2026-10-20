// PUBLIC SURFACE of the simulation consumed by RTS.Client.
// The sim agent implements the bodies (and may add members/files freely) but must NOT
// rename/remove/change the signatures declared here.
// World units: 1 tile = World.TileSize world units. Positions are world units (float), +Y down.
using RTS.Core;

namespace RTS.Sim;

public enum EntityKind
{
    // units
    Worker, Soldier, Ranger, Tank,
    // structures
    HQ, Barracks, Factory, SupplyDepot, Turret,
    // neutral
    CrystalField,
}

public enum Terrain : byte { Grass, Water, Rock }

public enum UnitOrder { Idle, Move, AttackMove, Attack, Harvest, ReturnCargo, Build, Hold }

/// <summary>Tags that bonus damage can target. An entity may carry several.</summary>
[Flags]
public enum EntityAttribute { None = 0, Light = 1, Armored = 2, Structure = 4 }

public sealed class EntityDef
{
    public EntityKind Kind;
    public string Name = "";
    public bool IsStructure;
    public int Cost;
    public int PopCost;          // units only
    public int PopProvided;      // structures only
    public float BuildTime;      // seconds
    public int MaxHp;
    public float Radius;         // world units (units); structures use FootprintTiles
    public int FootprintTiles;   // structures: square side length in tiles
    public float Speed;          // world units / second
    public float Range;          // attack range, world units (0 = no attack)
    public int Damage;
    public float Cooldown;       // seconds between attacks
    public float Sight;          // world units
    public EntityKind[] Trains = Array.Empty<EntityKind>(); // structures: units it can produce
    public EntityKind[] Builds = Array.Empty<EntityKind>(); // workers: structures it can build
    public EntityKind? Requires; // tech requirement (structure kind that must exist)
    public EntityAttribute Attributes;
    public int Armor;            // flat reduction applied to every hit taken
    public IReadOnlyDictionary<EntityAttribute, int> BonusDamage = new Dictionary<EntityAttribute, int>(); // per-hit extra vs targets with that attribute
}

public sealed class Entity
{
    public int Id;
    public EntityKind Kind;
    public EntityDef Def = null!;
    public int Owner;            // 0 = player, 1 = enemy AI, -1 = neutral
    public Vec2 Pos;             // world units; structures: center of footprint
    public int Hp;
    public bool Alive => Hp > 0;
    public UnitOrder Order;
    public Vec2? OrderTarget;    // for Move/AttackMove/Build
    public int? TargetId;        // for Attack/Harvest
    public float AttackCooldownLeft;
    public int Cargo;            // workers: crystals carried
    public int Resources;        // CrystalField: remaining
    public float BuildProgress;  // structures: 0..1 (1 = complete)
    public bool UnderConstruction => Def.IsStructure && BuildProgress < 1f;
    public readonly List<EntityKind> TrainQueue = new();
    public float TrainProgress;  // seconds into current TrainQueue[0]
    public Vec2? RallyPoint;
    public int Kills;
    public int Rank;             // veterancy 0..3
    public float LastHitTime;    // world time when last damaged (for flashing)
    public Vec2 Facing = new(1, 0);
    internal readonly EntityExtra X = new(); // sim-internal per-entity state
    public float Heading => MathF.Atan2(Facing.Y, Facing.X);
}

public sealed class PlayerState
{
    public int Index;
    public int Credits;
    public int Pop;
    public int PopCap;
    public bool Defeated;
}

public enum Difficulty { Easy, Normal, Hard }

public enum GameEvent { UnitTrained, StructureComplete, UnderAttack, UnitLost, InsufficientCredits, PopCapped, Victory, Defeat, Shot, Explosion }

public readonly record struct SimEvent(GameEvent Type, int Owner, Vec2 Pos);

public sealed partial class World
{
    public const float TileSize = 32f;
    public int Width { get; }            // tiles
    public int Height { get; }           // tiles
    public float Time { get; private set; }
    public readonly PlayerState[] Players = { new() { Index = 0 }, new() { Index = 1 } };
    public IReadOnlyList<Entity> Entities => _entities;
    public readonly List<SimEvent> Events = new(); // appended during Step; client drains/clears each frame
    public int? Winner { get; private set; }       // 0/1 when game over

    private readonly List<Entity> _entities = new();
    private readonly Terrain[,] _terrain;

    public World(int width, int height, int seed, Difficulty difficulty)
    {
        Width = width; Height = height;
        _terrain = new Terrain[width, height];
        InitWorld(seed, difficulty);
    }

    /// <summary>Standard 1v1 map: player base top-left-ish, AI base bottom-right-ish, crystal fields near each.</summary>
    public static World CreateSkirmish(int seed = 1, Difficulty difficulty = Difficulty.Normal)
        => new(96, 96, seed, difficulty);

    public Terrain TerrainAt(int tx, int ty) => _terrain[tx, ty];
    public static EntityDef DefOf(EntityKind k) => Defs.Get(k);
    public Entity? Get(int id) => FindEntity(id);

    /// <summary>Fixed-step simulation advance. Call with dt = 1/30 (client accumulates).</summary>
    public void Step(float dt) => StepImpl(dt);

    /// <summary>Fog of war for a player: 0 = never seen, 1 = explored (shroud), 2 = currently visible.</summary>
    public byte Visibility(int player, int tx, int ty) => VisibilityImpl(player, tx, ty);

    // ---- Commands (all validate ownership; ignore invalid ones). queue=true appends (shift-queue optional) ----
    public void CmdMove(IEnumerable<int> ids, Vec2 target, bool attackMove = false) => CmdMoveImpl(ids, target, attackMove);
    public void CmdMove(IEnumerable<int> ids, Vec2 target, bool attackMove, bool queue) => CmdMoveImpl(ids, target, attackMove, queue);
    public void CmdAttack(IEnumerable<int> ids, int targetId) => CmdAttackImpl(ids, targetId);
    public void CmdAttack(IEnumerable<int> ids, int targetId, bool queue) => CmdAttackImpl(ids, targetId, queue);
    public void CmdHarvest(IEnumerable<int> ids, int fieldId) => CmdHarvestImpl(ids, fieldId);
    public void CmdHarvest(IEnumerable<int> ids, int fieldId, bool queue) => CmdHarvestImpl(ids, fieldId, queue);
    public void CmdStop(IEnumerable<int> ids) => CmdStopImpl(ids);
    public void CmdHold(IEnumerable<int> ids) => CmdHoldImpl(ids);
    /// <summary>Smart right-click: enemy -> attack, crystal (workers) -> harvest, own unfinished structure (workers) -> help build, else move.</summary>
    public void CmdSmart(IEnumerable<int> ids, Vec2 target, int? targetEntityId) => CmdSmartImpl(ids, target, targetEntityId);
    public void CmdSmart(IEnumerable<int> ids, Vec2 target, int? targetEntityId, bool queue) => CmdSmartImpl(ids, target, targetEntityId, queue);
    /// <summary>Returns false (and emits InsufficientCredits etc.) if it can't be placed / afforded.</summary>
    public bool CmdBuild(int workerId, EntityKind structure, int tileX, int tileY) => CmdBuildImpl(workerId, structure, tileX, tileY);
    /// <summary>queue=true appends the build to the worker's queue; it is still paid (and validated) now.</summary>
    public bool CmdBuild(int workerId, EntityKind structure, int tileX, int tileY, bool queue) => CmdBuildImpl(workerId, structure, tileX, tileY, queue);
    public bool CanPlace(int owner, EntityKind structure, int tileX, int tileY) => CanPlaceImpl(owner, structure, tileX, tileY);
    public bool CmdTrain(int structureId, EntityKind unit) => CmdTrainImpl(structureId, unit);
    public void CmdCancelTrain(int structureId) => CmdCancelTrainImpl(structureId);
    public void CmdRally(int structureId, Vec2 point) => CmdRallyImpl(structureId, point);

    /// <summary>Topmost alive entity whose shape contains the world point (structures by footprint, units by radius).</summary>
    public Entity? PickAt(Vec2 worldPos) => PickAtImpl(worldPos);
}
