using System.Diagnostics;
using RTS.Core;
using RTS.Sim;
using Xunit.Abstractions;

namespace RTS.Sim.Tests;

public class SimTests
{
    private const float Dt = 1f / 30f;
    private readonly ITestOutputHelper _out;
    public SimTests(ITestOutputHelper output) => _out = output;

    private static World NewWorld(int seed = 1)
    {
        var w = World.CreateSkirmish(seed);
        w.AiEnabled = false;
        return w;
    }

    private static void Run(World w, float seconds)
    {
        int steps = (int)(seconds / Dt);
        for (int i = 0; i < steps; i++) w.Step(Dt);
    }

    private static void ClearRect(World w, int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) w.SetTerrain(x, y, Terrain.Grass);
    }

    private static List<Entity> Units(World w, int owner, EntityKind k) =>
        w.Entities.Where(e => e.Alive && e.Owner == owner && e.Kind == k).ToList();

    [Fact]
    public void Defs_AreComplete()
    {
        foreach (var k in Enum.GetValues<EntityKind>())
        {
            var d = World.DefOf(k);
            Assert.Equal(k, d.Kind);
            Assert.True(d.MaxHp > 0);
        }
        Assert.Contains(EntityKind.Worker, World.DefOf(EntityKind.HQ).Trains);
        Assert.Equal(EntityKind.Factory, World.DefOf(EntityKind.Tank).Requires);
    }

    [Fact]
    public void Map_HasStartingSetup()
    {
        var w = World.CreateSkirmish(7);
        for (int p = 0; p < 2; p++)
        {
            Assert.Single(Units(w, p, EntityKind.HQ));
            Assert.Equal(4, Units(w, p, EntityKind.Worker).Count);
            Assert.Equal(400, w.Players[p].Credits);
            Assert.Equal(10, w.Players[p].PopCap);
            Assert.Equal(4, w.Players[p].Pop);
        }
        Assert.True(w.Entities.Count(e => e.Kind == EntityKind.CrystalField) >= 8);
        // passable route between bases
        var worker = Units(w, 0, EntityKind.Worker)[0];
        var path = w.FindPath(worker.Pos, w.StartLocation(1), worker.Def.Radius, 82, 82, 3);
        Assert.NotEmpty(path);
        Assert.True(Vec2.Distance(path[^1], w.StartLocation(1)) < 3 * World.TileSize);
    }

    [Fact]
    public void Pathfinding_RoutesAroundObstacles()
    {
        var w = NewWorld();
        ClearRect(w, 30, 30, 60, 60);
        for (int y = 0; y <= 55; y++) w.SetTerrain(45, y, Terrain.Rock); // wall with gap at the bottom
        var from = World.TileCenter(35, 40);
        var to = World.TileCenter(55, 40);
        Assert.False(w.LineClear(from, to, 8f));
        var path = w.FindPath(from, to, 8f);
        Assert.NotEmpty(path);
        Assert.True(Vec2.Distance(path[^1], to) < 1f);
        var prev = from;
        foreach (var p in path)
        {
            Assert.True(w.LineClear(prev, p, 8f), $"segment ({prev.X},{prev.Y})->({p.X},{p.Y}) crosses obstacle");
            prev = p;
        }
        Assert.True(path.Any(p => p.Y > 55 * World.TileSize)); // went around through the gap

        var u = w.SpawnUnit(EntityKind.Soldier, 0, from);
        w.CmdMove(new[] { u.Id }, to);
        Run(w, 40);
        Assert.True(Vec2.Distance(u.Pos, to) < 20f, $"unit ended at ({u.Pos.X},{u.Pos.Y})");
        Assert.Equal(UnitOrder.Idle, u.Order);
    }

    [Fact]
    public void GroupMove_SpreadsUnits()
    {
        var w = NewWorld();
        ClearRect(w, 30, 30, 50, 50);
        var ids = new List<int>();
        for (int i = 0; i < 9; i++) ids.Add(w.SpawnUnit(EntityKind.Soldier, 0, World.TileCenter(32 + i % 3, 32 + i / 3)).Id);
        var target = World.TileCenter(44, 44);
        w.CmdMove(ids, target);
        Run(w, 20);
        var units = ids.Select(id => w.Get(id)!).ToList();
        foreach (var a in units)
        {
            Assert.True(Vec2.Distance(a.Pos, target) < 90f);
            foreach (var b in units) if (a != b) Assert.True(Vec2.Distance(a.Pos, b.Pos) > 8f, "units stacked");
        }
    }

    [Fact]
    public void Harvesting_IncreasesCredits()
    {
        var w = NewWorld();
        var workers = Units(w, 0, EntityKind.Worker);
        var hq = Units(w, 0, EntityKind.HQ)[0];
        var field = w.Entities.Where(e => e.Kind == EntityKind.CrystalField).OrderBy(e => Vec2.Distance(e.Pos, hq.Pos)).First();
        int before = field.Resources;
        w.CmdHarvest(workers.Select(x => x.Id), field.Id);
        Run(w, 60);
        Assert.True(w.Players[0].Credits > 400 + 100, $"credits {w.Players[0].Credits}");
        Assert.True(field.Resources < before);
        _out.WriteLine($"Credits after 60s with 4 workers: {w.Players[0].Credits}");
    }

    [Fact]
    public void Training_SpendsCreditsAndSpawnsUnit()
    {
        var w = NewWorld();
        var hq = Units(w, 0, EntityKind.HQ)[0];
        var rally = hq.Pos + new Vec2(150, 0);
        w.CmdRally(hq.Id, rally);
        Assert.True(w.CmdTrain(hq.Id, EntityKind.Worker));
        Assert.Equal(350, w.Players[0].Credits);
        Assert.False(w.CmdTrain(hq.Id, EntityKind.Soldier)); // HQ can't train soldiers
        Run(w, World.DefOf(EntityKind.Worker).BuildTime + 0.5f);
        Assert.Equal(5, Units(w, 0, EntityKind.Worker).Count);
        Assert.Equal(5, w.Players[0].Pop);
        Assert.True(w.Events.Any(e => e.Type == GameEvent.UnitTrained && e.Owner == 0));
        // cancel refunds
        Assert.True(w.CmdTrain(hq.Id, EntityKind.Worker));
        Assert.True(w.CmdTrain(hq.Id, EntityKind.Worker));
        int c = w.Players[0].Credits;
        w.CmdCancelTrain(hq.Id);
        Assert.Equal(c + 50, w.Players[0].Credits);
        Assert.Single(hq.TrainQueue);
    }

    [Fact]
    public void Training_BlockedByPopCap()
    {
        var w = NewWorld();
        var hq = Units(w, 0, EntityKind.HQ)[0];
        for (int i = 0; i < 6; i++) w.SpawnUnit(EntityKind.Worker, 0, hq.Pos + new Vec2(90, 60 + i * 2));
        Run(w, 0.1f);
        Assert.Equal(10, w.Players[0].Pop);
        Assert.True(w.CmdTrain(hq.Id, EntityKind.Worker));
        Run(w, 15);
        Assert.Equal(10, Units(w, 0, EntityKind.Worker).Count);
        Assert.True(w.Events.Any(e => e.Type == GameEvent.PopCapped));
    }

    [Fact]
    public void Building_PlacementValidation()
    {
        var w = NewWorld();
        var hq = Units(w, 0, EntityKind.HQ)[0];
        var worker = Units(w, 0, EntityKind.Worker)[0];
        var (hx, hy) = World.StructureOrigin(hq);
        Assert.False(w.CanPlace(0, EntityKind.Barracks, hx, hy));          // overlaps HQ
        Assert.False(w.CanPlace(0, EntityKind.Barracks, -1, 5));           // out of bounds
        Assert.False(w.CanPlace(0, EntityKind.Barracks, 80, 80));          // unexplored / enemy area
        w.SetTerrain(18, 18, Terrain.Water);
        Assert.False(w.CanPlace(0, EntityKind.Barracks, 17, 17));          // water
        Assert.False(w.CanPlace(0, EntityKind.Factory, 16, 8));            // needs barracks
        Assert.False(w.CanPlace(0, EntityKind.CrystalField, 16, 8));
        Assert.True(w.CanPlace(0, EntityKind.Barracks, 16, 8));

        Assert.True(w.CmdBuild(worker.Id, EntityKind.Barracks, 16, 8));
        Assert.Equal(250, w.Players[0].Credits);
        Assert.False(w.CanPlace(0, EntityKind.SupplyDepot, 17, 9));        // overlaps pending site
        Assert.False(w.CmdBuild(Units(w, 0, EntityKind.Worker)[1].Id, EntityKind.Factory, 20, 14));
        Run(w, 5);
        var site = w.Entities.SingleOrDefault(e => e.Kind == EntityKind.Barracks && e.Owner == 0);
        Assert.NotNull(site);
        Assert.True(site!.UnderConstruction);
        Assert.False(w.IsPassable(17, 9));
        // a helper speeds it up
        w.CmdSmart(new[] { Units(w, 0, EntityKind.Worker)[2].Id }, site.Pos, site.Id);
        Run(w, World.DefOf(EntityKind.Barracks).BuildTime);
        Assert.False(site.UnderConstruction);
        Assert.InRange(site.Hp, site.Def.MaxHp - 30, site.Def.MaxHp);
        Assert.True(w.Events.Any(e => e.Type == GameEvent.StructureComplete));
        Assert.True(w.CanPlace(0, EntityKind.Factory, 16, 13) || w.CanPlace(0, EntityKind.Factory, 20, 6));

        // insufficient credits
        w.Players[0].Credits = 10;
        var spot = Enumerable.Range(5, 25).SelectMany(x => Enumerable.Range(5, 25).Select(y => (x, y))).First(t => w.CanPlace(0, EntityKind.SupplyDepot, t.x, t.y));
        Assert.False(w.CmdBuild(worker.Id, EntityKind.SupplyDepot, spot.x, spot.y));
        Assert.True(w.Events.Any(e => e.Type == GameEvent.InsufficientCredits));
    }

    [Fact]
    public void Combat_KillsUnit_AndGrantsVeterancy()
    {
        var w = NewWorld();
        ClearRect(w, 40, 40, 56, 56);
        var tank = w.SpawnUnit(EntityKind.Tank, 0, World.TileCenter(42, 48));
        var victims = new List<Entity>();
        for (int i = 0; i < 3; i++) victims.Add(w.SpawnUnit(EntityKind.Worker, 1, World.TileCenter(47, 46 + i * 2)));
        w.CmdAttack(new[] { tank.Id }, victims[0].Id);
        Run(w, 8);
        Assert.False(victims[0].Alive);
        Assert.Null(w.Get(victims[0].Id));
        Assert.True(w.Events.Any(e => e.Type == GameEvent.Explosion));
        Assert.True(w.Events.Any(e => e.Type == GameEvent.Shot && e.Owner == 0));
        Assert.True(w.Events.Any(e => e.Type == GameEvent.UnitLost && e.Owner == 1));
        // idle tank auto-acquires the rest
        Run(w, 20);
        Assert.All(victims, v => Assert.False(v.Alive));
        Assert.Equal(3, tank.Kills);
        Assert.Equal(1, tank.Rank);
        Assert.True(World.MaxHpOf(tank) > tank.Def.MaxHp);
        Assert.True(World.DamageOf(tank) > tank.Def.Damage);
    }

    [Fact]
    public void Combat_TurretDefends_AndUnderAttackEmitted()
    {
        var w = NewWorld();
        ClearRect(w, 40, 40, 56, 56);
        var turret = w.SpawnStructure(EntityKind.Turret, 0, 44, 44, complete: true);
        var s = w.SpawnUnit(EntityKind.Soldier, 1, World.TileCenter(52, 45));
        w.CmdAttack(new[] { s.Id }, turret.Id);
        Run(w, 25);
        Assert.False(s.Alive);
        Assert.True(turret.Alive);
        Assert.True(w.Events.Any(e => e.Type == GameEvent.UnderAttack && e.Owner == 0));
    }

    [Fact]
    public void Fog_TracksVisibility()
    {
        var w = NewWorld();
        var hq = Units(w, 0, EntityKind.HQ)[0];
        var (hx, hy) = World.WorldToTile(hq.Pos);
        Assert.Equal(2, w.Visibility(0, hx, hy));
        Assert.Equal(0, w.Visibility(0, 84, 84));
        Assert.Equal(2, w.Visibility(1, 84, 84));
        Assert.Equal(0, w.Visibility(0, 200, 5)); // out of bounds is safe
        ClearRect(w, 12, 12, 60, 40);
        var scout = Units(w, 0, EntityKind.Worker)[0];
        w.CmdMove(new[] { scout.Id }, World.TileCenter(40, 30));
        Run(w, 25);
        Assert.Equal(2, w.Visibility(0, 40, 30));
        w.CmdMove(new[] { scout.Id }, hq.Pos + new Vec2(100, 0));
        Run(w, 25);
        Assert.Equal(1, w.Visibility(0, 40, 30)); // explored but not visible
    }

    [Fact]
    public void Victory_WhenAllStructuresDestroyed()
    {
        var w = NewWorld();
        foreach (var e in w.Entities.Where(e => e.Owner == 1 && e.Def.IsStructure)) e.Hp = 0;
        Run(w, 1);
        Assert.Equal(0, w.Winner);
        Assert.True(w.Players[1].Defeated);
        Assert.True(w.Events.Any(e => e.Type == GameEvent.Victory && e.Owner == 0));
        Assert.True(w.Events.Any(e => e.Type == GameEvent.Defeat && e.Owner == 1));
    }

    [Fact]
    public void Simulation_IsDeterministic()
    {
        string Snap(World w) => string.Join(";", w.Entities.Select(e => $"{e.Id}:{e.Pos.X:F2},{e.Pos.Y:F2}:{e.Hp}")) + $"|{w.Players[1].Credits}";
        var a = World.CreateSkirmish(3, Difficulty.Normal);
        var b = World.CreateSkirmish(3, Difficulty.Normal);
        Run(a, 120); Run(b, 120);
        Assert.Equal(Snap(a), Snap(b));
    }

    [Fact]
    public void AiVsAi_FifteenMinutes_IsFast()
    {
        var w = World.CreateSkirmish(5, Difficulty.Normal);
        w.AddAi(0, Difficulty.Normal);
        var sw = Stopwatch.StartNew();
        int steps = (int)(15 * 60 / Dt);
        for (int i = 0; i < steps && w.Winner == null; i++)
        {
            w.Step(Dt);
            if (i % 300 == 0) { w.Events.Clear(); w.Shots.Clear(); }
        }
        sw.Stop();
        int units = w.Entities.Count(e => !e.Def.IsStructure);
        string msg = $"AI vs AI: t={w.Time / 60:F1}m winner={w.Winner?.ToString() ?? "none"} units={units} pop0={w.Players[0].Pop} pop1={w.Players[1].Pop} wall={sw.ElapsedMilliseconds}ms";
        _out.WriteLine(msg);
        Console.WriteLine(msg);
        Assert.True(sw.ElapsedMilliseconds < 20000, msg);
    }

    [Theory]
    [InlineData(Difficulty.Easy, 25)]
    [InlineData(Difficulty.Normal, 20)]
    [InlineData(Difficulty.Hard, 16)]
    public void Ai_DefeatsPassivePlayer(Difficulty diff, int maxMinutes)
    {
        var w = World.CreateSkirmish(1, diff);
        var sw = Stopwatch.StartNew();
        int maxSteps = (int)(maxMinutes * 60 / Dt);
        int step = 0;
        for (; step < maxSteps && w.Winner == null; step++)
        {
            w.Step(Dt);
            if (step % 300 == 0) { w.Events.Clear(); w.Shots.Clear(); }
            if (step % (int)(60 / Dt) == 0 && step > 0)
            {
                var p = w.Players[1];
                int army = w.Entities.Count(e => e.Owner == 1 && e.Alive && !e.Def.IsStructure && e.Kind != EntityKind.Worker);
                int workers = w.Entities.Count(e => e.Owner == 1 && e.Alive && e.Kind == EntityKind.Worker);
                var structs = string.Join(",", w.Entities.Where(e => e.Owner == 1 && e.Def.IsStructure).GroupBy(e => e.Kind).Select(g => $"{g.Key}x{g.Count()}"));
                _out.WriteLine($"[{diff}] t={w.Time / 60:F0}m credits={p.Credits} pop={p.Pop}/{p.PopCap} workers={workers} army={army} structs={structs}");
            }
        }
        sw.Stop();
        string msg = $"[{diff}] AI {(w.Winner == 1 ? "won" : "did NOT win")} at {w.Time / 60f:F2} sim-min ({w.Time:F0}s), wall {sw.ElapsedMilliseconds} ms";
        _out.WriteLine(msg);
        Console.WriteLine(msg);
        Assert.Equal(1, w.Winner);
    }
}
