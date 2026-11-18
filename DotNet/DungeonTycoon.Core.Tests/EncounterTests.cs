using System.Linq;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class EncounterTests
    {
        [TestCase(0, false)]
        [TestCase(60, true)]
        public void MerchantDecidesOnce(int fatigue, bool buy)
        {
            var run = new DungeonRun();
            run.Start();
            var merchant = run.Encounters.Single(e => e.Kind == EncounterKind.Merchant);
            run.Party[1].Hp = 0;
            run.SetFatigueSteps(fatigue);
            run.VisitEncounter(merchant.Tile);
            Assert.That(merchant.Accepted, Is.EqualTo(buy));
            Assert.That(run.EncounterScene.Count(b => b.Speaker.HasValue), Is.EqualTo(3));
            var scene = run.EncounterScene;
            run.VisitEncounter(merchant.Tile);
            Assert.That(run.EncounterScene, Is.SameAs(scene));
            Assert.That(run.Coins, Is.EqualTo(buy ? 1 : 2));
            if (!buy) Assert.That(run.FatigueSteps, Is.EqualTo(fatigue));
        }

        [Test]
        public void StockIncludesGoodAndSpoiledBreadAndTea()
        {
            var changes = new System.Collections.Generic.HashSet<(int, int)>();
            for (int seed = 0; seed < 100 && changes.Count < 4; seed++)
            {
                var run = new DungeonRun(seed);
                run.Start();
                run.SetFatigueSteps(60);
                var tank = run.Party[(int)HeroRole.Tank];
                tank.Hp -= 10;
                int before = tank.Hp;
                run.VisitEncounter(run.Encounters[0].Tile);
                changes.Add((tank.Hp - before, run.FatigueSteps - 60));
                Assert.That(run.Coins, Is.EqualTo(1));
            }
            Assert.That(changes, Is.EquivalentTo(new[] { (0, -12), (0, 8), (8, 0), (-5, 0) }));
        }

        [Test]
        public void StatueCanBeBrokenOrSparedAndSameSeedReplays()
        {
            var outcomes = new System.Collections.Generic.HashSet<(int, int, int)>();
            for (int seed = 0; seed < 100; seed++)
            {
                var run = new DungeonRun(seed);
                var statue = run.Encounters.Single(e => e.Kind == EncounterKind.Statue);
                var replay = new DungeonRun(seed);
                Assert.That(replay.Encounters.Last().Tile, Is.EqualTo(statue.Tile));
                run.Start();
                replay.Start();
                if (seed % 2 == 0) run.Party[(int)HeroRole.Healer].Hp = 0;
                var before = run.Party.Select(h => (h.Attack, h.Armor, h.SpeedHundredths)).ToArray();
                run.VisitEncounter(statue.Tile);
                if (seed % 2 == 0) replay.Party[(int)HeroRole.Healer].Hp = 0;
                replay.VisitEncounter(statue.Tile);
                Assert.That(replay.EncounterScene.Select(b => b.Text), Is.EqualTo(run.EncounterScene.Select(b => b.Text)));
                var bonus = run.Party[0].StatueBonus;
                outcomes.Add((bonus.Attack, bonus.Armor, bonus.SpeedPercent));
                Assert.That(new[] { bonus.Attack, bonus.Armor, bonus.SpeedPercent }.Count(x => x != 0),
                    Is.EqualTo(statue.Accepted ? 1 : 0));
                for (int i = 0; i < before.Length; i++)
                {
                    var hero = run.Party[i];
                    Assert.That(hero.Attack, Is.EqualTo(before[i].Attack + bonus.Attack));
                    Assert.That(hero.Armor, Is.EqualTo(System.Math.Max(0, before[i].Armor + bonus.Armor)));
                    Assert.That(hero.SpeedHundredths, Is.EqualTo(before[i].SpeedHundredths * (100 + bonus.SpeedPercent) / 100));
                }
                run.VisitEncounter(statue.Tile);
                Assert.That(run.Coins, Is.EqualTo(statue.Accepted ? 3 : 2));
                if (outcomes.Count == 7) break;
            }
            Assert.That(outcomes, Is.EquivalentTo(new[]
                { (0, 0, 0), (-2, 0, 0), (2, 0, 0), (0, -2, 0), (0, 2, 0), (0, 0, -20), (0, 0, 20) }));
        }
    }
}
