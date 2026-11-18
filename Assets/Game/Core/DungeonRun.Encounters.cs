using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    public enum EncounterKind { Merchant, Statue }

    public sealed class MapEncounter
    {
        internal MapEncounter(EncounterKind kind, TilePoint tile)
        {
            Kind = kind;
            Tile = tile;
        }

        public EncounterKind Kind { get; }
        public TilePoint Tile { get; }
        public bool Visited { get; internal set; }
        public bool Accepted { get; internal set; }
    }

    public sealed partial class DungeonRun
    {
        private readonly List<MapEncounter> encounters = new List<MapEncounter>();
        public IReadOnlyList<MapEncounter> Encounters => encounters;

        private void PlaceEncounters()
        {
            encounters.Clear();
            if (sandbox && !sandboxFeatures) return;
            var candidates = Map.FindPath(Map.Entrance, Map.BossTile)
                .Where(t => Map.WalkDistance(t) > EntranceClearance && !Map.IsBossRoom(t)
                    && !Map.Chests.Contains(t) && !Map.Springs.Contains(t)).ToList();
            var random = new Random(unchecked(Seed * 43 + Stage));
            foreach (var kind in new[] { EncounterKind.Merchant, EncounterKind.Statue })
            {
                if (candidates.Count == 0) break;
                var tile = candidates[random.Next(candidates.Count)];
                encounters.Add(new MapEncounter(kind, tile));
                candidates.RemoveAll(t => Math.Abs(t.X - tile.X) + Math.Abs(t.Y - tile.Y) <= 2);
            }
        }
    }
}
