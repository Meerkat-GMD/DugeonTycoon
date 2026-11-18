using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class PatternTests
    {
        private const int Never = int.MaxValue / 2;

        /// <summary>
        /// A frozen fight in the open stage-one boss room (x 33-41, y 2-10). Every hero is at full health and frozen;
        /// every monster, the boss included, is engaged and frozen with its patterns on cooldown. Every hero has a
        /// combat trait that ignores patterns, so the role defaults decide.
        /// </summary>
        private static DungeonRun Arena(params MonsterKind[] kinds) => Arena(false, kinds);

        /// <summary>The same fight with the default formation's traits: 동료를 지키는 자 tank, 딜에 미친 자 melee, 재빠른 발 ranged.</summary>
        private static DungeonRun TraitArena(params MonsterKind[] kinds) => Arena(true, kinds);

        private static DungeonRun Arena(bool traits, params MonsterKind[] kinds)
        {
            var run = new DungeonRun();
            if (!traits)
                foreach (HeroRole role in System.Enum.GetValues(typeof(HeroRole)))
                    Assert.That(run.SetBuild(role, new HeroBuild(CombatTrait.Lookout, PersonalityTrait.Curious)), Is.True);
            foreach (var kind in kinds) Assert.That(run.Place(run.Map.BossTile, kind), Is.True);
            run.Start();
            DungeonRunTests.Drive(run, () => run.Phase == RunPhase.Combat);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Combat));
            run.ReduceFatigue(run.FatigueSteps);
            foreach (var monster in run.Monsters)
            {
                monster.Mood = MonsterMood.Chasing;
                monster.MoveReady = monster.AttackReady = Never;
                monster.StepUntil = 0;
                for (int i = 0; i < monster.PatternReady.Length; i++) monster.PatternReady[i] = Never;
            }
            foreach (var hero in run.Party)
            {
                hero.Hp = hero.MaxHp;
                hero.Critical = false;
                hero.MoveReady = hero.ActReady = Never;
                hero.StepUntil = 0;
                hero.SkillReady[0] = hero.SkillReady[1] = Never;
            }
            // Out of the way in the room's corners unless a test moves them.
            Put(run, HeroRole.Tank, 33, 7);
            Put(run, HeroRole.MeleeDamage, 34, 7);
            Put(run, HeroRole.Healer, 37, 2);
            Put(run, HeroRole.RangedDamage, 38, 2);
            foreach (var monster in run.Monsters) Put(monster, 38, 7);
            run.TakeEvents();
            return run;
        }

        private static void Put(DungeonRun run, HeroRole role, int x, int y) => run.PlaceHero(role, new TilePoint(x, y));
        private static void Put(MonsterState monster, int x, int y) => monster.Tile = new TilePoint(x, y);
        private static HeroState Hero(DungeonRun run, HeroRole role) => run.Party[(int)role];
        private static MonsterState Foe(DungeonRun run) => run.Monsters.First(m => !m.IsBoss);

        private static void LetMove(HeroState hero) => hero.MoveReady = 0;

        private static void TickUntilFired(DungeonRun run, PatternCast cast, List<CombatEvent> events = null)
        {
            while (run.Ticks < cast.FireTick)
            {
                run.Tick();
                events?.AddRange(run.TakeEvents());
            }
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Fired));
        }

        private static List<CombatEvent> HitsBy(IEnumerable<CombatEvent> events, HeroRole role) =>
            events.Where(e => e.Kind == CombatEventKind.Attack && e.FromHero && e.Source == (int)role).ToList();

        // ── Moving ───────────────────────────────────────────────────────

        [Test]
        public void AHeroMidStepNeitherAttacksNorUsesSkillsUntilItLands()
        {
            var run = Arena(MonsterKind.Skeleton);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(run, HeroRole.MeleeDamage, 35, 4);
            Put(Foe(run), 36, 4);
            melee.ActReady = 0;
            melee.SkillReady[0] = 0;
            melee.StepUntil = run.Ticks + 2;

            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.MeleeDamage), Is.Empty, "still stepping");
            Assert.That(run.SkillCooldownLeft(HeroRole.MeleeDamage, 0), Is.Zero, "no power strike either");

            run.Tick();
            Assert.That(HitsBy(run.TakeEvents(), HeroRole.MeleeDamage), Is.Not.Empty, "landed, so it strikes");
        }

        [Test]
        public void AMonsterMidStepDoesNotAttackUntilItLands()
        {
            var run = Arena(MonsterKind.Skeleton);
            Put(run, HeroRole.Tank, 35, 4);
            var skeleton = Foe(run);
            Put(skeleton, 36, 4);
            skeleton.AttackReady = 0;
            skeleton.StepUntil = run.Ticks + 2;
            bool Struck(IEnumerable<CombatEvent> events) => events.Any(e => e.Kind == CombatEventKind.Attack && !e.FromHero && e.Source == skeleton.Id);

            run.Tick();
            Assert.That(Struck(run.TakeEvents()), Is.False);
            run.Tick();
            Assert.That(Struck(run.TakeEvents()), Is.True);
        }

        // ── Slam ─────────────────────────────────────────────────────────

        [Test]
        public void ASlamHitsWhoeverStaysInItsSquare_TheTankHoldsAndTheMeleeHeroStepsOut()
        {
            var run = Arena(MonsterKind.Zombie);
            var zombie = Foe(run);
            var tank = Hero(run, HeroRole.Tank);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(zombie, 35, 4);
            Put(run, HeroRole.Tank, 34, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            LetMove(melee);
            melee.ActReady = 0;
            zombie.PatternReady[0] = 0;

            run.Tick();
            var events = run.TakeEvents();
            var cast = run.Casts.Single();
            Assert.That(cast.Pattern, Is.EqualTo(MonsterPattern.Slam));
            Assert.That(cast.Area.Count, Is.EqualTo(9));
            Assert.That(cast.Area, Does.Contain(new TilePoint(34, 4)).And.Contain(new TilePoint(36, 4)));
            Assert.That(events.Single(e => e.Kind == CombatEventKind.Pattern).Amount, Is.EqualTo((int)MonsterPattern.Slam));
            Assert.That(run.PartyKnows(zombie, MonsterPattern.Slam), Is.True);
            Assert.That(run.PartyKnows(zombie, MonsterPattern.TrackingBeam), Is.False);

            var later = new List<CombatEvent>();
            TickUntilFired(run, cast, later);
            Assert.That(cast.Hits, Is.EqualTo(new[] { (int)HeroRole.Tank }));
            Assert.That(cast.Area, Does.Not.Contain(run.StandingTile(HeroRole.MeleeDamage)));
            Assert.That(tank.Hp, Is.EqualTo(tank.MaxHp - (zombie.Attack * 150 / 100 - tank.Armor)));
            Assert.That(melee.Hp, Is.EqualTo(melee.MaxHp));
            Assert.That(HitsBy(later, HeroRole.MeleeDamage), Is.Empty, "dodging cost the melee hero its blows");
            Assert.That(run.Log, Has.Some.EqualTo($"{melee.Name} • 내려찍기 회피"));
            Assert.That(run.Log, Has.Some.EqualTo($"{tank.Name} • 내려찍기 버팀"));
        }

        [Test]
        public void AHeroThatCannotGetOutInTimeKeepsFightingAndIsHit()
        {
            var run = Arena(MonsterKind.Zombie);
            var zombie = Foe(run);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(zombie, 35, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            zombie.PatternReady[0] = 0;
            run.Tick();
            var cast = run.Casts.Single();
            // Free to move only two ticks before the slam lands: a step takes three.
            melee.MoveReady = cast.FireTick - 2;
            melee.ActReady = 0;
            var events = new List<CombatEvent>();
            TickUntilFired(run, cast, events);
            Assert.That(run.HeroTile(HeroRole.MeleeDamage), Is.EqualTo(new TilePoint(36, 4)));
            Assert.That(cast.Hits, Does.Contain((int)HeroRole.MeleeDamage));
            Assert.That(HitsBy(events, HeroRole.MeleeDamage), Is.Not.Empty, "it kept attacking instead");
        }

        [Test]
        public void AHeroStillMidStepIsHitWhereItStarted()
        {
            var run = Arena(MonsterKind.Zombie);
            var zombie = Foe(run);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(zombie, 35, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            zombie.PatternReady[0] = 0;
            run.Tick();
            var cast = run.Casts.Single();
            while (run.Ticks < cast.FireTick - 1) run.Tick();
            // Leaving the square one tick before it lands: the step ends a tick too late.
            Put(run, HeroRole.MeleeDamage, 37, 4);
            melee.StepFrom = new TilePoint(36, 4);
            melee.StepUntil = cast.FireTick + 1;
            run.Tick();
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Fired));
            Assert.That(cast.Hits, Does.Contain((int)HeroRole.MeleeDamage));
        }

        // ── Tracking beam ────────────────────────────────────────────────

        /// <summary>The boss at the west wall aims its beam at the tank; the healer stands right behind the tank on that line.</summary>
        private static (DungeonRun run, PatternCast cast) BeamAtTheTank(PatternResponse? tankAnswer)
        {
            var run = Arena();
            var boss = run.Boss;
            Put(boss, 33, 4);
            Put(run, HeroRole.Tank, 35, 4);
            Put(run, HeroRole.Healer, 37, 4);
            LetMove(Hero(run, HeroRole.Tank));
            if (tankAnswer.HasValue)
            {
                var answer = new Dictionary<MonsterPattern, PatternResponse> { { MonsterPattern.TrackingBeam, tankAnswer.Value } };
                Assert.That(run.ApplyTactics(run.Situation, new[] { new HeroTactic(HeroRole.Tank, -1, -1, "계속 버틴다!", answer) }), Is.True);
            }
            var cast = run.ForceCast(boss, MonsterPattern.TrackingBeam, HeroRole.Tank);
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Aiming));
            Assert.That(cast.Area, Does.Contain(new TilePoint(37, 4)), "the healer starts on the line");
            return (run, cast);
        }

        [Test]
        public void TheBeamsTargetLuresItAwaySoOnlyItIsHit()
        {
            var (run, cast) = BeamAtTheTank(null);
            var tank = Hero(run, HeroRole.Tank);
            TickUntilFired(run, cast);
            Assert.That(run.HeroTile(HeroRole.Tank), Is.Not.EqualTo(new TilePoint(35, 4)), "the tank walked off the healer's line");
            Assert.That(cast.Hits, Is.EqualTo(new[] { (int)HeroRole.Tank }));
            Assert.That(Hero(run, HeroRole.Healer).Hp, Is.EqualTo(Hero(run, HeroRole.Healer).MaxHp));
            Assert.That(run.Log, Has.Some.EqualTo($"{tank.Name} • 추적 레이저 유도"));
        }

        [Test]
        public void ATargetThatHoldsLetsTheBeamGoThroughToTheAllyBehind()
        {
            var (run, cast) = BeamAtTheTank(PatternResponse.Hold);
            TickUntilFired(run, cast);
            Assert.That(run.HeroTile(HeroRole.Tank).Y, Is.EqualTo(4), "it kept walking straight at the boss");
            Assert.That(cast.Hits, Is.EquivalentTo(new[] { (int)HeroRole.Tank, (int)HeroRole.Healer }));
        }

        [Test]
        public void TheBeamFollowsItsTargetUntilItLocks()
        {
            var (run, cast) = BeamAtTheTank(null);
            while (run.Ticks < cast.LockTick) run.Tick();
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Locked));
            Assert.That(cast.Area, Does.Contain(run.StandingTile(HeroRole.Tank)));
            Assert.That(cast.Area, Does.Not.Contain(new TilePoint(37, 4)));
            var locked = cast.Area.ToList();
            run.Tick();
            Assert.That(cast.Area, Is.EquivalentTo(locked), "no more tracking once locked");
        }

        [Test]
        public void TheBeamAlsoScorchesTheTilesAroundItsCaster()
        {
            var run = Arena();
            var boss = run.Boss;
            Put(boss, 36, 6);
            Put(run, HeroRole.Tank, 39, 6);
            // Diagonally behind the caster: no line toward the tank passes here, only the scorched ring does.
            Put(run, HeroRole.MeleeDamage, 35, 5);
            var cast = run.ForceCast(boss, MonsterPattern.TrackingBeam, HeroRole.Tank);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                Assert.That(cast.Area, Does.Contain(new TilePoint(36 + dx, 6 + dy)));
            TickUntilFired(run, cast);
            Assert.That(cast.Hits, Does.Contain((int)HeroRole.MeleeDamage), "frozen beside the caster, it cannot get out");
        }

        [Test]
        public void ABeamIsCalledOffWhenItsCasterFalls()
        {
            var (run, cast) = BeamAtTheTank(null);
            run.Tick();
            run.Boss.Mood = MonsterMood.Defeated;
            run.Monsters.Where(m => !m.IsBoss).ToList().ForEach(m => m.Mood = MonsterMood.Defeated);
            int before = Hero(run, HeroRole.Tank).Hp;
            for (int i = 0; i < 30; i++) run.Tick();
            Assert.That(run.Casts, Is.Empty);
            Assert.That(Hero(run, HeroRole.Tank).Hp, Is.EqualTo(before));
        }

        // ── Traits ───────────────────────────────────────────────────────

        [Test]
        public void UnderASlamTheProtectorStepsOutAndTheDamageMadKeepsHitting()
        {
            var run = TraitArena(MonsterKind.Zombie);
            var zombie = Foe(run);
            var tank = Hero(run, HeroRole.Tank);
            var melee = Hero(run, HeroRole.MeleeDamage);
            Assert.That(tank.CombatTrait, Is.EqualTo(CombatTrait.Protector));
            Assert.That(melee.CombatTrait, Is.EqualTo(CombatTrait.DamageMad));
            Put(zombie, 35, 4);
            Put(run, HeroRole.Tank, 34, 4);
            Put(run, HeroRole.MeleeDamage, 36, 4);
            LetMove(tank);
            LetMove(melee);
            melee.ActReady = 0;
            zombie.PatternReady[0] = 0;
            run.Tick();
            var cast = run.Casts.Single();
            var events = new List<CombatEvent>();
            TickUntilFired(run, cast, events);
            Assert.That(cast.Hits, Is.EqualTo(new[] { (int)HeroRole.MeleeDamage }));
            Assert.That(run.HeroTile(HeroRole.MeleeDamage), Is.EqualTo(new TilePoint(36, 4)));
            Assert.That(HitsBy(events, HeroRole.MeleeDamage), Is.Not.Empty, "it kept dealing damage");
            Assert.That(run.Log, Has.Some.EqualTo($"{melee.Name} • 내려찍기 버팀"));
            Assert.That(run.Log, Has.Some.EqualTo($"{tank.Name} • 내려찍기 회피"));
        }

        [Test]
        public void ADamageMadBeamTargetKeepsHittingAndTheAllyBehindItIsHitToo()
        {
            var run = TraitArena();
            var melee = Hero(run, HeroRole.MeleeDamage);
            Put(run.Boss, 33, 4);
            Put(run, HeroRole.MeleeDamage, 35, 4);
            Put(run, HeroRole.Healer, 37, 4);
            Put(run, HeroRole.Tank, 34, 9);
            LetMove(melee);
            var cast = run.ForceCast(run.Boss, MonsterPattern.TrackingBeam, HeroRole.MeleeDamage);
            TickUntilFired(run, cast);
            Assert.That(run.HeroTile(HeroRole.MeleeDamage).Y, Is.EqualTo(4), "it never stepped off the line");
            Assert.That(cast.Hits, Is.EquivalentTo(new[] { (int)HeroRole.MeleeDamage, (int)HeroRole.Healer }));
        }

        [Test]
        public void AStoicHoldsAGroundBlastOnlyWhileHealthy()
        {
            var fresh = new DungeonRun();
            Assert.That(fresh.SetBuild(HeroRole.Healer, new HeroBuild(CombatTrait.Stoic, PersonalityTrait.Curious)), Is.True);
            var healer = fresh.Party[(int)HeroRole.Healer];
            Assert.That(fresh.ResponseTo(HeroRole.Healer, MonsterPattern.Meteor, false), Is.EqualTo(PatternResponse.Hold));
            healer.Hp = healer.MaxHp * DungeonRun.StoicHoldPercent / 100 - 1;
            Assert.That(fresh.ResponseTo(HeroRole.Healer, MonsterPattern.Meteor, false), Is.EqualTo(PatternResponse.Dodge));
            Assert.That(fresh.ResponseTo(HeroRole.Healer, MonsterPattern.SoulBolt, false), Is.EqualTo(PatternResponse.Dodge));
        }

        // ── Meteor and choices ───────────────────────────────────────────

        [Test]
        public void TheMeteorFallsOnTheMostCrowdedHeroThenTheMostHurt()
        {
            var run = Arena();
            Put(run, HeroRole.Tank, 33, 2);
            Put(run, HeroRole.MeleeDamage, 37, 6);
            Put(run, HeroRole.Healer, 38, 6);
            Put(run, HeroRole.RangedDamage, 37, 7);
            Hero(run, HeroRole.RangedDamage).Hp -= 5;
            Assert.That(run.CrowdedHero().Role, Is.EqualTo(HeroRole.RangedDamage));
        }

        [Test]
        public void ResponsesComeFromTheTacticThenTheRole()
        {
            var run = Arena();
            Assert.That(run.ResponseTo(HeroRole.Tank, MonsterPattern.Slam, false), Is.EqualTo(PatternResponse.Hold));
            Assert.That(run.ResponseTo(HeroRole.MeleeDamage, MonsterPattern.Slam, false), Is.EqualTo(PatternResponse.Dodge));
            Assert.That(run.ResponseTo(HeroRole.Healer, MonsterPattern.TrackingBeam, true), Is.EqualTo(PatternResponse.Lure));
            Assert.That(run.ResponseTo(HeroRole.Healer, MonsterPattern.TrackingBeam, false), Is.EqualTo(PatternResponse.Dodge));

            var answers = new Dictionary<MonsterPattern, PatternResponse>
            {
                { MonsterPattern.Slam, PatternResponse.Lure },
                { MonsterPattern.Meteor, PatternResponse.Hold }
            };
            run.ApplyTactics(run.Situation, new[] { new HeroTactic(HeroRole.MeleeDamage, -1, -1, "계속 친다!", answers) });
            Assert.That(run.ResponseTo(HeroRole.MeleeDamage, MonsterPattern.Meteor, false), Is.EqualTo(PatternResponse.Hold));
            Assert.That(run.ResponseTo(HeroRole.MeleeDamage, MonsterPattern.Slam, false), Is.EqualTo(PatternResponse.Dodge),
                "lure only fits the beam's own target");
            Assert.That(run.ResponseTo(HeroRole.MeleeDamage, MonsterPattern.SoulBolt, false), Is.EqualTo(PatternResponse.Dodge));
        }

        [Test]
        public void EveryMonsterKindCarriesItsPatterns()
        {
            var run = Arena(MonsterKind.Skeleton, MonsterKind.Ghost);
            var placed = run.Monsters.Where(m => !m.IsBoss).ToList();
            Assert.That(placed.Single(m => m.Kind == MonsterKind.Skeleton).Patterns, Is.Empty);
            Assert.That(placed.Single(m => m.Kind == MonsterKind.Ghost).Patterns, Is.EqualTo(new[] { MonsterPattern.SoulBolt }));
            Assert.That(run.Boss.Name, Is.EqualTo("무덤 기사"));
            Assert.That(run.Boss.Patterns, Is.EqualTo(new[] { MonsterPattern.Slam, MonsterPattern.TrackingBeam }));
            Assert.That(DungeonRun.Cost(MonsterKind.Ghost), Is.InRange(DungeonRun.Cost(MonsterKind.Skeleton), DungeonRun.Cost(MonsterKind.Zombie)));
        }

        [Test]
        public void AWholeRunWithUndeadUsesPatternsAndReplaysTheSame()
        {
            List<string> Play()
            {
                var run = new DungeonRun(seed: 5);
                var tiles = DungeonRunTests.PlaceableTiles(run);
                run.Place(tiles[tiles.Count / 3], MonsterKind.Ghost);
                run.Place(tiles[tiles.Count / 2], MonsterKind.Zombie);
                run.Place(tiles[tiles.Count / 2], MonsterKind.Skeleton);
                run.Start();
                DungeonRunTests.Drive(run, () => !DungeonRunTests.InDungeon(run) || run.Stage == 2);
                return run.Log.ToList();
            }
            var first = Play();
            Assert.That(first, Has.Some.Contains("발동 •"), "some pattern landed");
            Assert.That(first, Has.Some.Contains("회피"), "some hero dodged");
            Assert.That(Play(), Is.EqualTo(first));
        }

        [Test]
        public void ASoulBoltFliesStraightAndAHeroBesideTheLineIsSafe()
        {
            var run = Arena(MonsterKind.Ghost);
            var ghost = Foe(run);
            Put(ghost, 33, 4);
            Put(run, HeroRole.RangedDamage, 36, 4);
            var cast = run.ForceCast(ghost, MonsterPattern.SoulBolt, HeroRole.RangedDamage);
            Assert.That(cast.Phase, Is.EqualTo(CastPhase.Locked), "fixed when cast");
            Assert.That(cast.Area, Does.Contain(new TilePoint(36, 4)).And.Contain(new TilePoint(38, 4)));
            Assert.That(cast.Area, Does.Not.Contain(new TilePoint(36, 5)));
            Assert.That(cast.Area.Count, Is.EqualTo(5));
        }
    }
}
