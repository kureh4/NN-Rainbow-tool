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
        private const float SpatialRainbowSpan = 5f / 6f;

        private const string FurShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur.lilcontainer";
        private const string FurCutoutShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur_cutout.lilcontainer";
        private const string FurTwoPassShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur_two.lilcontainer";

        public static bool ApplyBuildClones(
            nadena.dev.ndmf.BuildContext context,
            IReadOnlyList<NNRainbowTarget> targets,
            out string error)
        {
            error = null;

            var rainbowTexturePath = AssetDatabase.GUIDToAssetPath(RainbowTextureGuid);
            var rainbowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(rainbowTexturePath);
            if (rainbowTexture == null)
            {
                error = "NN Rainbow gradient texture could not be loaded (GUID: " +
                        RainbowTextureGuid + ", path: " + rainbowTexturePath + ")";
                return false;
            }

            foreach (var target in targets)
            {
                foreach (var renderer in target.Renderers)
                {
                    if (renderer == null) continue;
                    if (!target.SupportedSlots.TryGetValue(renderer, out var slots) || slots.Count == 0)
                        continue;

                    var sourceMaterials = renderer.sharedMaterials ?? Array.Empty<Material>();
                    var resultMaterials = (Material[])sourceMaterials.Clone();
                    var furSlots = target.FurSlots.TryGetValue(renderer, out var fs) ? fs : null;

                    var nonFurMaterials = slots
                        .Where(slot =>
                            slot >= 0 &&
                            slot < sourceMaterials.Length &&
                            sourceMaterials[slot] != null &&
                            (furSlots == null || !furSlots.Contains(slot)))
                        .Select(slot => sourceMaterials[slot])
                        .ToArray();

                    target.OverlayLayerByRenderer[renderer] = 0;
                    var emissionLayer = ChooseEmissionLayer(nonFurMaterials);
                    target.EmissionLayerByRenderer[renderer] = emissionLayer;

                    foreach (var slot in slots)
                    {
                        if (slot < 0 || slot >= sourceMaterials.Length) continue;

                        var original = sourceMaterials[slot];
                        if (original == null) continue;

                        var isFur = furSlots != null && furSlots.Contains(slot);
                        Material clone;

                        if (isFur)
                        {
                            if (TryCreateLilToonFurClone(renderer, original, out clone, out var furError))
                            {
                                if (!target.FurCustomSlots.TryGetValue(renderer, out var customSlots))
                                {
                                    customSlots = new List<int>();
                                    target.FurCustomSlots[renderer] = customSlots;
                                }
                                if (!customSlots.Contains(slot)) customSlots.Add(slot);

                                Debug.Log("[NN - Rainbow] lilToon Fur extension active: renderer=" +
                                          renderer.name + ", material=" + original.name +
                                          ", shader=" + clone.shader.name);
                            }
                            else
                            {
                                clone = new Material(original)
                                {
                                    name = original.name + "__NNR_FUR_FALLBACK"
                                };

                                Debug.LogError("[NN - Rainbow] Fur gradient / flowing rainbow disabled for '" +
                                               original.name + "': " + furError +
                                               "  全体色相は従来処理で使用できます。");
                            }
                        }
                        else
                        {
                            clone = new Material(original)
                            {
                                name = original.name + "__NNR_STOCK_EMISSION"
                            };
                            PrepareEmission(clone, emissionLayer, rainbowTexture);
                        }

                        resultMaterials[slot] = clone;
                    }

                    renderer.sharedMaterials = resultMaterials;

                    if (nonFurMaterials.Length > 0 && emissionLayer == 0)
                    {
                        Debug.LogWarning($"[NN - Rainbow] '{target.DisplayName}/{renderer.name}' already uses both Emission slots. " +
                                         "Rainbow is skipped for non-Fur materials on this renderer to preserve the original material.");
                    }
                }
            }

            return true;
        }

        private static bool TryCreateLilToonFurClone(
            Renderer renderer,
            Material original,
            out Material clone,
            out string error)
        {
            clone = null;
            error = null;

            if (renderer == null || original == null)
            {
                error = "renderer or material was null";
                return false;
            }

            if (!NNRainbowShaderVariants.TryGetVariant(original, out var variant))
            {
                error = "material is not a supported lilToon variant";
                return false;
            }

            var shaderPath = FurShaderPath;
            var shaderName = "Hidden/NNRainbow/Fur";

            switch (variant)
            {
                case NNRainbowVariant.Fur:
                    shaderPath = FurShaderPath;
                    shaderName = "Hidden/NNRainbow/Fur";
                    break;
                case NNRainbowVariant.FurCutout:
                    shaderPath = FurCutoutShaderPath;
                    shaderName = "Hidden/NNRainbow/FurCutout";
                    break;
                case NNRainbowVariant.FurTwoPass:
                    shaderPath = FurTwoPassShaderPath;
                    shaderName = "Hidden/NNRainbow/FurTwoPass";
                    break;
                default:
                    error = "material is not a Fur variant";
                    return false;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
                shader = Shader.Find(shaderName);

            if (shader == null || !shader.isSupported)
            {
                error = "lilToon custom Fur shader was not available. path=" +
                        shaderPath + ", name=" + shaderName;
                return false;
            }

            clone = new Material(shader)
            {
                name = original.name + "__NNR_LILTOON_FUR"
            };
            clone.CopyPropertiesFromMaterial(original);
            clone.renderQueue = original.renderQueue;

            if (!clone.HasProperty("_NNRMode") ||
                !clone.HasProperty("_NNRSpeed") ||
                !clone.HasProperty("_NNRScale") ||
                !clone.HasProperty("_NNROffset") ||
                !clone.HasProperty("_NNRStrength") ||
                !clone.HasProperty("_NNRGlow"))
            {
                error = "custom lilToon Fur shader loaded, but NN Rainbow properties were not generated. shader=" +
                        shader.name + ", path=" + shaderPath;
                UnityEngine.Object.DestroyImmediate(clone);
                clone = null;
                return false;
            }

            var bounds = renderer.localBounds;
            var diagonalRange = Mathf.Max(
                0.0001f,
                2f * (Mathf.Abs(bounds.extents.x) + Mathf.Abs(bounds.extents.y)));
            var diagonalMin =
                bounds.center.x - bounds.center.y -
                Mathf.Abs(bounds.extents.x) - Mathf.Abs(bounds.extents.y);

            var scale = SpatialRainbowSpan / diagonalRange;
            var offset = -diagonalMin * scale;

            SetFloatIf(clone, "_NNRMode", 0f);
            SetFloatIf(clone, "_NNRSpeed", 0f);
            SetFloatIf(clone, "_NNRScale", scale);
            SetFloatIf(clone, "_NNROffset", offset);
            SetFloatIf(clone, "_NNRStrength", 1f);
            SetFloatIf(clone, "_NNRGlow", 0f);

            return true;
        }

        private static int ChooseEmissionLayer(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return 0;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission"))) return 1;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission2nd"))) return 2;
            return 0;
        }

        private static bool IsUnusedToggle(Material material, string property) =>
            material != null &&
            material.HasProperty(property) &&
            material.GetFloat(property) < 0.5f;

        private static void PrepareEmission(Material material, int layer, Texture2D rainbow)
        {
            if (material == null || layer == 0) return;

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

            SetFloatIf(material, use, 1f);
            SetColorIf(material, color, new Color(2f, 2f, 2f, 1f));

            if (material.HasProperty(tex))
            {
                material.SetTexture(tex, rainbow);
                material.SetTextureScale(tex, Vector2.one);
                material.SetTextureOffset(tex, Vector2.zero);
            }

            SetFloatIf(material, blend, 0f);
            SetFloatIf(material, blendMode, 1f);
            SetFloatIf(material, mainStrength, 0f);
            SetFloatIf(material, uvMode, 0f);
            SetVectorIf(material, blink, Vector4.zero);
            SetVectorIf(material, scroll, Vector4.zero);
        }

        private static void SetFloatIf(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void SetColorIf(Material material, string property, Color value)
        {
            if (material != null && material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void SetVectorIf(Material material, string property, Vector4 value)
        {
            if (material != null && material.HasProperty(property))
                material.SetVector(property, value);
        }
    }
}
