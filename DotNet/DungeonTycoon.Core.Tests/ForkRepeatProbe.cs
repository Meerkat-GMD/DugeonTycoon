using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    [Explicit("diagnostic")]
    public class ForkRepeatProbe
    {
        internal static int ProbeRuns = int.TryParse(Environment.GetEnvironmentVariable("PROBE_RUNS"), out var n) ? n : 300;

        internal sealed class ForkRecord
        {
            public int Steps;
            public TilePoint Leader;
            public bool Backtracking;
            public List<ForkOption> Options;
            public int Chosen = -1;
        }

        internal static List<ForkRecord> Walk(int stage, IReadOnlyList<int> picks, out int nextOptions, DungeonMap map = null)
        {
            var run = DungeonRun.Sandbox(features: true, stage: stage, map: map);
            run.Start();
            var forks = new List<ForkRecord>();
            nextOptions = 0;
            for (int tick = 0; tick < 40000; tick++)
            {
                if (run.FightingBoss || !DungeonRunTests.InDungeon(run)) break;
                if (DungeonRunTests.SettlePrayer(run)) continue;
                if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                var fork = run.PendingFork;
                if (fork != null)
                {
                    var record = new ForkRecord { Steps = run.StepsTaken, Leader = run.LeaderTile, Backtracking = fork.Backtracking, Options = fork.Options.ToList() };
                    forks.Add(record);
                    if (forks.Count > picks.Count)
                    {
                        nextOptions = fork.Options.Count;
                        return forks;
                    }
                    int pick = Math.Min(picks[forks.Count - 1], fork.Options.Count - 1);
                    record.Chosen = pick;
                    run.ResolveFork(fork.Token, run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, pick, ".")).ToList());
                    continue;
                }
                run.Tick();
            }
            return forks;
        }

        internal static int[,] Rooms(DungeonMap map)
        {
            var id = new int[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++) id[x, y] = -1;
            bool Block(int x, int y) => map.IsFloor(new TilePoint(x, y)) && map.IsFloor(new TilePoint(x + 1, y)) &&
                                        map.IsFloor(new TilePoint(x, y + 1)) && map.IsFloor(new TilePoint(x + 1, y + 1));
            var room = new bool[map.Width, map.Height];
            for (int y = 0; y < map.Height - 1; y++)
            for (int x = 0; x < map.Width - 1; x++)
                if (Block(x, y)) room[x, y] = room[x + 1, y] = room[x, y + 1] = room[x + 1, y + 1] = true;
            int next = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (!room[x, y] || id[x, y] >= 0) continue;
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x, y));
                id[x, y] = next;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                        if (nx >= 0 && ny >= 0 && nx < map.Width && ny < map.Height && room[nx, ny] && id[nx, ny] < 0)
                        {
                            id[nx, ny] = next;
                            queue.Enqueue((nx, ny));
                        }
                }
                next++;
            }
            return id;
        }

        internal static int RoomOf(int[,] rooms, TilePoint tile)
        {
            if (rooms[tile.X, tile.Y] >= 0) return rooms[tile.X, tile.Y];
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = tile.X + dx, y = tile.Y + dy;
                if (x >= 0 && y >= 0 && x < rooms.GetLength(0) && y < rooms.GetLength(1) && rooms[x, y] >= 0) return rooms[x, y];
            }
            return -1;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Probe(int stage)
        {
            var map = DungeonMap.ForStage(stage);
            var rooms = Rooms(map);
            var stack = new Stack<List<int>>();
            stack.Push(new List<int>());
            int runs = 0, leaves = 0;
            var flagged = new Dictionary<string, string>();
            while (stack.Count > 0 && runs < ProbeRuns)
            {
                var picks = stack.Pop();
                runs++;
                var forks = Walk(stage, picks, out int options);
                if (options > 0 && picks.Count < 30)
                    for (int k = options - 1; k >= 0; k--) stack.Push(picks.Concat(new[] { k }).ToList());
                else leaves++;
                for (int i = 1; i < forks.Count && i <= picks.Count; i++)
                {
                    var a = forks[i - 1];
                    var b = forks[i];
                    int ra = RoomOf(rooms, a.Leader), rb = RoomOf(rooms, b.Leader);
                    if (ra < 0 || ra != rb) continue;
                    string key = $"room {ra} {a.Leader}->{b.Leader}";
                    if (flagged.ContainsKey(key)) continue;
                    string Show(ForkRecord f) => string.Join(" | ", f.Options.Select(o => $"{o.Label}{o.Target} {o.Feature} n{o.Frontier.Count}"));
                    flagged[key] = $"{key} steps {b.Steps - a.Steps} picks [{string.Join(",", picks.Take(i + 1))}]{(b.Backtracking ? " BACK" : "")}\n" +
                                   $"   A chose {a.Chosen}: {Show(a)}\n   B: {Show(b)}";
                }
            }
            TestContext.Out.WriteLine($"stage {stage}: runs {runs}, leaves {leaves}, flagged {flagged.Count}");
            foreach (var line in flagged.Values) TestContext.Out.WriteLine(line);
        }

        [TestCase(1, "0,0,0,1,0")]
        [TestCase(2, "0,1,1,0,2,1,0")]
        [TestCase(2, "0,0,0,1,0,1,0")]
        [TestCase(3, "0,1,0")]
        [TestCase(1, "1")]
        public void PrintWalk(int stage, string list)
        {
            var picks = list.Split(',').Select(int.Parse).ToList();
            var rooms = Rooms(DungeonMap.ForStage(stage));
            var forks = Walk(stage, picks, out _);
            foreach (var f in forks)
                TestContext.Out.WriteLine($"  room {RoomOf(rooms, f.Leader)} at {f.Leader} step {f.Steps}{(f.Backtracking ? " BACK" : "")} chose {f.Chosen}: " +
                    string.Join(" | ", f.Options.Select(o => $"{o.Label}{o.Target} {o.Feature} n{o.Frontier.Count} [{string.Join(" ", o.Frontier.Take(6))}]")));
        }

        /// <summary>The real first stage with its patrol and local opinions, printed fork by fork.</summary>
        [Test]
        public void RealStageOne()
        {
            var rooms = Rooms(DungeonMap.ForStage(1));
            foreach (int seed in new[] { 20261118, 1, 2, 3 })
            {
                var run = new DungeonRun(seed);
                run.Start();
                TestContext.Out.WriteLine($"seed {seed}");
                TilePoint? last = null;
                for (int tick = 0; tick < 40000 && !run.FightingBoss && DungeonRunTests.InDungeon(run); tick++)
                {
                    if (DungeonRunTests.SettlePrayer(run)) continue;
                    if (run.PendingLoot != null) { run.ResolveLoot(run.PendingLoot.Token); continue; }
                    var fork = run.PendingFork;
                    if (fork != null)
                    {
                        int room = RoomOf(rooms, run.LeaderTile);
                        bool again = last.HasValue && RoomOf(rooms, last.Value) == room && room >= 0;
                        TestContext.Out.WriteLine($"  {(again ? "!!" : "  ")} room {room} at {run.LeaderTile} step {run.StepsTaken} phase {run.Phase}{(fork.Backtracking ? " BACK" : "")}: " +
                            string.Join(" | ", fork.Options.Select(o => $"{o.Label}{o.Target} {o.Feature} n{o.Frontier.Count}")));
                        last = run.LeaderTile;
                        run.ResolveFork(fork.Token);
                        TestContext.Out.WriteLine("       -> " + run.LastFork.Replace('\n', ' '));
                        continue;
                    }
                    var phase = run.Phase;
                    run.Tick();
                    if (phase != run.Phase) TestContext.Out.WriteLine($"       phase {run.Phase} at step {run.StepsTaken} leader {run.LeaderTile}");
                }
            }
        }
    }
}
