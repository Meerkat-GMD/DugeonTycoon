using System.Collections.Generic;

namespace DungeonTycoon.Core
{
    /// <summary>One unexplored way the party can take from a fork, described only with what the party knows.</summary>
    public sealed class ForkOption
    {
        internal ForkOption(int index, string label, IReadOnlyList<TilePoint> path, HashSet<TilePoint> frontier,
            int visibleMonsters, int depth)
        {
            Index = index;
            Label = label;
            Path = path;
            Frontier = frontier;
            VisibleMonsters = visibleMonsters;
            Depth = depth;
        }

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
        internal ForkDiscussion(int token, bool backtracking, IReadOnlyList<ForkOption> options, IReadOnlyList<ForkOpinion> local)
        {
            Token = token;
            Backtracking = backtracking;
            Options = options;
            LocalOpinions = local;
        }

        public int Token { get; }
        /// <summary>True when the party stands at a dead end and chooses which skipped way to return to.</summary>
        public bool Backtracking { get; }
        public IReadOnlyList<ForkOption> Options { get; }
        /// <summary>What each living hero would say without an AI response.</summary>
        public IReadOnlyList<ForkOpinion> LocalOpinions { get; }
    }
}
