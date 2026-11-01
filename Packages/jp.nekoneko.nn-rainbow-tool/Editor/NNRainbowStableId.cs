using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowStableId
    {
        public static string For(Transform avatarRoot, NNRainbowMarker marker, Transform target)
        {
            var raw = RelativePath(avatarRoot, target)
                      + "|"
                      + target.GetSiblingIndex()
                      + "|"
                      + RelativePath(avatarRoot, marker.transform)
                      + "|"
                      + marker.transform.GetSiblingIndex();

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(12);
                for (var i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static string RelativePath(Transform root, Transform t)
        {
            if (root == null || t == null) return string.Empty;
            if (root == t) return string.Empty;

            var p = t.name;
            var cur = t.parent;
            while (cur != null && cur != root)
            {
                p = cur.name + "/" + p;
                cur = cur.parent;
            }
            return p;
        }
    }
}
