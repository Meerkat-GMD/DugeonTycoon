using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>
    /// The traits that act outside the telegraphed patterns: whom combat traits hit, guard and heal, where they
    /// stand, what personality traits say in a fight, and how they bid, pray and vote. Each test compares the trait
    /// with a hero who lacks it in the same spot.
    /// </summary>
    public class TraitBehaviourTests
    {
        private const int Never = int.MaxValue / 2;
        private const CombatTrait Plain = CombatTrait.Lookout;

        /// <summary>
        /// A fight in the open stage-one boss room (x 33-38, y 2-7). Every hero starts with a trait that does nothing
        /// here (망보기 · 호기심쟁이) unless <paramref name="builds"/> says otherwise; the boss is out of play, heroes are
        /// at full health and frozen, monsters are engaged and frozen with no patterns ready.
        /// </summary>
        private static DungeonRun Arena(Action<DungeonRun> builds, MonsterKind[] kinds, int level = 1)
        {
            var run = DungeonRun.Sandbox();
            foreach (HeroRole role in Enum.GetValues(typeof(HeroRole)))
                Assert.That(run.SetBuild(role, new HeroBuild(Plain, PersonalityTrait.Curious)), Is.True);
            builds?.Invoke(run);
            foreach (var kind in kinds) Assert.That(run.Place(run.Map.BossTile, kind), Is.True);
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            int needed = (level - 1) * DungeonRun.ExperiencePerLevel - run.Experience;
            if (needed > 0) run.GrantExperience(needed);
            run.ReduceFatigue(run.FatigueSteps);
            foreach (var monster in run.Monsters)
            {
                monster.Mood = monster.IsBoss ? MonsterMood.Defeated : MonsterMood.Chasing;
                Freeze(monster);
            }
            foreach (var hero in run.Party)
            {
                hero.Hp = hero.MaxHp;
                hero.Critical = false;
                Freeze(hero);
            }
            Put(run, HeroRole.Tank, 33, 7);
            Put(run, HeroRole.MeleeDamage, 34, 7);
            Put(run, HeroRole.Healer, 37, 2);
            Put(run, HeroRole.RangedDamage, 38, 2);
            foreach (var monster in Foes(run)) Put(monster, 38, 7);
            run.TakeEvents();
            return run;
        }

        private static DungeonRun Arena(Action<DungeonRun> builds, params MonsterKind[] kinds) => Arena(builds, kinds, 1);

        private static void With(DungeonRun run, HeroRole role, CombatTrait trait) =>
            Assert.That(run.SetBuild(role, run.BuildOf(role).With(trait)), Is.True);

        private static void With(DungeonRun run, HeroRole role, PersonalityTrait trait) =>
            Assert.That(run.SetBuild(role, run.BuildOf(role).With(trait)), Is.True);

        private static void Freeze(HeroState hero)
        {
            hero.MoveReady = hero.ActReady = Never;
            hero.StepUntil = 0;
            hero.SkillReady[0] = hero.SkillReady[1] = Never;
        }

        private static void Freeze(MonsterState monster)
        {
            monster.MoveReady = monster.AttackReady = Never;
            monster.StepUntil = monster.TauntedUntil = 0;
            for (int i = 0; i < monster.PatternReady.Length; i++) monster.PatternReady[i] = Never;
        }

        /// <summary>Lets the hero act now, any step landed, with only the listed skills ready.</summary>
        private static void Ready(HeroState hero, params int[] skills)
        {
            hero.MoveReady = hero.ActReady = hero.StepUntil = 0;
            foreach (int index in skills) hero.SkillReady[index] = 0;
        }

        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];
        private static List<MonsterState> Foes(DungeonRun run) => run.Monsters.Where(m => !m.IsBoss).ToList();

        private static void Put(DungeonRun run, HeroRole role, int x, int y)
        {
            var tile = new TilePoint(x, y);
            Assert.That(run.Map.IsFloor(tile), Is.True, tile.ToString());
            run.PlaceHero(role, tile);
        }

        private static void Put(MonsterState monster, int x, int y) => monster.Tile = new TilePoint(x, y);

        /// <summary>Health at <paramref name="percent"/>% of the maximum, flagged critical below 30% as a blow would.</summary>
        private static void Wound(HeroState hero, int percent)
        {
            hero.Hp = Math.Max(1, hero.MaxHp * percent / 100);
            hero.Critical = hero.Hp * 10 < hero.MaxHp * 3;
        }

        private static int Chebyshev(TilePoint a, TilePoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        private static int Steps(DungeonRun run, TilePoint a, TilePoint b) => run.Map.FindPath(a, b).Count - 1;

        private static List<CombatEvent> HitsBy(IEnumerable<CombatEvent> events, HeroRole role) =>
            events.Where(e => e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)role).ToList();

        // ── Healers ──────────────────────────────────────────────────────

        [TestCase(CombatTrait.Triage, true)]
        [TestCase(CombatTrait.RescueLeader, false)]
        public void TriageHealsTheBadlyHurtAllyBeforeAMassHeal(CombatTrait trait, bool triage)
        {
            var run = Arena(r => With(r, HeroRole.Healer, trait), new[] { MonsterKind.Skeleton }, level: 3);
            Put(run, HeroRole.Healer, 35, 4);
            Put(run, HeroRole.Tank, 36, 4);
            Put(run, HeroRole.RangedDamage, 35, 5);
            Wound(Hero(run, HeroRole.Tank), 20);
            Wound(Hero(run, HeroRole.RangedDamage), 70);
            Ready(Hero(run, HeroRole.Healer), 0);
            run.Tick();
            var heals = run.TakeEvents().Where(e => e.Kind == CombatEventKind.Heal).Select(e => (HeroRole)e.Target).ToList();
            Assert.That(heals, triage ? Is.EqualTo(new[] { HeroRole.Tank }) : Is.EquivalentTo(new[] { HeroRole.Tank, HeroRole.RangedDamage }),
                triage ? "one heal for the tank, no mass heal" : "two wounded allies close by take the mass heal");
        }

        [TestCase(CombatTrait.RescueLeader, true)]
        [TestCase(CombatTrait.Triage, false)]
        public void ARescueLeaderRaisesTheFallenInsteadOfDodging(CombatTrait trait, bool raises)
        {
            var run = Arena(r => With(r, HeroRole.Healer, trait), new[] { MonsterKind.Zombie }, level: 3);
            var zombie = Foes(run).Single();
            Put(zombie, 37, 4);
            Put(run, HeroRole.Healer, 36, 4);
            Put(run, HeroRole.RangedDamage, 34, 4);
            Put(run, HeroRole.Tank, 33, 2);
            Put(run, HeroRole.MeleeDamage, 34, 2);
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.ForceCast(zombie, MonsterPattern.Slam);
            Ready(Hero(run, HeroRole.Healer));
            run.Tick();
            Assert.That(run.ReviveUsed, Is.EqualTo(raises));
            Assert.That(run.HeroTile(HeroRole.Healer) == new TilePoint(36, 4), Is.EqualTo(raises),
                raises ? "it stays in the slam to raise the fallen" : "it steps out of the slam first");
        }

        [Test]
        public void ACheererPraisesTheHealWhenACriticalAllyIsOutOfDanger()
        {
            var run = Arena(r => With(r, HeroRole.MeleeDamage, PersonalityTrait.Cheerer), MonsterKind.Skeleton);
            Put(run, HeroRole.Healer, 35, 4);
            Put(run, HeroRole.Tank, 36, 4);
            var tank = Hero(run, HeroRole.Tank);
            tank.Hp = tank.MaxHp * 3 / 10 - 1;
            tank.Critical = true;
            Ready(Hero(run, HeroRole.Healer));
            run.Tick();
            Assert.That(tank.Critical, Is.False, "the heal lifts the tank to 30% or more");
            Assert.That(Hero(run, HeroRole.MeleeDamage).LastLine, Does.Contain("세레나").And.Contain("가론"));
            Assert.That(run.Log.Any(line => line.StartsWith("칭찬")), Is.True);
        }

        // ── Whom to hit ──────────────────────────────────────────────────

        [TestCase(CombatTrait.Avenger, true)]
        [TestCase(Plain, false)]
        public void AnAvengerGoesForTheMonsterThatDownedAnAlly(CombatTrait trait, bool avenges)
        {
            var run = Arena(r => With(r, HeroRole.MeleeDamage, trait), MonsterKind.Skeleton, MonsterKind.Skeleton);
            var culprit = Foes(run)[0];
            var weak = Foes(run)[1];
            Put(run, HeroRole.Tank, 34, 4);
            Put(culprit, 35, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            Put(weak, 37, 4);
            Hero(run, HeroRole.Tank).Hp = 1;
            culprit.AttackReady = 0;
            run.Tick();
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.Zero, "the skeleton downs the tank");
            weak.Hp = 5;
            Ready(Hero(run, HeroRole.MeleeDamage));
            run.TakeEvents();
            run.Tick();
            var hit = HitsBy(run.TakeEvents(), HeroRole.MeleeDamage).Single();
            Assert.That(hit.Target, Is.EqualTo(avenges ? culprit.Id : weak.Id));
        }

        [TestCase(CombatTrait.Finisher, true)]
        [TestCase(Plain, false)]
        public void AFinisherEndsTheNearlyDeadOldFoeOnceReinforcementsArrive(CombatTrait trait, bool finishes)
        {
            var run = Arena(r => With(r, HeroRole.MeleeDamage, trait), MonsterKind.Skeleton, MonsterKind.Skeleton);
            var old = Foes(run)[0];
            var fresh = Foes(run)[1];
            fresh.Reinforcement = true;
            Put(old, 35, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            Put(fresh, 37, 4);
            old.Hp = old.MaxHp / 10;
            fresh.Hp = old.Hp - 1;
            Ready(Hero(run, HeroRole.MeleeDamage));
            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.MeleeDamage).Single().Target, Is.EqualTo(finishes ? old.Id : fresh.Id));
        }

        [TestCase(CombatTrait.GiantHunter, true)]
        [TestCase(Plain, false)]
        public void AGiantHunterTurnsOnABigReinforcementInReach(CombatTrait trait, bool hunts)
        {
            var run = Arena(r => With(r, HeroRole.RangedDamage, trait), MonsterKind.Skeleton, MonsterKind.Zombie);
            var skeleton = Foes(run).Single(m => m.Kind == MonsterKind.Skeleton);
            var zombie = Foes(run).Single(m => m.Kind == MonsterKind.Zombie);
            zombie.Reinforcement = true;
            Put(run, HeroRole.RangedDamage, 34, 4);
            Put(skeleton, 37, 3);
            Put(zombie, 37, 5);
            Ready(Hero(run, HeroRole.RangedDamage));
            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.RangedDamage).Single().Target, Is.EqualTo(hunts ? zombie.Id : skeleton.Id));
        }

        // ── Where to stand ───────────────────────────────────────────────

        [TestCase(CombatTrait.Bodyguard, true)]
        [TestCase(Plain, false)]
        public void ABodyguardDrawsTheBlowMeantForTheCriticalHealer(CombatTrait trait, bool guards)
        {
            var run = Arena(r => With(r, HeroRole.Tank, trait), MonsterKind.Skeleton);
            var skeleton = Foes(run).Single();
            Put(skeleton, 36, 4);
            Put(run, HeroRole.Healer, 36, 5);
            Put(run, HeroRole.Tank, 34, 4);
            Wound(Hero(run, HeroRole.Healer), 20);
            Ready(Hero(run, HeroRole.Tank));
            run.Tick();
            Assert.That(Chebyshev(run.HeroTile(HeroRole.Tank), skeleton.Tile), Is.EqualTo(1), "both walk up to the skeleton");
            Freeze(Hero(run, HeroRole.Tank));
            skeleton.AttackReady = 0;
            run.TakeEvents();
            run.Tick();
            var blow = run.TakeEvents().Single(e => e.Kind == CombatEventKind.Attack && !e.FromHero);
            Assert.That((HeroRole)blow.Target, Is.EqualTo(guards ? HeroRole.Tank : HeroRole.Healer));
        }

        [Test]
        public void AnInterceptorOnlyAnswersAReinforcement()
        {
            var run = Arena(r => With(r, HeroRole.MeleeDamage, CombatTrait.Interceptor), MonsterKind.Skeleton);
            var skeleton = Foes(run).Single();
            Put(skeleton, 36, 4);
            Put(run, HeroRole.RangedDamage, 36, 5);
            Put(run, HeroRole.MeleeDamage, 33, 4);
            Wound(Hero(run, HeroRole.RangedDamage), 20);
            Ready(Hero(run, HeroRole.MeleeDamage));
            run.Tick();
            Assert.That(run.Log.Any(line => line.Contains("가로막는 자")), Is.False, "the skeleton was there from the start");
            skeleton.Reinforcement = true;
            Ready(Hero(run, HeroRole.MeleeDamage));
            run.Tick();
            Assert.That(run.Log.Any(line => line.Contains("가로막는 자")), Is.True);
        }

        [TestCase(CombatTrait.SurvivalFirst, true)]
        [TestCase(Plain, false)]
        public void SurvivalFirstFallsBackToTheHealerInsteadOfFighting(CombatTrait trait, bool fallsBack)
        {
            var run = Arena(r => With(r, HeroRole.MeleeDamage, trait), MonsterKind.Skeleton);
            var skeleton = Foes(run).Single();
            Put(run, HeroRole.Healer, 33, 2);
            Put(run, HeroRole.MeleeDamage, 38, 7);
            Put(skeleton, 38, 6);
            Wound(Hero(run, HeroRole.MeleeDamage), 20);
            Ready(Hero(run, HeroRole.MeleeDamage));
            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.MeleeDamage), fallsBack ? Is.Empty : Has.Count.EqualTo(1));
            Assert.That(Steps(run, run.HeroTile(HeroRole.MeleeDamage), run.HeroTile(HeroRole.Healer)), Is.EqualTo(fallsBack ? 9 : 10),
                fallsBack ? "one step toward the healer" : "it stays and fights");
        }

        [TestCase(CombatTrait.Cornered, true)]
        [TestCase(Plain, false)]
        public void TheCorneredStandAndShootInsteadOfBackingOff(CombatTrait trait, bool stands)
        {
            var run = Arena(r => With(r, HeroRole.RangedDamage, trait), MonsterKind.Skeleton);
            var skeleton = Foes(run).Single();
            Put(run, HeroRole.RangedDamage, 35, 4);
            Put(skeleton, 36, 4);
            Wound(Hero(run, HeroRole.RangedDamage), 20);
            Ready(Hero(run, HeroRole.RangedDamage));
            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.RangedDamage), stands ? Has.Count.EqualTo(1) : Is.Empty);
            Assert.That(run.HeroTile(HeroRole.RangedDamage) == new TilePoint(35, 4), Is.EqualTo(stands));
        }

        [Test]
        public void ALineKeeperCallsTheBackLineAwayFromTheFightWhenTheFrontFalls()
        {
            var run = Arena(r => With(r, HeroRole.RangedDamage, CombatTrait.LineKeeper), MonsterKind.Skeleton);
            var skeleton = Foes(run).Single();
            Put(run, HeroRole.Tank, 35, 4);
            Put(skeleton, 36, 4);
            Put(run, HeroRole.RangedDamage, 35, 6);
            Put(run, HeroRole.Healer, 34, 5);
            Hero(run, HeroRole.Tank).Hp = 1;
            skeleton.AttackReady = 0;
            run.Tick();
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.Zero);
            Assert.That(run.RallyPoint, Is.Not.Null);
            Assert.That(Hero(run, HeroRole.RangedDamage).LastLine, Does.Contain("모여"));
            var rally = run.RallyPoint.Value;
            Assert.That(Chebyshev(rally, skeleton.Tile), Is.GreaterThan(Chebyshev(new TilePoint(35, 6), skeleton.Tile)));
            var before = run.HeroTile(HeroRole.Healer);
            Ready(Hero(run, HeroRole.Healer));
            run.Tick();
            Assert.That(Steps(run, run.HeroTile(HeroRole.Healer), rally), Is.LessThan(Steps(run, before, rally)),
                "the back line walks to the rally point");
        }

        // ── Calling out ──────────────────────────────────────────────────

        [Test]
        public void ALookoutNamesTheReinforcementsAndWhereTheyComeFrom()
        {
            var run = Arena(null, MonsterKind.Skeleton, MonsterKind.Ghost);
            var ghost = Foes(run).Single(m => m.Kind == MonsterKind.Ghost);
            ghost.Mood = MonsterMood.Wandering;
            Put(run, HeroRole.RangedDamage, 34, 4);
            Put(ghost, 37, 4);
            run.Tick();
            Assert.That(ghost.Mood, Is.EqualTo(MonsterMood.Chasing));
            Assert.That(ghost.Reinforcement, Is.True);
            Assert.That(Foes(run).Single(m => m.Kind == MonsterKind.Skeleton).Reinforcement, Is.False);
            Assert.That(Hero(run, HeroRole.RangedDamage).LastLine, Does.StartWith("동쪽!").And.Contain("유령"));
        }

        [Test]
        public void ANaggerPointsOutWhatTheAllyCaughtByASlamMissed()
        {
            var run = Arena(r => With(r, HeroRole.RangedDamage, PersonalityTrait.Grumbler), MonsterKind.Zombie);
            var zombie = Foes(run).Single();
            Put(zombie, 36, 4);
            Put(run, HeroRole.Tank, 36, 5);
            var cast = run.ForceCast(zombie, MonsterPattern.Slam);
            for (int i = 0; i < 200 && cast.Phase != CastPhase.Fired; i++) run.Tick();
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Fired));
            Assert.That(Hero(run, HeroRole.RangedDamage).LastLine, Does.Contain("가론").And.Contain("바닥"));
        }

        // ── Armour ───────────────────────────────────────────────────────

        private static Equipment Item(string name) => EquipmentCatalog.Find(name) ?? throw new ArgumentException(name);

        /// <summary>One skeleton by the entrance drops <paramref name="drop"/>; the fight ends with the armour pending.</summary>
        private static DungeonRun Shared(Action<DungeonRun> builds, string drop)
        {
            var run = DungeonRun.Sandbox();
            builds(run);
            var tile = DungeonRunTests.PlaceableTiles(run).OrderBy(t => run.Map.WalkDistance(t)).First();
            Assert.That(run.Place(tile, MonsterKind.Skeleton), Is.True);
            var drops = new Queue<string>(new[] { drop });
            run.DropOverride = _ => drops.Count > 0 ? Item(drops.Dequeue()) : null;
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            for (int i = 0; i < 5000 && run.Phase == RunPhase.Combat; i++) run.Tick();
            Assert.That(run.PendingLoot, Is.Not.Null);
            return run;
        }

        private static LootWish Local(DungeonRun run, HeroRole role) => run.PendingLoot.LocalWishes.Single(w => w.Role == role);

        [TestCase("가죽 갑옷", true)]
        [TestCase("누빈 조끼", false)]
        public void AnAppraiserBidsOnlyForAGainOfFive(string armour, bool bids)
        {
            var run = Shared(r => With(r, HeroRole.Tank, PersonalityTrait.Appraiser), armour);
            Assert.That(Item(armour).Power, Is.EqualTo(bids ? 5 : 4));
            Assert.That(Local(run, HeroRole.Tank).Wants, Is.EqualTo(bids));
            // The trait overrules the AI either way.
            var wishes = run.PendingLoot.Heroes.Select(role => new LootWish(role, 0, role == HeroRole.Tank ? !bids : false, "AI")).ToList();
            run.LootRollOverride = () => 10;
            Assert.That(run.ResolveLoot(run.PendingLoot.Token, wishes), Is.True);
            Assert.That(Hero(run, HeroRole.Tank).BodyArmor?.Name == armour, Is.EqualTo(bids));
        }

        [Test]
        public void ACollectorAlwaysBidsOnRareArmour()
        {
            var run = Shared(r => With(r, HeroRole.Healer, PersonalityTrait.Collector), "사슬 갑옷");
            var nobody = run.PendingLoot.Heroes.Select(role => new LootWish(role, 0, false, "AI")).ToList();
            Assert.That(run.ResolveLoot(run.PendingLoot.Token, nobody), Is.True);
            Assert.That(Hero(run, HeroRole.Healer).BodyArmor?.Name, Is.EqualTo("사슬 갑옷"));
        }

        [Test]
        public void AnUnderdogStepsBackForTheBidderWithTheLeastArmour()
        {
            var run = Shared(r => With(r, HeroRole.Tank, PersonalityTrait.Underdog), "가죽 갑옷");
            var tank = Local(run, HeroRole.Tank);
            Assert.That(tank.Wants, Is.False);
            Assert.That(tank.Line, Does.Contain("리아"), "the ranged hero has the least armour");
            Assert.That(Local(run, HeroRole.RangedDamage).Wants, Is.True);
        }

        [Test]
        public void APeacemakerSumsUpTheGainsBeforeTheRoll()
        {
            var run = Shared(r => With(r, HeroRole.Tank, PersonalityTrait.Peacemaker), "가죽 갑옷");
            var two = run.PendingLoot.Heroes
                .Select(role => new LootWish(role, 0, role == HeroRole.MeleeDamage || role == HeroRole.RangedDamage, "AI")).ToList();
            var faces = new Queue<int>(new[] { 15, 3 });
            run.LootRollOverride = () => faces.Dequeue();
            Assert.That(run.ResolveLoot(run.PendingLoot.Token, two), Is.True);
            var call = run.LastScene.Single(b => b.Kind == BeatKind.Line && b.Speaker == HeroRole.Tank && b.Text.Contains("판정"));
            Assert.That(call.Text, Does.Contain("카이 +").And.Contain("리아 +"));
            Assert.That(run.LastScene.Count(b => b.Kind == BeatKind.Roll), Is.EqualTo(1), "the roll still happens");
            Assert.That(run.LastScene.ToList().FindIndex(b => b == call), Is.LessThan(run.LastScene.ToList().FindIndex(b => b.Kind == BeatKind.Roll)));
        }

        // ── Prayer ───────────────────────────────────────────────────────

        private static DungeonRun Marching(Action<DungeonRun> builds)
        {
            var run = DungeonRun.Sandbox();
            builds(run);
            run.Start();
            Assert.That(ExplorationTests.TickToFirstFork(run), Is.True);
            Assert.That(run.ResolveFork(run.PendingFork.Token), Is.True);
            run.SetFaith(2);
            return run;
        }

        [Test]
        public void ADevoutAllyPraysForTheDesperateHeroAndTheMiracleHealsThem()
        {
            var run = Marching(r => With(r, HeroRole.Healer, PersonalityTrait.Devout));
            Hero(run, HeroRole.Tank).Hp = 5;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.Tick();
            var prayer = run.PendingPrayer;
            Assert.That(prayer.Role, Is.EqualTo(HeroRole.Healer));
            Assert.That(prayer.For, Is.EqualTo(HeroRole.Tank));
            Assert.That(prayer.Local.Line, Does.Contain("가론"));
            // The AI cannot talk the devout out of it.
            Assert.That(run.HearPrayer(prayer.Token, new PrayerProposal(false, "")).Praying, Is.True);
            Assert.That(run.AnswerPrayer(prayer.Token, PrayerAnswer.HealingMiracle), Is.True);
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.EqualTo(Hero(run, HeroRole.Tank).MaxHp));
            Assert.That(Hero(run, HeroRole.Healer).Belief, Is.EqualTo(Belief.Firm));
            Assert.That(Hero(run, HeroRole.Tank).Belief, Is.EqualTo(Belief.Steady), "only the one who prayed is moved");
        }

        [Test]
        public void ADevoutHeroInDespairPraysEvenWhenTheAiSaysNo()
        {
            var run = Marching(r => With(r, HeroRole.Tank, PersonalityTrait.Devout));
            Hero(run, HeroRole.Tank).Hp = 5;
            Hero(run, HeroRole.RangedDamage).Hp = 0;
            run.Tick();
            var prayer = run.PendingPrayer;
            Assert.That(prayer.Role, Is.EqualTo(HeroRole.Tank));
            Assert.That(prayer.ForAlly, Is.False);
            Assert.That(run.HearPrayer(prayer.Token, new PrayerProposal(false, "")).Praying, Is.True);
        }

        // ── Forks ────────────────────────────────────────────────────────

        [Test]
        public void ANavigatorHeadsForTheRememberedBossMarker()
        {
            var run = DungeonRun.Sandbox();
            With(run, HeroRole.Tank, PersonalityTrait.Navigator);
            run.Start();
            var path = new[] { new TilePoint(0, 0), new TilePoint(1, 0) };
            var options = new[]
            {
                new ForkOption(0, "북쪽 길", path, new HashSet<TilePoint>(), 0, 10, markerDistance: 12),
                new ForkOption(1, "동쪽 길", path, new HashSet<TilePoint>(), 0, 2, markerDistance: 3),
            };
            var navigator = run.LocalOpinion(Hero(run, HeroRole.Tank), options);
            Assert.That(navigator.Option, Is.EqualTo(1));
            Assert.That(navigator.Line, Does.Contain("표식"));
            var unmarked = options.Select(o => new ForkOption(o.Index, o.Label, path, new HashSet<TilePoint>(), 0, o.Depth)).ToList();
            Assert.That(run.LocalOpinion(Hero(run, HeroRole.Tank), unmarked).Line, Does.Not.Contain("표식"),
                "without a marker the navigator argues like anyone else");
        }
    }
}
