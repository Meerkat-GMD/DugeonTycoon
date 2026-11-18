using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DungeonTycoon.Core;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class CoreContractTests
    {
        [Test]
        public void PartyRolesIncludeTankHealerMeleeAndRangedDamage()
        {
            Assert.That(Enum.GetValues<HeroRole>(), Is.EquivalentTo(new[]
            {
                HeroRole.Tank, HeroRole.Healer, HeroRole.MeleeDamage, HeroRole.RangedDamage
            }));
        }

        [TestCase("고블린", "고블린이")]
        [TestCase("오우거", "오우거가")]
        [TestCase("마왕 벨자크", "마왕 벨자크가")]
        [TestCase("고블린, 오우거", "고블린, 오우거가")]
        public void SubjectParticleFollowsTheLastSyllable(string word, string expected)
        {
            Assert.That(HeroTraits.Subject(word), Is.EqualTo(expected));
        }

        [Test]
        public void CoreAssemblyStaysIndependentOfTheUnityEngine()
        {
            var assembly = typeof(HeroRole).Assembly;
            var definitionPath = Path.Combine(AppContext.BaseDirectory, assembly.GetName().Name + ".asmdef");
            using var definition = JsonDocument.Parse(File.ReadAllText(definitionPath));

            Assert.That(definition.RootElement.GetProperty("noEngineReferences").GetBoolean(), Is.True);
            var unityMetaPath = Path.Combine(AppContext.BaseDirectory, "DungeonTycoon.Unity.asmdef.meta");
            var unityGuid = File.ReadLines(unityMetaPath)
                .Single(line => line.StartsWith("guid:", StringComparison.Ordinal))
                .Substring("guid:".Length).Trim();
            var declaredReferences = definition.RootElement.GetProperty("references").EnumerateArray()
                .Select(reference => reference.GetString()).ToArray();
            Assert.That(declaredReferences, Does.Not.Contain("GUID:" + unityGuid).IgnoreCase);
            Assert.That(declaredReferences.Where(IsUnityAssembly), Is.Empty);
            var unityReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(IsUnityAssembly);
            Assert.That(unityReferences, Is.Empty);
        }

        private static bool IsUnityAssembly(string name)
        {
            return name == "DungeonTycoon.Unity"
                || name == "UniTask"
                || name.StartsWith("UnityEngine", StringComparison.Ordinal)
                || name.StartsWith("UnityEditor", StringComparison.Ordinal);
        }
    }
}
