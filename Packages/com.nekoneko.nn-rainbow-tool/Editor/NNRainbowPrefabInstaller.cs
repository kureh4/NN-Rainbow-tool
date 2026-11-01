using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    [InitializeOnLoad]
    internal static class NNRainbowPrefabInstaller
    {
        private const string SourcePrefabGuid = "0673d3a454cb9b24a9af583144573439";
        private const string DestinationRoot = "Assets/nekoneko";
        private const string DestinationFolder = DestinationRoot + "/NN - Rainbow tool";
        private const string DestinationPrefab = DestinationFolder + "/NN_Rainbow.prefab";

        static NNRainbowPrefabInstaller()
        {
            EditorApplication.delayCall += EnsurePrefabExists;
        }

        [MenuItem("Tools/NN - Rainbow/PrefabをAssetsに配置")]
        private static void PlacePrefabFromMenu()
        {
            EnsurePrefabExists(selectAfter: true);
        }

        private static void EnsurePrefabExists()
        {
            EnsurePrefabExists(selectAfter: false);
        }

        private static void EnsurePrefabExists(bool selectAfter)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DestinationPrefab) != null)
            {
                if (selectAfter)
                {
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(DestinationPrefab);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                }
                return;
            }

            var sourcePrefab = AssetDatabase.GUIDToAssetPath(SourcePrefabGuid);
            if (string.IsNullOrEmpty(sourcePrefab))
            {
                Debug.LogWarning("[NN - Rainbow] Package内のNN_Rainbow.prefabが見つかりません。");
                return;
            }

            EnsureFolder(DestinationRoot);
            EnsureFolder(DestinationFolder);

            if (!AssetDatabase.CopyAsset(sourcePrefab, DestinationPrefab))
            {
                Debug.LogWarning("[NN - Rainbow] NN_Rainbow.prefabをAssetsへ配置できませんでした。");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DestinationPrefab);
            Debug.Log("[NN - Rainbow] Prefabを配置しました: " + DestinationPrefab);

            if (selectAfter && prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var slash = path.LastIndexOf('/');
            if (slash <= 0) return;

            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
