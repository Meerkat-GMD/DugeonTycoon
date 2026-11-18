using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class HeroTraitTests
    {
        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];

        // ── Stats ────────────────────────────────────────────────────────

        [TestCase(HeroRole.Tank, 160, 6, 11, 8550)]
        [TestCase(HeroRole.Healer, 74, 3, 6, 10800)]
        [TestCase(HeroRole.MeleeDamage, 64, 27, 1, 11000)]
        [TestCase(HeroRole.RangedDamage, 50, 16, 0, 13965)]
        public void TheDefaultFormationGivesTheStatsInTheDesignTable(HeroRole role, int maxHp, int attack, int armor, int speed)
        {
            var stats = HeroStats.Starting(role, HeroBuild.Default(role));
            Assert.That(new[] { stats.MaxHp, stats.Attack, stats.Armor, stats.SpeedHundredths },
                Is.EqualTo(new[] { maxHp, attack, armor, speed }));
            Assert.That(HeroBuild.Default(role).Left, Is.Zero, "the default build spends every point");

            var hero = Hero(new DungeonRun(), role);
            Assert.That(hero.Build, Is.EqualTo(HeroBuild.Default(role)));
            Assert.That(new[] { hero.MaxHp, hero.Hp, hero.Attack, hero.Armor, hero.SpeedHundredths },
                Is.EqualTo(new[] { maxHp, maxHp, attack, armor, speed }));
        }

        [Test]
        public void SpeedPercentagesAddUpAndMultiplyTheRoleBaseSpeed()
        {
            // The design doc's example: a 90% tank with 3 speed points and a -5% trait moves at 96.3%.
            var build = new HeroBuild(CombatTrait.Stoic, PersonalityTrait.Greedy, speed: 3)
                .With(CombatTrait.Protector);
            Assert.That(HeroStats.Starting(HeroRole.Tank, build).SpeedHundredths, Is.EqualTo(9630));
            Assert.That(HeroStats.Starting(HeroRole.Tank, build).SpeedPercent, Is.EqualTo(96.3).Within(1e-9));
        }

        [Test]
        public void EachPointAddsItsGainAndTraitsAddTheirBonus()
        {
            var plain = new HeroBuild(CombatTrait.Lookout, PersonalityTrait.Peacemaker);
            var trained = plain.WithPoints(BuildStat.MaxHp, 4).WithPoints(BuildStat.Attack, 3).WithPoints(BuildStat.Armor, 2);
            var a = HeroStats.Starting(HeroRole.Healer, plain);
            var b = HeroStats.Starting(HeroRole.Healer, trained);
            Assert.That(b.MaxHp - a.MaxHp, Is.EqualTo(4 * 6));
            Assert.That(b.Attack - a.Attack, Is.EqualTo(3));
            Assert.That(b.Armor - a.Armor, Is.EqualTo(2));
            // 망보기 -6 health, 중재자 +8 health and -1 attack on the healer's 44/4/0.
            Assert.That(new[] { a.MaxHp, a.Attack, a.Armor }, Is.EqualTo(new[] { 44 - 6 + 8, 4 - 1, 0 }));
        }

        [Test]
        public void StatsNeverDropBelowTheirFloors()
        {
            // 궁지의 전사 -3 armour and 약자 챙김 -2 armour on a healer with no armour.
            var stats = HeroStats.Starting(HeroRole.Healer, new HeroBuild(CombatTrait.Cornered, PersonalityTrait.Underdog));
            Assert.That(stats.Armor, Is.Zero);
            Assert.That(stats.MaxHp, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void ABuildSpendsAtMostTenWholePointsAndNeverANegativeOne()
        {
            var build = new HeroBuild(CombatTrait.Avenger, PersonalityTrait.Navigator, maxHp: 4, attack: 6);
            Assert.That(build.Spent, Is.EqualTo(10));
            Assert.That(build.Left, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => build.WithPoints(BuildStat.Speed, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => build.WithPoints(BuildStat.Armor, -1));
            Assert.That(build.WithPoints(BuildStat.Attack, 0).Left, Is.EqualTo(6), "points may be left unspent");
        }

        [Test]
        public void EveryTraitHasANameATriggerAndALine()
        {
            var all = TraitCatalog.CombatTraits.Select(TraitCatalog.Of).Concat(TraitCatalog.PersonalityTraits.Select(TraitCatalog.Of));
            foreach (var info in all)
            {
                Assert.That(new[] { info.Name, info.Trigger, info.Line, info.Summary }, Has.None.Empty, info.Name);
                Assert.That(info.Summary.Length, Is.LessThanOrEqualTo(22), $"{info.Name}: the summary fits on one card line");
            }
            Assert.That(TraitCatalog.CombatTraits.Count() + TraitCatalog.PersonalityTraits.Count(), Is.EqualTo(30));
            Assert.That(TraitCatalog.Of(CombatTrait.Triage).OnlyFor, Is.EqualTo(HeroRole.Healer));
            Assert.That(all.Where(info => !info.Works).Select(info => info.Name), Is.Empty, "every trait acts in play now");
        }

        // ── Formation ────────────────────────────────────────────────────

        [Test]
        public void TheFormationIsSetBeforeTheFirstDepartureAndLockedAfter()
        {
            var run = new DungeonRun();
            var build = new HeroBuild(CombatTrait.Stoic, PersonalityTrait.Greedy, armor: 10);
            Assert.That(run.CanEditFormation, Is.True);
            Assert.That(run.SetBuild(HeroRole.Tank, build), Is.True);
            Assert.That(Hero(run, HeroRole.Tank).Armor, Is.EqualTo(4 + 4 + 10));
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.EqualTo(Hero(run, HeroRole.Tank).MaxHp));

            run.Start();
            Assert.That(run.CanEditFormation, Is.False);
            Assert.That(run.SetBuild(HeroRole.Tank, HeroBuild.Default(HeroRole.Tank)), Is.False);
            Assert.That(run.BuildOf(HeroRole.Tank), Is.EqualTo(build));
        }

        [Test]
        public void ARematchKeepsTheFormationAndPlanAndTheSameSeedReplaysTheSameRun()
        {
            var first = new DungeonRun(4242);
            var tiles = DungeonRunTests.PlaceableTiles(first).Take(3).ToList();
            foreach (var tile in tiles) Assert.That(first.Place(tile, MonsterKind.Skeleton), Is.True);
            first.SetBuild(HeroRole.MeleeDamage, new HeroBuild(CombatTrait.Avenger, PersonalityTrait.Generous, attack: 10));
            first.Start();
            for (int i = 0; i < 400; i++) Advance(first);

            var again = first.Rematch(first.Seed);
            Assert.That(again.Phase, Is.EqualTo(RunPhase.Planning));
            Assert.That(again.Stage, Is.EqualTo(1));
            Assert.That(again.Seed, Is.EqualTo(4242));
            Assert.That(again.Placements.Select(p => p.Tile), Is.EqualTo(tiles));
            Assert.That(again.BuildOf(HeroRole.MeleeDamage), Is.EqualTo(first.BuildOf(HeroRole.MeleeDamage)));
            Assert.That(again.CanEditFormation, Is.True, "the copy can still be changed before it sets off");
            Assert.That(again.Party.All(h => h.Hp == h.MaxHp && h.Weapon == null && h.BodyArmor == null), Is.True);

            var control = new DungeonRun(4242);
            foreach (var tile in tiles) control.Place(tile, MonsterKind.Skeleton);
            control.SetBuild(HeroRole.MeleeDamage, first.BuildOf(HeroRole.MeleeDamage));
            again.Start();
            control.Start();
            for (int i = 0; i < 400; i++)
            {
                Advance(again);
                Advance(control);
            }
            Assert.That(again.Log, Is.EqualTo(control.Log), "same seed, plan and formation give the same run");
        }

        [Test]
        public void ARematchAfterTheFirstStageLaysOutTheSecondStagePlanWhenItOpens()
        {
            var first = new DungeonRun(7);
            first.Start();
            DungeonRunTests.Drive(first, () => first.Stage == 2 || !DungeonRunTests.InDungeon(first), 60000);
            Assume.That(first.Stage, Is.EqualTo(2));
            var tile = DungeonRunTests.PlaceableTiles(first).First();
            Assert.That(first.Place(tile, MonsterKind.Zombie), Is.True);

            var next = first.Rematch(99);
            Assert.That(next.Placements, Is.Empty, "the first stage was left empty");
            next.Start();
            DungeonRunTests.Drive(next, () => next.Stage == 2 || !DungeonRunTests.InDungeon(next), 60000);
            Assume.That(next.Stage, Is.EqualTo(2));
            Assert.That(next.Placements.Select(p => (p.Tile, p.Kind)), Is.EqualTo(new[] { (tile, MonsterKind.Zombie) }));
        }

        private static void Advance(DungeonRun run)
        {
            if (run.PendingFork != null) run.ResolveFork(run.PendingFork.Token);
            else if (run.PendingLoot != null) run.ResolveLoot(run.PendingLoot.Token);
            else if (run.PendingPrayer != null) run.HearPrayer(run.PendingPrayer.Token);
            else run.Tick();
        }

        // ── Combat speed ─────────────────────────────────────────────────

        [Test]
        public void FasterHeroesStepSoonerInAFightAndTheRemainderCarries()
        {
            var run = new DungeonRun();
            var tank = Hero(run, HeroRole.Tank);          // 85.5%
            var ranged = Hero(run, HeroRole.RangedDamage); // 139.65%
            int[] Steps(HeroState hero) => Enumerable.Range(0, 20).Select(_ => CombatMove(hero)).ToArray();
            var slow = Steps(tank);
            var fast = Steps(ranged);
            Assert.That(slow.Sum(), Is.EqualTo((int)Math.Floor(20 * 3 * 10000.0 / tank.SpeedHundredths)).Within(1));
            Assert.That(fast.Sum(), Is.EqualTo((int)Math.Floor(20 * 3 * 10000.0 / ranged.SpeedHundredths)).Within(1));
            Assert.That(fast.Sum(), Is.LessThan(slow.Sum()));
            Assert.That(slow.Distinct(), Is.EquivalentTo(new[] { 3, 4 }));
        }

        /// <summary>Calls the private combat step delay through reflection; the rule is too small to stage a fight for.</summary>
        private static int CombatMove(HeroState hero)
        {
            var method = typeof(DungeonRun).GetMethod("CombatMoveTicks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (int)method.Invoke(null, new object[] { hero });
        }

        // ── Personality: voices and prayer ───────────────────────────────

        [Test]
        public void TheOldTagsCarryOverAsThreeVoicesAndTheRestSpeakPlainly()
        {
            Assert.That(HeroTraits.VoiceOf(PersonalityTrait.Challenger), Is.EqualTo(Voice.Bold));
            Assert.That(HeroTraits.VoiceOf(PersonalityTrait.CautiousGuide), Is.EqualTo(Voice.Careful));
            Assert.That(HeroTraits.VoiceOf(PersonalityTrait.Curious), Is.EqualTo(Voice.Curious));
            Assert.That(TraitCatalog.PersonalityTraits.Count(t => HeroTraits.VoiceOf(t) == Voice.Neutral), Is.EqualTo(11));
        }

        // ── Personality: armour bids ─────────────────────────────────────

        private static Equipment Item(string name) => EquipmentCatalog.Find(name) ?? throw new ArgumentException(name);

        /// <summary>One skeleton that drops <paramref name="armour"/>; the fight runs to the loot debate.</summary>
        private static DungeonRun ArmourAfterAFight(string armour, Action<DungeonRun> formation)
        {
            var run = new DungeonRun();
            formation(run);
            var tile = DungeonRunTests.PlaceableTiles(run).OrderBy(t => run.Map.WalkDistance(t)).First();
            Assert.That(run.Place(tile, MonsterKind.Skeleton), Is.True);
            run.DropOverride = _ => Item(armour);
            run.Start();
            DungeonRunTests.Drive(run, () => run.PendingLoot != null || run.Phase == RunPhase.Defeat);
            Assert.That(run.PendingLoot, Is.Not.Null);
            return run;
        }

        private static void Personality(DungeonRun run, HeroRole role, PersonalityTrait trait) =>
            run.SetBuild(role, run.BuildOf(role).With(trait));

        private static List<string> Marks(DungeonRun run) =>
            run.LastScene.Where(b => b.Kind == BeatKind.Line && b.Mark != null).Select(b => $"{b.Speaker}:{b.Mark}").ToList();

        [Test]
        public void AGreedyHeroBidsEvenWhenTheAiSaysNo()
        {
            var run = ArmourAfterAFight("사슬 갑옷", r => Personality(r, HeroRole.Healer, PersonalityTrait.Greedy));
            var loot = run.PendingLoot;
            Assert.That(loot.LocalWishes.Single(w => w.Role == HeroRole.Healer).Wants, Is.True);
            var nobody = loot.Heroes.Select(role => new LootWish(role, 0, false, "난 됐어")).ToList();
            run.LootRollOverride = () => 10;
            Assert.That(run.ResolveLoot(loot.Token, nobody), Is.True);
            Assert.That(Hero(run, HeroRole.Healer).BodyArmor?.Name, Is.EqualTo("사슬 갑옷"), "the greedy healer was the only bidder");
            Assert.That(Marks(run), Does.Contain("Healer:" + DialogueLines.Wants));
        }

        [Test]
        public void AGreedyHeroBidsOnWorseArmourButDropsOutOfTheRoll()
        {
            var run = ArmourAfterAFight("가죽 갑옷", r => Personality(r, HeroRole.Healer, PersonalityTrait.Greedy));
            var healer = Hero(run, HeroRole.Healer);
            healer.Equip(Item("사슬 갑옷"));
            var loot = run.PendingLoot;
            Assume.That(Item("가죽 갑옷").Power, Is.LessThan(Item("사슬 갑옷").Power));
            Assert.That(run.ResolveLoot(loot.Token), Is.True);
            Assert.That(Marks(run), Does.Contain("Healer:" + DialogueLines.Wants), "greed still declares a bid");
            Assert.That(run.LastScene.Any(b => b.Mark == DialogueLines.Worse && b.MarkRole == HeroRole.Healer), Is.True);
            Assert.That(healer.BodyArmor.Name, Is.EqualTo("사슬 갑옷"), "worse armour never replaces better");
        }

        [Test]
        public void AGenerousHeroStepsBackForABidderWhoGainsMore()
        {
            var run = ArmourAfterAFight("사슬 갑옷", r => Personality(r, HeroRole.Tank, PersonalityTrait.Generous));
            Hero(run, HeroRole.Tank).Equip(Item("가죽 갑옷"));
            var loot = run.PendingLoot;
            var wishes = loot.Heroes.Select(role => new LootWish(role, 0,
                role == HeroRole.Tank || role == HeroRole.MeleeDamage, "원해")).ToList();
            Assert.That(run.ResolveLoot(loot.Token, wishes), Is.True);
            Assert.That(Marks(run), Does.Contain("Tank:" + DialogueLines.Passes));
            Assert.That(run.LastScene.Single(b => b.Speaker == HeroRole.Tank && b.Mark != null).Text,
                Is.EqualTo(DialogueLines.GenerousPass("카이")));
            Assert.That(Hero(run, HeroRole.MeleeDamage).BodyArmor?.Name, Is.EqualTo("사슬 갑옷"), "no roll: one bidder left");
            Assert.That(run.LastScene.Any(b => b.Kind == BeatKind.Roll), Is.False);
        }

        [Test]
        public void AGenerousHeroKeepsTheirBidWhenNobodyWhoGainsMoreBids()
        {
            var run = ArmourAfterAFight("사슬 갑옷", r => Personality(r, HeroRole.Tank, PersonalityTrait.Generous));
            Hero(run, HeroRole.Tank).Equip(Item("가죽 갑옷"));
            var loot = run.PendingLoot;
            // The melee hero gains more but passes: the tank does not step back into nobody taking it.
            var wishes = loot.Heroes.Select(role => new LootWish(role, 0, role == HeroRole.Tank, "글쎄")).ToList();
            Assert.That(run.ResolveLoot(loot.Token, wishes), Is.True);
            Assert.That(Hero(run, HeroRole.Tank).BodyArmor.Name, Is.EqualTo("사슬 갑옷"));
        }

        [Test]
        public void TheGreedyHeroBidsAndTheGenerousOneStepsBackOnTheSameArmour()
        {
            var run = ArmourAfterAFight("사슬 갑옷", r =>
            {
                Personality(r, HeroRole.MeleeDamage, PersonalityTrait.Greedy);
                Personality(r, HeroRole.Tank, PersonalityTrait.Generous);
            });
            Hero(run, HeroRole.Tank).Equip(Item("가죽 갑옷"));
            // The back line already wears armour as good, so only the tank and the melee hero care.
            Hero(run, HeroRole.Healer).Equip(Item("축복받은 로브"));
            Hero(run, HeroRole.RangedDamage).Equip(Item("축복받은 로브"));
            Assume.That(Item("축복받은 로브").Power, Is.EqualTo(Item("사슬 갑옷").Power));
            Assert.That(run.ResolveLoot(run.PendingLoot.Token), Is.True);
            Assert.That(run.LastScene.Any(b => b.Kind == BeatKind.Roll), Is.False, "the generous tank leaves the greedy one alone");
            Assert.That(Hero(run, HeroRole.MeleeDamage).BodyArmor?.Name, Is.EqualTo("사슬 갑옷"));
            Assert.That(Hero(run, HeroRole.Tank).BodyArmor.Name, Is.EqualTo("가죽 갑옷"));
        }
    }
}
