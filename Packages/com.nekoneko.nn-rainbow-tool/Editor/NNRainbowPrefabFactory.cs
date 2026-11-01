using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowPrefabFactory
    {
        [MenuItem("GameObject/NN - Rainbow/Create Marker", false, 10)]
        private static void CreateMarker()
        {
            var go = new GameObject("NN_Rainbow");
            go.AddComponent<NNRainbowMarker>();

            if (Selection.activeTransform != null)
                go.transform.SetParent(Selection.activeTransform, false);

            Undo.RegisterCreatedObjectUndo(go, "Create NN_Rainbow");
            Selection.activeGameObject = go;
        }
    }
}
