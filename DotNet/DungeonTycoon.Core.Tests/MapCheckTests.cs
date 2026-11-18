using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>
    /// Checks a layout the way a party walks it. The party ignores the boss room and keeps exploring until it has seen
    /// every other tile, always taking the first (or always the last) way at a fork, so every room gets walked into.
    /// Use <see cref="Check"/> on a draft map before adding it to <see cref="DungeonLayouts"/>.
    /// </summary>
    public class MapCheckTests
    {
        private static IEnumerable<int> Stages => Enumerable.Range(1, DungeonLayouts.StageCount);

        [TestCaseSource(nameof(Stages))]
        public void NoRoomIsArguedOverTwiceInARow(int stage)
        {
            foreach (bool last in new[] { false, true })
                Check(DungeonMap.ForStage(stage), stage, last);
        }

        /// <summary>The room a tile is in, or the room a doorway opens onto; -1 in a corridor.</summary>
        private static int RoomAround(DungeonMap map, TilePoint tile)
        {
            int room = map.RoomOf(tile);
            if (room >= 0) return room;
            foreach (var next in new[] { new TilePoint(tile.X + 1, tile.Y), new TilePoint(tile.X - 1, tile.Y),
                         new TilePoint(tile.X, tile.Y + 1), new TilePoint(tile.X, tile.Y - 1) })
                if (map.RoomOf(next) >= 0) return map.RoomOf(next);
            return -1;
        }

        /// <summary>
        /// Walks the whole map (bar the boss room) with fixed choices and fails if the party opens a second discussion
        /// in a room it has not left since the last one, or if any floor stays unseen.
        /// </summary>
        internal static void Check(DungeonMap map, int stage, bool pickLast)
        {
            var run = DungeonRun.Sandbox(features: true, stage: stage, map: map, exploreAll: true);
            run.Start();
            string policy = pickLast ? "마지막 길" : "첫 길";
            var log = new List<string>();
            int forkRoom = -1;
            bool left = true;
            for (int tick = 0; tick < 60000 && !run.ExploredAll; tick++)
            {
                Assert.That(DungeonRunTests.InDungeon(run), Is.True, $"{stage}스테이지 {policy}: 탐색이 끝나기 전에 원정이 멈췄다");
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Travel), $"{stage}스테이지 {policy}: 빈 던전에서 전투가 났다");
                if (DungeonRunTests.SettlePrayer(run)) continue;
                if (run.PendingLoot != null)
                {
                    run.ResolveLoot(run.PendingLoot.Token);
                    continue;
                }
                var fork = run.PendingFork;
                if (fork != null)
                {
                    int room = RoomAround(run.Map, run.LeaderTile);
                    string labels = string.Join(" / ", fork.Options.Select(o => o.Label));
                    log.Add($"{run.StepsTaken}걸음 {run.LeaderTile} 방 {room}: {labels}");
                    Assert.That(room >= 0 && room == forkRoom && !left, Is.False,
                        $"{stage}스테이지 {policy}: 방 {room}을 떠나지 않고 다시 논의했다\n" + string.Join("\n", log));
                    forkRoom = room;
                    left = false;
                    int pick = pickLast ? fork.Options.Count - 1 : 0;
                    run.ResolveFork(fork.Token, run.Party.Where(h => h.Hp > 0).Select(h => new ForkOpinion(h.Role, pick, ".")).ToList());
                    continue;
                }
                run.Tick();
                if (RoomAround(run.Map, run.LeaderTile) != forkRoom) left = true;
            }
            Assert.That(run.ExploredAll, Is.True, $"{stage}스테이지 {policy}: 탐색이 끝나지 않았다");
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var tile = new TilePoint(x, y);
                if (map.IsFloor(tile) && !map.IsBossRoom(tile))
                    Assert.That(run.IsSeen(tile), Is.True, $"{stage}스테이지 {policy}: {tile}을 보지 못했다");
            }
        }
    }
}
