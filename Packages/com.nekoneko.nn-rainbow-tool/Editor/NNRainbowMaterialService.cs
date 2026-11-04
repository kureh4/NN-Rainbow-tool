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
        private const int FurFlowFrames = 60;


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
                            if (TryCreateLilToonFurMaterialSet(
                                    context,
                                    renderer,
                                    original,
                                    out var materialSet,
                                    out var furError))
                            {
                                clone = materialSet.Base;

                                if (!target.FurMaterialSets.TryGetValue(renderer, out var bySlot))
                                {
                                    bySlot = new Dictionary<int, NNRainbowFurMaterialSet>();
                                    target.FurMaterialSets[renderer] = bySlot;
                                }
                                bySlot[slot] = materialSet;

                                Debug.Log("[NN - Rainbow] lilToon Fur material-swap set active: renderer=" +
                                          renderer.name + ", material=" + original.name +
                                          ", shader=" + clone.shader.name +
                                          ", flowFrames=" + materialSet.Flow.Length);
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

        private static bool TryCreateLilToonFurMaterialSet(
            nadena.dev.ndmf.BuildContext context,
            Renderer renderer,
            Material original,
            out NNRainbowFurMaterialSet set,
            out string error)
        {
            set = null;
            error = null;

            if (context == null || renderer == null || original == null)
            {
                error = "build context, renderer, or material was null";
                return false;
            }

            if (!NNRainbowShaderVariants.TryGetVariant(original, out var variant))
            {
                error = "material is not a supported lilToon variant";
                return false;
            }

            var shaderPath = NNRainbowShaderBootstrap.FurShaderPath;
            var shaderName = "Hidden/NNRainbow/Fur";

            switch (variant)
            {
                case NNRainbowVariant.Fur:
                    shaderPath = NNRainbowShaderBootstrap.FurShaderPath;
                    shaderName = "Hidden/NNRainbow/Fur";
                    break;
                case NNRainbowVariant.FurCutout:
                    shaderPath = NNRainbowShaderBootstrap.FurCutoutShaderPath;
                    shaderName = "Hidden/NNRainbow/FurCutout";
                    break;
                case NNRainbowVariant.FurTwoPass:
                    shaderPath = NNRainbowShaderBootstrap.FurTwoPassShaderPath;
                    shaderName = "Hidden/NNRainbow/FurTwoPass";
                    break;
                default:
                    error = "material is not a Fur variant";
                    return false;
            }

            if (!NNRainbowShaderBootstrap.EnsureShaderReady(
                    shaderPath,
                    shaderName,
                    out var shader,
                    out var shaderError))
            {
                error = shaderError ?? ("lilToon custom Fur shader was not available. path=" +
                                        shaderPath + ", name=" + shaderName);
                return false;
            }

            var bounds = renderer.localBounds;
            var diagonalRange = Mathf.Max(
                0.0001f,
                2f * (Mathf.Abs(bounds.extents.x) + Mathf.Abs(bounds.extents.y)));
            // Diagonal axis: upper-left -> lower-right.
            // The shader uses X + Y, so normalize that same axis here.
            var diagonalMin =
                bounds.center.x + bounds.center.y -
                Mathf.Abs(bounds.extents.x) - Mathf.Abs(bounds.extents.y);

            var scale = SpatialRainbowSpan / diagonalRange;
            var offset = -diagonalMin * scale;

            Material MakeVariant(string suffix, float mode, float phaseOffset, float glow)
            {
                var material = new Material(original)
                {
                    name = original.name + suffix
                };
                material.shader = shader;
                material.renderQueue = original.renderQueue;

                if (!material.HasProperty("_NNRMode") ||
                    !material.HasProperty("_NNRScale") ||
                    !material.HasProperty("_NNROffset") ||
                    !material.HasProperty("_NNRStrength") ||
                    !material.HasProperty("_NNRGlow"))
                {
                    UnityEngine.Object.DestroyImmediate(material);
                    return null;
                }

                SetFloatIf(material, "_NNRMode", mode);
                SetFloatIf(material, "_NNRSpeed", 0f);
                SetFloatIf(material, "_NNRScale", scale);
                SetFloatIf(material, "_NNROffset", offset + phaseOffset);
                SetFloatIf(material, "_NNRStrength", mode > 1.5f ? 1f : 0f);
                SetFloatIf(material, "_NNRGlow", glow);

                context.AssetSaver.SaveAsset(material);
                return material;
            }

            var baseMaterial = MakeVariant("__NNR_Base", 0f, 0f, 0f);
            var gradient = MakeVariant("__NNR_Gradient", 2f, 0f, 0f);
            var gradientGlow = MakeVariant("__NNR_GradientGlow", 2f, 0f, 1f);

            if (baseMaterial == null || gradient == null || gradientGlow == null)
            {
                error = "NN Rainbow Fur material variants could not see required custom shader properties. " +
                        "shader=" + shader.name +
                        ", shaderPropertyCheck=" + NNRainbowShaderBootstrap.DescribeRainbowProperties(shader);
                return false;
            }

            var flow = new Material[FurFlowFrames];
            var flowGlow = new Material[FurFlowFrames];

            for (var i = 0; i < FurFlowFrames; i++)
            {
                var phase = (float)i / FurFlowFrames;
                flow[i] = MakeVariant($"__NNR_Flow_{i:00}", 2f, phase, 0f);
                flowGlow[i] = MakeVariant($"__NNR_FlowGlow_{i:00}", 2f, phase, 1f);

                if (flow[i] == null || flowGlow[i] == null)
                {
                    error = "NN Rainbow Fur flow material variants could not be created.";
                    return false;
                }
            }

            set = new NNRainbowFurMaterialSet
            {
                Base = baseMaterial,
                Gradient = gradient,
                GradientGlow = gradientGlow,
                Flow = flow,
                FlowGlow = flowGlow
            };

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
