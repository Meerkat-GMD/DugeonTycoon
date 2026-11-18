using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonTycoon.Core
{
    /// <summary>A hero's answer to "do you want this armour?". Also the shape of an AI proposal.</summary>
    public sealed class LootWish
    {
        public LootWish(HeroRole role, int item, bool wants, string line)
        {
            Role = role;
            Item = item;
            Wants = wants;
            Line = line;
        }

        public HeroRole Role { get; }
        /// <summary>Index into <see cref="LootDistribution.Items"/>.</summary>
        public int Item { get; }
        public bool Wants { get; }
        public string Line { get; }
    }

    /// <summary>Armour from one fight, waiting for the living heroes to decide who wears it.</summary>
    public sealed class LootDistribution
    {
        internal LootDistribution(int token, IReadOnlyList<Equipment> items, IReadOnlyList<HeroRole> heroes,
            IReadOnlyList<LootWish> local)
        {
            Token = token;
            Items = items;
            Heroes = heroes;
            LocalWishes = local;
        }

        public int Token { get; }
        /// <summary>The armour, in the order it dropped.</summary>
        public IReadOnlyList<Equipment> Items { get; }
        /// <summary>The heroes who were standing when the fight ended, in march order. Only they take part.</summary>
        public IReadOnlyList<HeroRole> Heroes { get; }
        /// <summary>What each of those heroes says about each item without an AI response.</summary>
        public IReadOnlyList<LootWish> LocalWishes { get; }
    }

    /// <summary>What happened to one dropped item.</summary>
    public sealed class SpoilResult
    {
        internal SpoilResult(Equipment item, HeroRole? wearer, string reason)
        {
            Item = item;
            Wearer = wearer;
            Reason = reason;
        }

        public Equipment Item { get; }
        /// <summary>Who put it on, or null when it was thrown away.</summary>
        public HeroRole? Wearer { get; }
        /// <summary>Why it was thrown away; empty when worn.</summary>
        public string Reason { get; }
    }

    /// <summary>The summary of one fight for the settlement banner, ready once the armour is shared out.</summary>
    public sealed class CombatSpoils
    {
        internal CombatSpoils(int number, int experience, int levelBefore, int levelAfter, IReadOnlyList<SpoilResult> items)
        {
            Number = number;
            Experience = experience;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            Items = items;
        }

        /// <summary>Counts up with every settled fight, so the screen shows each banner once.</summary>
        public int Number { get; }
        public int Experience { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public IReadOnlyList<SpoilResult> Items { get; }
    }

    public sealed partial class DungeonRun
    {
        private readonly List<Equipment> spoils = new List<Equipment>();
        private readonly List<SpoilResult> settled = new List<SpoilResult>();
        private int spoilsExperience;
        private int spoilsLevel;
        private int nextLootToken;
        private int settledFights;

        /// <summary>Armour waiting to be shared out after a fight, or null. The dungeon is frozen meanwhile.</summary>
        public LootDistribution PendingLoot { get; private set; }
        /// <summary>The latest settled fight, or null before the first one.</summary>
        public CombatSpoils LastSpoils { get; private set; }
        /// <summary>Items dropped so far in the fight going on now, in the order they fell.</summary>
        public IReadOnlyList<Equipment> Spoils => spoils;
        /// <summary>Every item dropped this expedition, in the order it fell. Drop events index into it.</summary>
        public IReadOnlyList<Equipment> Drops => drops;
        private readonly List<Equipment> drops = new List<Equipment>();

        /// <summary>Tests pin what a fallen monster drops. Null uses the drop tables.</summary>
        internal Func<MonsterState, Equipment> DropOverride { get; set; }
        /// <summary>Tests pin the faces of the loot d20. Null uses the seeded loot dice.</summary>
        internal Func<int> LootRollOverride { get; set; }

        private void BeginSpoils()
        {
            spoils.Clear();
            spoilsExperience = Experience;
            spoilsLevel = Level;
        }

        private void DropLoot(MonsterState monster)
        {
            var item = DropOverride != null ? DropOverride(monster) : EquipmentCatalog.Roll(monster, lootDice);
            if (item == null) return;
            spoils.Add(item);
            drops.Add(item);
            events.Add(new CombatEvent(CombatEventKind.Drop, false, monster.Id, false, monster.Id, drops.Count - 1));
            log.Add($"전리품 • {HeroTraits.Subject(monster.Name)} {HeroTraits.Particle(item.Name, "을", "를")} 떨어뜨렸다 ({item.GradeName} · {item.Stats})");
        }

        /// <summary>
        /// The fight is over: weapons go straight to their one possible owner, and any armour waits for the
        /// standing heroes to share it out. Fallen heroes get nothing.
        /// </summary>
        private void SettleSpoils()
        {
            settled.Clear();
            var armour = new List<Equipment>();
            foreach (var item in spoils)
            {
                if (item.Slot == EquipmentSlot.Armor)
                {
                    armour.Add(item);
                    continue;
                }
                var hero = party[(int)item.Role.Value];
                if (hero.Hp == 0) Discard(item, $"{HeroTraits.Subject(hero.Name)} 쓰러져 있다");
                else if (item.Power <= Equipment.PowerOf(hero.Weapon)) Discard(item, "지금 무기가 더 낫다");
                else Wear(hero, item);
            }
            spoils.Clear();

            var standing = MarchOrder.Where(role => party[(int)role].Hp > 0).ToList();
            if (armour.Count == 0) FinishSpoils();
            else if (standing.Count == 0)
            {
                foreach (var item in armour) Discard(item, "받을 용사가 없다");
                FinishSpoils();
            }
            else
            {
                var local = new List<LootWish>();
                for (int i = 0; i < armour.Count; i++)
                {
                    int index = i;
                    local.AddRange(TraitWishes(armour[i], i, standing, role => LocalWish(party[(int)role], armour[index], index)));
                }
                PendingLoot = new LootDistribution(++nextLootToken, armour.AsReadOnly(), standing.AsReadOnly(), local.AsReadOnly());
                log.Add("전리품 분배 • 용사들이 " + HeroTraits.Particle(string.Join(", ", armour.Select(item => item.Name)), "을", "를") +
                        " 누가 입을지 의논합니다.");
            }
        }

        /// <summary>A hero wants armour that is better than what they wear. A greedy hero wants it anyway.</summary>
        public LootWish LocalWish(HeroState hero, Equipment item, int index)
        {
            bool better = Gain(hero, item) > 0;
            if (hero.Personality == PersonalityTrait.Greedy)
                return new LootWish(hero.Role, index, true, DialogueLines.GreedyBid(better));
            return new LootWish(hero.Role, index, better, DialogueLines.LootWish(hero.Role, better));
        }

        /// <summary>How much the item's power beats what the hero wears in that slot; negative when it is worse.</summary>
        private static int Gain(HeroState hero, Equipment item) => item.Power - Equipment.PowerOf(hero.WornIn(item.Slot));

        /// <summary>
        /// Takes each hero's first answer (the AI's or the local one) and lets the personality trait overrule it,
        /// so the same situation always ends in the same bids. A greedy hero always bids; worse armour still drops
        /// them from the roll. A generous hero passes when another standing hero bids validly and gains more. Who
        /// bids is judged on the first answers, so two generous heroes never both step back for each other.
        /// </summary>
        private List<LootWish> TraitWishes(Equipment item, int index, IReadOnlyList<HeroRole> heroes, Func<HeroRole, LootWish> first)
        {
            var wishes = new List<LootWish>();
            foreach (var role in heroes)
            {
                var hero = party[(int)role];
                var wish = first(role);
                if (hero.Personality == PersonalityTrait.Greedy && !wish.Wants)
                    wish = new LootWish(role, index, true, DialogueLines.GreedyBid(Gain(hero, item) > 0));
                wishes.Add(wish);
            }
            var bids = wishes.ToArray();
            for (int i = 0; i < bids.Length; i++)
            {
                var hero = party[(int)bids[i].Role];
                if (hero.Personality != PersonalityTrait.Generous || !bids[i].Wants) continue;
                int own = Gain(hero, item);
                HeroState better = null;
                for (int j = 0; j < bids.Length; j++)
                {
                    var ally = party[(int)bids[j].Role];
                    if (j == i || !bids[j].Wants || item.Power < Equipment.PowerOf(ally.WornIn(item.Slot))) continue;
                    if (Gain(ally, item) > own && (better == null || Gain(ally, item) > Gain(better, item))) better = ally;
                }
                if (better != null) wishes[i] = new LootWish(hero.Role, index, false, DialogueLines.GenerousPass(better.Name));
            }
            return wishes;
        }

        /// <summary>
        /// Shares out the pending armour one item at a time. Valid wishes (from the AI) replace a hero's local one;
        /// anything missing or invalid falls back to it. A hero whose armour would get worse is left out, several
        /// candidates roll a plain d20 and tied top faces roll again.
        /// </summary>
        public bool ResolveLoot(int token, IReadOnlyList<LootWish> wishes = null)
        {
            var loot = PendingLoot;
            if (loot == null || token != loot.Token) return false;
            var scene = new List<DialogueBeat>();
            for (int i = 0; i < loot.Items.Count; i++)
            {
                var item = loot.Items[i];
                scene.Add(DialogueBeat.Narration($"{HeroTraits.Particle(item.Name, "을", "를")} 얻었다.", item));
                var wanting = new List<HeroRole>();
                int index = i;
                var decided = TraitWishes(item, i, loot.Heroes, role =>
                {
                    var proposal = wishes?.FirstOrDefault(w => w != null && w.Role == role && w.Item == index);
                    return proposal != null && !string.IsNullOrWhiteSpace(proposal.Line)
                        ? proposal : LocalWish(party[(int)role], item, index);
                });
                foreach (var wish in decided)
                {
                    var role = wish.Role;
                    var hero = party[(int)role];
                    hero.LastLine = wish.Line;
                    string mark = wish.Wants ? DialogueLines.Wants : DialogueLines.Passes;
                    scene.Add(DialogueBeat.Line(role, wish.Line, mark));
                    log.Add($"{hero.Name} {mark} • {item.Name} “{wish.Line}”");
                    if (wish.Wants) wanting.Add(role);
                }

                var candidates = new List<HeroRole>();
                foreach (var role in wanting)
                {
                    var hero = party[(int)role];
                    if (item.Power >= Equipment.PowerOf(hero.BodyArmor))
                    {
                        candidates.Add(role);
                        continue;
                    }
                    string text = $"{hero.Name}에게는 지금 입은 {HeroTraits.Subject(hero.BodyArmor.Name)} 더 낫다.";
                    scene.Add(DialogueBeat.Narration(text, role, DialogueLines.Worse));
                    log.Add(text);
                }

                for (int round = 0; candidates.Count > 1; round++)
                {
                    var rolls = candidates.Select(role => new DiceRoll(role, LootRollOverride?.Invoke() ?? lootDice.Next(1, 21), 0, "")).ToList();
                    int top = rolls.Max(r => r.Face);
                    var best = rolls.Where(r => r.Face == top).Select(r => r.Role).ToList();
                    string title = round == 0 ? DialogueLines.LootRollTitle : DialogueLines.LootRerollTitle;
                    log.Add($"{title} • " + string.Join("  ", rolls.Select(r => $"{party[(int)r.Role].Name} [{r.Face}]")));
                    if (best.Count == 1)
                    {
                        scene.Add(DialogueBeat.Contest(title, rolls, best[0]));
                    }
                    else
                    {
                        scene.Add(DialogueBeat.Tie(title, rolls));
                    }
                    candidates = best;
                }

                if (candidates.Count == 1)
                {
                    var hero = party[(int)candidates[0]];
                    var old = hero.BodyArmor;
                    Wear(hero, item);
                    scene.Add(DialogueBeat.Narration($"{hero.Name} ▸ {HeroTraits.Particle(item.Name, "을", "를")} 입는다." +
                                                     (old != null ? $" {HeroTraits.Particle(old.Name, "은", "는")} 사라진다." : "")));
                    string taken = DialogueLines.LootTaken(hero.Role);
                    hero.LastLine = taken;
                    scene.Add(DialogueBeat.Line(hero.Role, taken));
                }
                else
                {
                    Discard(item, "원하는 용사가 없다");
                    scene.Add(DialogueBeat.Narration($"{item.Name} ▸ 버림"));
                }
            }
            LastScene = scene.AsReadOnly();
            PendingLoot = null;
            FinishSpoils();
            return true;
        }

        private void Wear(HeroState hero, Equipment item)
        {
            hero.Equip(item);
            settled.Add(new SpoilResult(item, hero.Role, ""));
            log.Add($"장착 • {hero.Name} ▸ {item.Name} ({item.Stats})");
        }

        private void Discard(Equipment item, string reason)
        {
            settled.Add(new SpoilResult(item, null, reason));
            log.Add($"버림 • {item.Name} ({reason})");
        }

        private void FinishSpoils()
        {
            LastSpoils = new CombatSpoils(++settledFights, Experience - spoilsExperience, spoilsLevel, Level, settled.ToArray());
            settled.Clear();
        }
    }
}
