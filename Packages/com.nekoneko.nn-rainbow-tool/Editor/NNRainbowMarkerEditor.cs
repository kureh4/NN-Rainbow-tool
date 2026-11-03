using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    [CustomEditor(typeof(NNRainbowMarker))]
    internal sealed class NNRainbowMarkerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var marker = (NNRainbowMarker)target;
            EditorGUILayout.LabelField("NN - Rainbow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "このNN_Rainbowの親オブジェクトを1つのRainbow対象として自動取得します。\n" +
                "中に対象を入れる必要はありません。\n" +
                "lilToon Shaderを維持したまま、NDMFで非破壊に虹色設定を追加します。",
                MessageType.Info);

            var parent = marker.transform.parent;
            EditorGUILayout.LabelField("対象", parent != null ? parent.name : "なし");
            if (parent == null)
            {
                EditorGUILayout.HelpBox("レインボー化したいGameObjectの子へNN_Rainbowを移動してください。", MessageType.Error);
                return;
            }

            var avatarDescriptor = marker.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            var root = avatarDescriptor != null ? avatarDescriptor.gameObject : marker.transform.root.gameObject;
            var current = NNRainbowTargetScanner.Scan(root).FirstOrDefault(x => x.Root == parent);
            if (current == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("この親以下に対応したlilToon Rendererが見つかりません。", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("メニュー表示名", current.DisplayName);
            EditorGUILayout.LabelField("対応Renderer", current.Renderers.Count.ToString());
            EditorGUILayout.LabelField("対応Material", current.SupportedMaterials.Count.ToString());
            EditorGUILayout.LabelField("未対応Material Slot", current.UnsupportedMaterials.Count.ToString());
            EditorGUILayout.HelpBox(
                "対応: 通常lilToonのOpaque / Cutout / Transparent（Outline含む）\n" +
                "対応: lilToon Fur / FurCutout / FurTwoPass\n" +
                "未使用のEmission / Emission2ndを虹色レイヤーとして使用します。両方使用済みの場合はそのRendererを安全のためスキップします。",
                MessageType.None);
        }
    }
}
