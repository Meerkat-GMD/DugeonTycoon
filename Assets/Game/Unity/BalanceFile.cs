using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    /// <summary>
    /// Loads Resources/Balance/balance.json into <see cref="BalanceTable.Current"/>. Called before every new expedition,
    /// so a change to the file (after Unity reimports it) applies from the next expedition on. A missing or broken file
    /// keeps the built-in numbers and says why in the console.
    /// </summary>
    public static class BalanceFile
    {
        public const string ResourcePath = "Balance/balance";

        public static void Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"밸런스 파일 Resources/{ResourcePath}.json이 없어 기본 수치를 씁니다.");
                BalanceTable.Current = null;
                return;
            }
            var table = new BalanceTable();
            try
            {
                JsonUtility.FromJsonOverwrite(asset.text, table);
            }
            catch (System.ArgumentException e)
            {
                Debug.LogError($"밸런스 파일을 읽지 못해 기본 수치를 씁니다: {e.Message}");
                BalanceTable.Current = null;
                return;
            }
            var problems = table.Problems();
            if (problems.Count > 0)
            {
                Debug.LogError("밸런스 파일에 문제가 있어 기본 수치를 씁니다:\n" + string.Join("\n", problems));
                BalanceTable.Current = null;
                return;
            }
            BalanceTable.Current = table;
        }
    }
}
