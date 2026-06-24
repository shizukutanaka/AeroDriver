using System;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// ドライバーバージョン文字列のユーティリティ
    /// </summary>
    public static class VersionHelper
    {
        /// <summary>
        /// 2つのバージョン文字列を比較します。
        /// 返り値: 正 = version1 が新しい, 0 = 同じ, 負 = version1 が古い
        /// </summary>
        public static int Compare(string version1, string version2)
        {
            if (string.IsNullOrEmpty(version1) && string.IsNullOrEmpty(version2)) return 0;
            if (string.IsNullOrEmpty(version1)) return -1;
            if (string.IsNullOrEmpty(version2)) return 1;

            string[] v1Parts = version1.Split('.', ',');
            string[] v2Parts = version2.Split('.', ',');
            int maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

            for (int i = 0; i < maxLength; i++)
            {
                int v1 = i < v1Parts.Length && int.TryParse(v1Parts[i], out int t1) ? t1 : 0;
                int v2 = i < v2Parts.Length && int.TryParse(v2Parts[i], out int t2) ? t2 : 0;
                if (v1 != v2) return v1.CompareTo(v2);
            }

            return 0;
        }

        /// <summary>
        /// version1 が version2 より新しいかどうかを返します
        /// </summary>
        public static bool IsNewer(string version1, string version2) => Compare(version1, version2) > 0;
    }
}
