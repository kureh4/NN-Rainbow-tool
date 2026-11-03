using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowMaterialService
    {
        private const string RainbowTextureGuid = "89070c92845f40b5917a66d82036f5ab";

        public static bool ApplyBuildClones(nadena.dev.ndmf.BuildContext context, IReadOnlyList<NNRainbowTarget> targets, out string error)
        {
            error = null;
            var rainbowTexturePath = AssetDatabase.GUIDToAssetPath(RainbowTextureGuid);
            var rainbowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(rainbowTexturePath);
            if (rainbowTexture == null)
            {
                error = "NN Rainbow gradient texture could not be loaded (GUID: " + RainbowTextureGuid + ", path: " + rainbowTexturePath + ")";
                return false;
            }

            foreach (var target in targets)
            {
                foreach (var renderer in target.Renderers)
                {
                    if (renderer == null) continue;
                    if (!target.SupportedSlots.TryGetValue(renderer, out var slots) || slots.Count == 0) continue;

                    var src = renderer.sharedMaterials ?? Array.Empty<Material>();
                    var dst = (Material[])src.Clone();
                    var sourceMaterials = slots.Where(slot => slot >= 0 && slot < src.Length && src[slot] != null).Select(slot => src[slot]).ToArray();
                    target.OverlayLayerByRenderer[renderer] = 0;
                    var emissionLayer = ChooseEmissionLayer(sourceMaterials);
                    target.EmissionLayerByRenderer[renderer] = emissionLayer;

                    var clones = new Dictionary<Material, Material>();
                    var customFurByOriginal = new Dictionary<Material, bool>();
                    foreach (var slot in slots)
                    {
                        if (slot < 0 || slot >= src.Length) continue;
                        var original = src[slot];
                        if (original == null) continue;
                        if (!clones.TryGetValue(original, out var clone))
                        {
                            clone = new Material(original) { name = original.name + "__NNR_STOCK_EMISSION" };
                            var customFur = TryApplyCustomFurShader(clone, original, rainbowTexture);
                            PrepareEmission(clone, emissionLayer, rainbowTexture);
                            clones.Add(original, clone);
                            customFurByOriginal[original] = customFur;
                        }
                        dst[slot] = clone;

                        if (customFurByOriginal.TryGetValue(original, out var isCustomFur) && isCustomFur)
                        {
                            if (!target.FurCustomSlots.TryGetValue(renderer, out var customSlots))
                            {
                                customSlots = new List<int>();
                                target.FurCustomSlots[renderer] = customSlots;
                            }
                            if (!customSlots.Contains(slot)) customSlots.Add(slot);
                        }
                    }
                    renderer.sharedMaterials = dst;

                    if (emissionLayer == 0)
                        Debug.LogWarning($"[NN - Rainbow] '{target.DisplayName}/{renderer.name}' already uses both Emission slots. Rainbow is skipped for this renderer to preserve the original material.");
                }
            }
            return true;
        }

        private static bool TryApplyCustomFurShader(Material clone, Material original, Texture2D rainbowTexture)
        {
            if (clone == null || original == null) return false;
            if (!NNRainbowShaderVariants.TryGetVariant(original, out var variant)) return false;

            string shaderName = null;
            switch (variant)
            {
                case NNRainbowVariant.Fur:
                    shaderName = "Hidden/NNRainbow/Fur";
                    break;
                case NNRainbowVariant.FurCutout:
                    shaderName = "Hidden/NNRainbow/FurCutout";
                    break;
                case NNRainbowVariant.FurTwoPass:
                    shaderName = "Hidden/NNRainbow/FurTwoPass";
                    break;
            }

            if (string.IsNullOrEmpty(shaderName)) return false;

            var shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[NN - Rainbow] Fur custom shader unavailable; using hue fallback: " + shaderName);
                return false;
            }

            var queue = clone.renderQueue;
            clone.shader = shader;
            clone.renderQueue = queue;

            if (clone.HasProperty("_NNRRainbowTex")) clone.SetTexture("_NNRRainbowTex", rainbowTexture);
            if (clone.HasProperty("_NNRMode")) clone.SetFloat("_NNRMode", 0f);
            if (clone.HasProperty("_NNRScroll")) clone.SetFloat("_NNRScroll", 0f);
            if (clone.HasProperty("_NNRBlend")) clone.SetFloat("_NNRBlend", 0f);
            return true;
        }

        private static int ChooseEmissionLayer(Material[] materials)
        {
            if (materials.Length == 0) return 0;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission"))) return 1;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission2nd"))) return 2;
            return 0;
        }

        private static bool IsUnusedToggle(Material material, string property) => material != null && material.HasProperty(property) && material.GetFloat(property) < 0.5f;

        private static void PrepareEmission(Material m, int layer, Texture2D rainbow)
        {
            if (m == null || layer == 0) return;
            var suffix = layer == 1 ? "" : "2nd";
            var use = "_UseEmission" + suffix;
            var color = "_Emission" + suffix + "Color";
            var tex = "_Emission" + suffix + "Map";
            var blend = "_Emission" + suffix + "Blend";
            var blendMode = "_Emission" + suffix + "BlendMode";
            var mainStrength = "_Emission" + suffix + "MainStrength";
            var uvMode = "_Emission" + suffix + "Map_UVMode";
            var blink = "_Emission" + suffix + "Blink";
            var scroll = "_Emission" + suffix + "Map_ScrollRotate";

            SetFloatIf(m, use, 1f);
            SetColorIf(m, color, new Color(2.0f, 2.0f, 2.0f, 1f));
            if (m.HasProperty(tex))
            {
                m.SetTexture(tex, rainbow);
                m.SetTextureScale(tex, Vector2.one);
                m.SetTextureOffset(tex, Vector2.zero);
            }
            SetFloatIf(m, blend, 0f);
            SetFloatIf(m, blendMode, 1f);
            SetFloatIf(m, mainStrength, 0f);
            SetFloatIf(m, uvMode, 0f);
            SetVectorIf(m, blink, Vector4.zero);
            SetVectorIf(m, scroll, Vector4.zero);
        }

        private static void SetFloatIf(Material m, string property, float value) { if (m.HasProperty(property)) m.SetFloat(property, value); }
        private static void SetColorIf(Material m, string property, Color value) { if (m.HasProperty(property)) m.SetColor(property, value); }
        private static void SetVectorIf(Material m, string property, Vector4 value) { if (m.HasProperty(property)) m.SetVector(property, value); }
    }
}
