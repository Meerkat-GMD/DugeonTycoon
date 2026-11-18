using System;

namespace DungeonTycoon.Core
{
    public static class D20Check
    {
        public static bool Succeeds(int roll, int modifier, int difficulty)
        {
            if (roll < 1 || roll > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(roll), roll, "A d20 roll must be between 1 and 20.");
            }

            return (long)roll + modifier >= difficulty;
        }
    }
}
