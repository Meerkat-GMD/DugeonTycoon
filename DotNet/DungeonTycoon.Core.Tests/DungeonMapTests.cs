using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class DungeonMapTests
    {
        private static IEnumerable<TilePoint> Floors(DungeonMap map)
        {
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var tile = new TilePoint(x, y);
                if (map.IsFloor(tile)) yield return tile;
            }
        }

        private static IEnumerable<TilePoint> Neighbours(DungeonMap map, TilePoint tile)
        {
            var steps = new[] { new TilePoint(0, -1), new TilePoint(1, 0), new TilePoint(0, 1), new TilePoint(-1, 0) };
            foreach (var step in steps)
            {
                var next = new TilePoint(tile.X + step.X, tile.Y + step.Y);
                if (map.IsFloor(next)) yield return next;
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void StageMapsHaveForksDeadEndsAndRoomForMonsters(int stage)
        {
            var map = DungeonMap.ForStage(stage);
            Assert.That(map.IsBossRoom(map.BossTile), Is.True);
            Assert.That(map.IsBossRoom(map.Entrance), Is.False);
            Assert.That(map.WalkDistance(map.Entrance), Is.Zero);
            Assert.That(map.WalkDistance(map.BossTile), Is.GreaterThan(DungeonRun.EntranceClearance));
            Assert.That(Floors(map).All(tile => map.WalkDistance(tile) >= 0), Is.True);
            Assert.That(Floors(map).Count(tile => map.WalkDistance(tile) > DungeonRun.EntranceClearance),
                Is.GreaterThan(50));

            // A dead end: blocking one tile cuts off a pocket of several floor tiles that holds neither
            // the entrance nor the boss room.
            var floors = Floors(map).ToList();
            bool deadEnd = floors.Any(gate =>
            {
                if (gate == map.Entrance || map.IsBossRoom(gate)) return false;
                var reached = Reachable(map, map.Entrance, gate);
                var cutOff = floors.Where(tile => tile != gate && !reached.Contains(tile)).ToList();
                return cutOff.Count >= 6 && !cutOff.Any(map.IsBossRoom);
            });
            Assert.That(deadEnd, Is.True);

            // A fork: the boss stays reachable when a corridor on the shortest route is blocked.
            var corridor = map.FindPath(map.Entrance, map.BossTile)
                .First(tile => !map.IsBossRoom(tile) && Neighbours(map, tile).Count() == 2 &&
                    map.WalkDistance(tile) > DungeonRun.EntranceClearance);
            Assert.That(Reachable(map, map.Entrance, corridor).Contains(map.BossTile), Is.True);
        }

        private static HashSet<TilePoint> Reachable(DungeonMap map, TilePoint start, TilePoint blocked)
        {
            var seen = new HashSet<TilePoint> { start };
            var queue = new Queue<TilePoint>();
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var next in Neighbours(map, queue.Dequeue()))
                    if (next != blocked && seen.Add(next)) queue.Enqueue(next);
            return seen;
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ShortestPathWalksFloorOneStepAtATime(int stage)
        {
            var map = DungeonMap.ForStage(stage);
            var path = map.FindPath(map.Entrance, map.BossTile);
            Assert.That(path.First(), Is.EqualTo(map.Entrance));
            Assert.That(path.Last(), Is.EqualTo(map.BossTile));
            Assert.That(path.Count, Is.EqualTo(map.WalkDistance(map.BossTile) + 1));
            for (int i = 1; i < path.Count; i++)
            {
                Assert.That(map.IsFloor(path[i]), Is.True);
                Assert.That(Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y), Is.EqualTo(1));
            }
            Assert.That(map.FindPath(map.Entrance, new TilePoint(0, 0)), Is.Empty);
            Assert.That(map.FindPath(map.Entrance, map.Entrance), Is.EqualTo(new[] { map.Entrance }));
        }

        [Test]
        public void ParsingRejectsBrokenLayouts()
        {
            Assert.Throws<FormatException>(() => DungeonMap.Parse("#####\n#E.B#\n####"));
            Assert.Throws<FormatException>(() => DungeonMap.Parse("#####\n#E.E#\n#BBB#\n#####"));
            Assert.Throws<FormatException>(() => DungeonMap.Parse("#####\n#E..#\n#####"));
            Assert.Throws<FormatException>(() => DungeonMap.Parse("#####\n#E#B#\n#####"));
            Assert.Throws<FormatException>(() => DungeonMap.Parse("#####\n#E?B#\n#####"));
            var map = DungeonMap.Parse("\r\n#####\r\n#E.B#\r\n#####\r\n");
            Assert.That(map.Width, Is.EqualTo(5));
            Assert.That(map.Height, Is.EqualTo(3));
            Assert.That(map.BossTile, Is.EqualTo(new TilePoint(3, 1)));
        }
    }
}
