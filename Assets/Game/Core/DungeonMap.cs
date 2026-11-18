using System;
using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    public readonly struct TilePoint : IEquatable<TilePoint>
    {
        public TilePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Column, increasing to the east.</summary>
        public int X { get; }
        /// <summary>Row, increasing to the south (row 0 is the top line of a text map).</summary>
        public int Y { get; }

        public bool Equals(TilePoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is TilePoint other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X}, {Y})";
        public static bool operator ==(TilePoint a, TilePoint b) => a.Equals(b);
        public static bool operator !=(TilePoint a, TilePoint b) => !a.Equals(b);
    }

    /// <summary>
    /// A fixed dungeon layout. Text maps use '#' for wall, '.' for floor,
    /// 'E' for the single entrance floor tile and 'B' for boss room floor.
    /// </summary>
    public sealed class DungeonMap
    {
        private static readonly int[] StepX = { 0, 1, 0, -1 };
        private static readonly int[] StepY = { -1, 0, 1, 0 };
        private readonly bool[,] floor;
        private readonly bool[,] bossRoom;
        private readonly int[,] distance;

        private DungeonMap(int width, int height)
        {
            Width = width;
            Height = height;
            floor = new bool[width, height];
            bossRoom = new bool[width, height];
            distance = new int[width, height];
        }

        public int Width { get; }
        public int Height { get; }
        public TilePoint Entrance { get; private set; }
        /// <summary>Where the stage boss stands: the boss room tile nearest the room's centre.</summary>
        public TilePoint BossTile { get; private set; }

        public static DungeonMap ForStage(int stage) => Parse(DungeonLayouts.ForStage(stage));

        public static DungeonMap Parse(string layout)
        {
            if (string.IsNullOrWhiteSpace(layout)) throw new FormatException("Map is empty.");
            var rows = new List<string>();
            foreach (var raw in layout.Split('\n'))
            {
                string row = raw.TrimEnd('\r');
                if (row.Length > 0) rows.Add(row);
            }
            int width = rows[0].Length;
            var map = new DungeonMap(width, rows.Count);
            bool hasEntrance = false;
            var bossTiles = new List<TilePoint>();
            for (int y = 0; y < rows.Count; y++)
            {
                if (rows[y].Length != width) throw new FormatException($"Row {y} is {rows[y].Length} wide, expected {width}.");
                for (int x = 0; x < width; x++)
                {
                    switch (rows[y][x])
                    {
                        case '#': break;
                        case '.': map.floor[x, y] = true; break;
                        case 'E':
                            if (hasEntrance) throw new FormatException("Map has more than one entrance.");
                            hasEntrance = true;
                            map.floor[x, y] = true;
                            map.Entrance = new TilePoint(x, y);
                            break;
                        case 'B':
                            map.floor[x, y] = true;
                            map.bossRoom[x, y] = true;
                            bossTiles.Add(new TilePoint(x, y));
                            break;
                        default: throw new FormatException($"Unknown map symbol '{rows[y][x]}' at ({x}, {y}).");
                    }
                }
            }
            if (!hasEntrance) throw new FormatException("Map has no entrance.");
            if (bossTiles.Count == 0) throw new FormatException("Map has no boss room.");

            map.MeasureFromEntrance();
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.floor[x, y] && map.distance[x, y] < 0)
                    throw new FormatException($"Floor at ({x}, {y}) cannot be reached from the entrance.");

            double cx = 0, cy = 0;
            foreach (var tile in bossTiles)
            {
                cx += tile.X;
                cy += tile.Y;
            }
            cx /= bossTiles.Count;
            cy /= bossTiles.Count;
            var best = bossTiles[0];
            foreach (var tile in bossTiles)
            {
                double d = (tile.X - cx) * (tile.X - cx) + (tile.Y - cy) * (tile.Y - cy);
                double b = (best.X - cx) * (best.X - cx) + (best.Y - cy) * (best.Y - cy);
                if (d < b) best = tile;
            }
            map.BossTile = best;
            return map;
        }

        public bool Contains(TilePoint tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < Width && tile.Y < Height;
        public bool IsFloor(TilePoint tile) => Contains(tile) && floor[tile.X, tile.Y];
        public bool IsBossRoom(TilePoint tile) => Contains(tile) && bossRoom[tile.X, tile.Y];

        /// <summary>Steps needed to walk from the entrance, or -1 for walls and tiles outside the map.</summary>
        public int WalkDistance(TilePoint tile) => Contains(tile) ? distance[tile.X, tile.Y] : -1;

        /// <summary>
        /// True when no wall lies strictly between the two tile centres. The end tiles themselves may be walls,
        /// so a viewer sees the faces of the walls around a room.
        /// </summary>
        public bool HasLineOfSight(TilePoint from, TilePoint to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            int samples = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 4;
            for (int i = 1; i < samples; i++)
            {
                double t = (double)i / samples;
                var tile = new TilePoint((int)Math.Floor(from.X + dx * t + .5), (int)Math.Floor(from.Y + dy * t + .5));
                if (tile != from && tile != to && !IsFloor(tile)) return false;
            }
            return true;
        }

        /// <summary>Shortest four-way walk including both ends, or an empty list when either end is not floor.</summary>
        public IReadOnlyList<TilePoint> FindPath(TilePoint from, TilePoint to) => FindPath(from, to, IsFloor);

        /// <summary>Shortest four-way walk over tiles the predicate allows (it should only allow floor).</summary>
        public IReadOnlyList<TilePoint> FindPath(TilePoint from, TilePoint to, Func<TilePoint, bool> passable)
        {
            var path = new List<TilePoint>();
            if (!passable(from) || !passable(to)) return path;
            var previous = new TilePoint?[Width, Height];
            var seen = new bool[Width, Height];
            var queue = new Queue<TilePoint>();
            queue.Enqueue(from);
            seen[from.X, from.Y] = true;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == to) break;
                for (int i = 0; i < 4; i++)
                {
                    var next = new TilePoint(current.X + StepX[i], current.Y + StepY[i]);
                    if (!Contains(next) || seen[next.X, next.Y] || !passable(next)) continue;
                    seen[next.X, next.Y] = true;
                    previous[next.X, next.Y] = current;
                    queue.Enqueue(next);
                }
            }
            if (!seen[to.X, to.Y]) return path;
            for (TilePoint? step = to; step.HasValue; step = previous[step.Value.X, step.Value.Y])
                path.Add(step.Value);
            path.Reverse();
            return path;
        }

        /// <summary>Steps from <paramref name="start"/> to every tile the predicate allows, up to <paramref name="limit"/>.</summary>
        public Dictionary<TilePoint, int> Distances(TilePoint start, Func<TilePoint, bool> passable, int limit = int.MaxValue)
        {
            var result = new Dictionary<TilePoint, int>();
            if (!passable(start)) return result;
            var queue = new Queue<TilePoint>();
            result[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int steps = result[current];
                if (steps >= limit) continue;
                for (int i = 0; i < 4; i++)
                {
                    var next = new TilePoint(current.X + StepX[i], current.Y + StepY[i]);
                    if (!Contains(next) || result.ContainsKey(next) || !passable(next)) continue;
                    result[next] = steps + 1;
                    queue.Enqueue(next);
                }
            }
            return result;
        }

        private void MeasureFromEntrance()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                distance[x, y] = -1;
            var queue = new Queue<TilePoint>();
            distance[Entrance.X, Entrance.Y] = 0;
            queue.Enqueue(Entrance);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    var next = new TilePoint(current.X + StepX[i], current.Y + StepY[i]);
                    if (!IsFloor(next) || distance[next.X, next.Y] >= 0) continue;
                    distance[next.X, next.Y] = distance[current.X, current.Y] + 1;
                    queue.Enqueue(next);
                }
            }
        }
    }
}
