using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    /// <summary>
    /// Every Core test runs on the numbers in the game's balance file, so a change there is checked by the same rules
    /// the game plays by.
    /// </summary>
    [SetUpFixture]
    public class BalanceFileSetup
    {
        [OneTimeSetUp]
        public void LoadTheGamesBalanceFile() => BalanceTable.Current = BalanceFile.Read();

        [OneTimeTearDown]
        public void RestoreTheBuiltInNumbers() => BalanceTable.Current = null;
    }

    internal static class BalanceFile
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>Assets/Game/Unity/Resources/Balance/balance.json, found from the test binaries up.</summary>
        internal static string Path
        {
            get
            {
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
                if (dir == null) throw new DirectoryNotFoundException("No Assets folder above the test binaries.");
                return System.IO.Path.Combine(dir.FullName, "Assets", "Game", "Unity", "Resources", "Balance", "balance.json");
            }
        }

        internal static BalanceTable Read() =>
            JsonSerializer.Deserialize<BalanceTable>(File.ReadAllText(Path), Options) ?? throw new InvalidDataException("Empty balance file.");
    }

    public class BalanceFileTests
    {
        [Test]
        public void TheBalanceFileIsPlayable()
        {
            var table = BalanceFile.Read();
            Assert.That(table.Problems(), Is.Empty);
        }

        /// <summary>A key left out of the file would silently fall back to the built-in number; list them all.</summary>
        [Test]
        public void TheBalanceFileSpellsOutEveryNumber()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(BalanceFile.Path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            void Check(Type type, JsonElement element, string where)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.That(element.TryGetProperty(field.Name, out var value), Is.True, $"{where}{field.Name} is missing");
                    if (field.FieldType == typeof(MonsterBalance) || field.FieldType == typeof(BossBalance))
                        Check(field.FieldType, value, where + field.Name + ".");
                }
                var known = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToHashSet();
                foreach (var property in element.EnumerateObject())
                    Assert.That(known.Contains(property.Name), Is.True, $"{where}{property.Name} is not a balance number (typo?)");
            }
            Check(typeof(BalanceTable), document.RootElement, "");
        }

        [TestCase(20, 0, 20)]
        [TestCase(20, 20, 10)]
        [TestCase(31, 11, 20)]
        [TestCase(3, 200, 1)]
        [TestCase(0, 5, 0)]
        public void ArmourCutsABlowByRatioAndNeverToZero(int attack, int armor, int expected)
        {
            var table = new BalanceTable { armorScale = 20 };
            Assert.That(table.Mitigate(attack, armor), Is.EqualTo(expected));
        }

        [Test]
        public void TheRunUsesTheLoadedTable()
        {
            var table = BalanceFile.Read();
            table.skeleton.hp = 7;
            table.budget = new[] { 9, 9, 9 };
            var before = BalanceTable.Current;
            try
            {
                BalanceTable.Current = table;
                var run = new DungeonRun();
                Assert.That(run.Budget, Is.EqualTo(9));
                Assert.That(DungeonRun.MonsterStats(MonsterKind.Skeleton).Hp, Is.EqualTo(7));
            }
            finally
            {
                BalanceTable.Current = before;
            }
        }
    }
}
