using System;
using NUnit.Framework;

namespace DungeonTycoon.Core.Tests
{
    public class D20CheckTests
    {
        [TestCase(10, 0, 10, true)]
        [TestCase(9, 0, 10, false)]
        [TestCase(17, 0, 10, true)]
        [TestCase(1, 9, 10, true)]
        [TestCase(20, -11, 10, false)]
        [TestCase(20, int.MaxValue, int.MaxValue, true)]
        public void ComparesModifiedRollToDifficulty(int roll, int modifier, int difficulty, bool success)
        {
            Assert.That(D20Check.Succeeds(roll, modifier, difficulty), Is.EqualTo(success));
        }

        [TestCase(0)]
        [TestCase(21)]
        public void RejectsResultsOutsideTheDieRange(int roll)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => D20Check.Succeeds(roll, 0, 10));
        }
    }
}
