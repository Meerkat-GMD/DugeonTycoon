using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>One unexplored way the party can take from a fork, described only with what the party knows.</summary>
    public sealed class ForkOption
    {
        internal ForkOption(int index, string label, IReadOnlyList<TilePoint> path, HashSet<TilePoint> frontier,
            int visibleMonsters, int depth, int markerDistance = -1)
        {
            Index = index;
            Label = label;
            Path = path;
            Frontier = frontier;
            VisibleMonsters = visibleMonsters;
            Depth = depth;
            MarkerDistance = markerDistance;
        }

        /// <summary>
        /// Tiles (Chebyshev) from the edge that way to the boss-room marker the party remembers, or -1 when it has
        /// seen none.
        /// </summary>
        public int MarkerDistance { get; }

        public int Index { get; }
        /// <summary>Direction from the party, e.g. "북쪽 길".</summary>
        public string Label { get; }
        /// <summary>Steps to the edge of what the party has seen that way.</summary>
        public int Distance => Path.Count - 1;
        /// <summary>Living monsters the party can see right now along that way.</summary>
        public int VisibleMonsters { get; }
        /// <summary>Known steps from the entrance to the edge that way; larger means deeper.</summary>
        public int Depth { get; }
        /// <summary>How much unexplored edge lies that way.</summary>
        public int Openness => Frontier.Count;
        public TilePoint Target => Path[Path.Count - 1];
        /// <summary>The tiles from the party to the edge of what it has seen that way, for highlighting.</summary>
        public IReadOnlyList<TilePoint> Route => Path;
        internal IReadOnlyList<TilePoint> Path { get; }
        internal HashSet<TilePoint> Frontier { get; }
    }

    /// <summary>A hero's vote at a fork. Also the shape of an AI proposal.</summary>
    public sealed class ForkOpinion
    {
        public ForkOpinion(HeroRole role, int option, string line)
        {
            Role = role;
            Option = option;
            Line = line;
        }

        public HeroRole Role { get; }
        public int Option { get; }
        public string Line { get; }
    }

    public sealed class ForkDiscussion
    {
        internal ForkDiscussion(int token, bool backtracking, IReadOnlyList<ForkOption> options, IReadOnlyList<ForkOpinion> local,
            IReadOnlyList<HeroRole> grumbling)
        {
            Token = token;
            Backtracking = backtracking;
            Options = options;
            LocalOpinions = local;
            Grumbling = grumbling;
        }

        /// <summary>The heroes too worn out to argue nicely at this fork: they complain and back no oracle against their way.</summary>
        public IReadOnlyList<HeroRole> Grumbling { get; }
        public bool Grumbles(HeroRole role) => Grumbling.Contains(role);

        public int Token { get; }
        /// <summary>True when the party stands at a dead end and chooses which skipped way to return to.</summary>
        public bool Backtracking { get; }
        public IReadOnlyList<ForkOption> Options { get; }
        /// <summary>What each living hero would say without an AI response.</summary>
        public IReadOnlyList<ForkOpinion> LocalOpinions { get; }
        /// <summary>The opinions the party settled on, in march order, once <see cref="DungeonRun.HearFork"/> ran; else null.</summary>
        public IReadOnlyList<ForkOpinion> Heard { get; internal set; }
    }
}
