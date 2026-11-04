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
        private const string SpatialFurShaderGuid = "30fd2be66625487a859efbf5d32646e1";
        private const string SpatialFurCutoutShaderGuid = "f38d2b4e658641f8bf21133f967c1cc9";
        private const string SpatialFurTwoPassShaderGuid = "185ce65981d44dbf8a386a3ae172e058";
        private const float SpatialRainbowSpan = 5f / 6f; // red -> purple across the visible diagonal

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
                    var spatialByOriginal = new Dictionary<Material, bool>();
                    foreach (var slot in slots)
                    {
                        if (slot < 0 || slot >= src.Length) continue;
                        var original = src[slot];
                        if (original == null) continue;
                        if (!clones.TryGetValue(original, out var clone))
                        {
                            clone = new Material(original) { name = original.name + "__NNR_STOCK_EMISSION" };
                            PrepareEmission(clone, emissionLayer, rainbowTexture);

                            var spatialFur = TryApplySpatialFurShader(clone, original, renderer, emissionLayer, rainbowTexture);
                            clones.Add(original, clone);
                            spatialByOriginal[original] = spatialFur;
                        }
                        dst[slot] = clone;

                        if (spatialByOriginal.TryGetValue(original, out var isSpatialFur) && isSpatialFur)
                        {
                            if (!target.FurSpatialSlots.TryGetValue(renderer, out var spatialSlots))
                            {
                                spatialSlots = new List<int>();
                                target.FurSpatialSlots[renderer] = spatialSlots;
                            }
                            if (!spatialSlots.Contains(slot)) spatialSlots.Add(slot);
                        }
                    }
                    renderer.sharedMaterials = dst;

                    if (emissionLayer == 0)
                        Debug.LogWarning($"[NN - Rainbow] '{target.DisplayName}/{renderer.name}' already uses both Emission slots. Rainbow is skipped for this renderer to preserve the original material.");
                }
            }
            return true;
        }

        private static bool TryApplySpatialFurShader(Material clone, Material original, Renderer renderer, int emissionLayer, Texture2D rainbowTexture)
        {
            if (clone == null || original == null || renderer == null) return false;
            if (!NNRainbowShaderVariants.TryGetVariant(original, out var variant)) return false;
            if (variant != NNRainbowVariant.Fur &&
                variant != NNRainbowVariant.FurCutout &&
                variant != NNRainbowVariant.FurTwoPass) return false;

            string shaderGuid;
            string fallbackShaderName;
            switch (variant)
            {
                case NNRainbowVariant.Fur:
                    shaderGuid = SpatialFurShaderGuid;
                    fallbackShaderName = "Hidden/NNRainbowSpatial/Fur";
                    break;
                case NNRainbowVariant.FurCutout:
                    shaderGuid = SpatialFurCutoutShaderGuid;
                    fallbackShaderName = "Hidden/NNRainbowSpatial/FurCutout";
                    break;
                case NNRainbowVariant.FurTwoPass:
                    shaderGuid = SpatialFurTwoPassShaderGuid;
                    fallbackShaderName = "Hidden/NNRainbowSpatial/FurTwoPass";
                    break;
                default:
                    return false;
            }

            var shaderPath = AssetDatabase.GUIDToAssetPath(shaderGuid);
            var shader = !string.IsNullOrEmpty(shaderPath)
                ? AssetDatabase.LoadAllAssetsAtPath(shaderPath).OfType<Shader>().FirstOrDefault()
                : null;

            // Name lookup is only a fallback; the package asset GUID is the primary route.
            if (shader == null) shader = Shader.Find(fallbackShaderName);

            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[NN - Rainbow] Spatial Fur shader unavailable; keeping stable fallback. guid=" + shaderGuid + ", path=" + shaderPath + ", name=" + fallbackShaderName);
                return false;
            }

            var queue = clone.renderQueue;
            clone.shader = shader;
            clone.renderQueue = queue;

            if (clone.HasProperty("_NNRRainbowTex")) clone.SetTexture("_NNRRainbowTex", rainbowTexture);
            if (clone.HasProperty("_NNRMode")) clone.SetFloat("_NNRMode", 0f);
            if (clone.HasProperty("_NNRScroll")) clone.SetFloat("_NNRScroll", 0f);
            if (clone.HasProperty("_NNRBlend")) clone.SetFloat("_NNRBlend", 0f);

            var bounds = renderer.localBounds;
            var diagonalRange = Mathf.Max(0.001f, 2f * (Mathf.Abs(bounds.extents.x) + Mathf.Abs(bounds.extents.y)));
            var diagonalMin = bounds.center.x - bounds.center.y - bounds.extents.x - bounds.extents.y;
            var spatialScale = SpatialRainbowSpan / diagonalRange;
            var spatialOffset = -diagonalMin * spatialScale;

            if (clone.HasProperty("_NNRSpatialScale")) clone.SetFloat("_NNRSpatialScale", spatialScale);
            if (clone.HasProperty("_NNRSpatialOffset")) clone.SetFloat("_NNRSpatialOffset", spatialOffset);

            // This custom Fur path draws the spatial rainbow itself.
            // Disable only the unused emission slot NN Rainbow prepared, so UV-based emission cannot wash it into one color.
            if (emissionLayer == 1) SetFloatIf(clone, "_UseEmission", 0f);
            if (emissionLayer == 2) SetFloatIf(clone, "_UseEmission2nd", 0f);

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
