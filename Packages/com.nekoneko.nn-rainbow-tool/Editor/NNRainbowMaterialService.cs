using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowMaterialService
    {
        private const string RainbowTextureGuid = "89070c92845f40b5917a66d82036f5ab";
        private const float SpatialRainbowSpan = 5f / 6f; // red -> magenta across the target diagonal

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
                    var furSlots = target.FurSlots.TryGetValue(renderer, out var fs) ? fs : null;

                    var furUvChannel = 0;
                    if (furSlots != null && furSlots.Count > 0)
                    {
                        if (!TryPrepareFurUvMesh(context, target, renderer, furSlots, out furUvChannel, out var furMeshError))
                        {
                            Debug.LogWarning("[NN - Rainbow] Fur gradient/flow disabled on renderer '" + renderer.name + "': " + furMeshError);
                            furUvChannel = 0;
                        }
                        else
                        {
                            Debug.Log("[NN - Rainbow] Fur UV rainbow active: renderer=" + renderer.name + ", UV" + furUvChannel);
                        }
                    }

                    var nonFurMaterials = slots
                        .Where(slot => slot >= 0 && slot < src.Length && src[slot] != null && (furSlots == null || !furSlots.Contains(slot)))
                        .Select(slot => src[slot])
                        .ToArray();

                    target.OverlayLayerByRenderer[renderer] = 0;
                    var emissionLayer = ChooseEmissionLayer(nonFurMaterials);
                    target.EmissionLayerByRenderer[renderer] = emissionLayer;

                    foreach (var slot in slots)
                    {
                        if (slot < 0 || slot >= src.Length) continue;
                        var original = src[slot];
                        if (original == null) continue;

                        var isFur = furSlots != null && furSlots.Contains(slot);
                        var clone = new Material(original)
                        {
                            name = original.name + (isFur ? "__NNR_FUR_UV" : "__NNR_STOCK_EMISSION")
                        };

                        if (isFur)
                        {
                            if (furUvChannel > 0)
                            {
                                var mainLayer = ChooseMainOverlayLayer(original);
                                if (mainLayer != 0)
                                {
                                    PrepareMainOverlay(clone, mainLayer, rainbowTexture, furUvChannel);

                                    if (!target.FurUvSlots.TryGetValue(renderer, out var bySlot))
                                    {
                                        bySlot = new Dictionary<int, NNRainbowFurUvData>();
                                        target.FurUvSlots[renderer] = bySlot;
                                    }

                                    bySlot[slot] = new NNRainbowFurUvData
                                    {
                                        MainLayer = mainLayer,
                                        UvChannel = furUvChannel
                                    };
                                }
                                else
                                {
                                    Debug.LogWarning("[NN - Rainbow] Fur gradient/flow disabled for material '" + original.name +
                                                     "': Main 2nd and Main 3rd are already in use.");
                                }
                            }
                        }
                        else
                        {
                            PrepareEmission(clone, emissionLayer, rainbowTexture);
                        }

                        dst[slot] = clone;
                    }

                    renderer.sharedMaterials = dst;

                    if (nonFurMaterials.Length > 0 && emissionLayer == 0)
                    {
                        Debug.LogWarning($"[NN - Rainbow] '{target.DisplayName}/{renderer.name}' already uses both Emission slots. " +
                                         "Rainbow is skipped for non-Fur materials on this renderer to preserve the original material.");
                    }
                }
            }

            return true;
        }

        private static bool TryPrepareFurUvMesh(
            nadena.dev.ndmf.BuildContext context,
            NNRainbowTarget target,
            Renderer renderer,
            IReadOnlyList<int> furSlots,
            out int uvChannel,
            out string error)
        {
            uvChannel = 0;
            error = null;

            var sourceMesh = GetSharedMesh(renderer);
            if (sourceMesh == null)
            {
                error = "mesh not found";
                return false;
            }

            uvChannel = FindFreeUvChannel(sourceMesh);
            if (uvChannel == 0)
            {
                error = "UV1, UV2 and UV3 are all already in use";
                return false;
            }

            var vertices = sourceMesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                error = "mesh has no vertices";
                return false;
            }

            var used = new bool[vertices.Length];
            foreach (var subMesh in furSlots)
            {
                if (subMesh < 0 || subMesh >= sourceMesh.subMeshCount) continue;
                var indices = sourceMesh.GetTriangles(subMesh, true);
                foreach (var index in indices)
                {
                    if ((uint)index < used.Length) used[index] = true;
                }
            }

            var rendererToTarget = target.Root != null
                ? target.Root.worldToLocalMatrix * renderer.transform.localToWorldMatrix
                : Matrix4x4.identity;

            var minDiagonal = float.PositiveInfinity;
            var maxDiagonal = float.NegativeInfinity;
            var usedCount = 0;

            for (var i = 0; i < vertices.Length; i++)
            {
                if (!used[i]) continue;
                var p = rendererToTarget.MultiplyPoint3x4(vertices[i]);
                var d = p.x - p.y;
                if (d < minDiagonal) minDiagonal = d;
                if (d > maxDiagonal) maxDiagonal = d;
                usedCount++;
            }

            if (usedCount == 0)
            {
                error = "Fur submesh has no vertices";
                return false;
            }

            var range = Mathf.Max(0.0001f, maxDiagonal - minDiagonal);
            var rainbowUv = new List<Vector2>(vertices.Length);

            for (var i = 0; i < vertices.Length; i++)
            {
                var p = rendererToTarget.MultiplyPoint3x4(vertices[i]);
                var normalized = Mathf.Clamp01((p.x - p.y - minDiagonal) / range);
                rainbowUv.Add(new Vector2(normalized * SpatialRainbowSpan, 0.5f));
            }

            var clone = UnityEngine.Object.Instantiate(sourceMesh);
            clone.name = sourceMesh.name + "__NNR_RainbowUV";
            clone.SetUVs(uvChannel, rainbowUv);
            context.AssetSaver.SaveAsset(clone);
            SetSharedMesh(renderer, clone);

            return true;
        }

        private static int FindFreeUvChannel(Mesh mesh)
        {
            if (mesh == null) return 0;
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord3)) return 3;
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord2)) return 2;
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) return 1;
            return 0;
        }

        private static int ChooseMainOverlayLayer(Material material)
        {
            if (material == null) return 0;

            if (material.HasProperty("_UseMain2ndTex") &&
                material.HasProperty("_Main2ndTex") &&
                material.GetFloat("_UseMain2ndTex") < 0.5f)
                return 2;

            if (material.HasProperty("_UseMain3rdTex") &&
                material.HasProperty("_Main3rdTex") &&
                material.GetFloat("_UseMain3rdTex") < 0.5f)
                return 3;

            return 0;
        }

        private static void PrepareMainOverlay(Material material, int layer, Texture2D rainbowTexture, int uvChannel)
        {
            if (material == null || rainbowTexture == null || (layer != 2 && layer != 3)) return;

            var suffix = layer == 2 ? "2nd" : "3rd";
            var use = "_UseMain" + suffix + "Tex";
            var color = "_Color" + suffix;
            var tex = "_Main" + suffix + "Tex";
            var blendMask = "_Main" + suffix + "BlendMask";
            var blendMode = "_Main" + suffix + "TexBlendMode";
            var alphaMode = "_Main" + suffix + "TexAlphaMode";
            var enableLighting = "_Main" + suffix + "EnableLighting";
            var uvMode = "_Main" + suffix + "Tex_UVMode";
            var angle = "_Main" + suffix + "TexAngle";
            var scroll = "_Main" + suffix + "Tex_ScrollRotate";
            var cull = "_Main" + suffix + "Tex_Cull";
            var distanceFade = "_Main" + suffix + "DistanceFade";

            SetFloatIf(material, use, 0f);
            SetColorIf(material, color, Color.white);

            if (material.HasProperty(tex))
            {
                material.SetTexture(tex, rainbowTexture);
                material.SetTextureScale(tex, Vector2.one);
                material.SetTextureOffset(tex, Vector2.zero);
            }

            if (material.HasProperty(blendMask))
                material.SetTexture(blendMask, Texture2D.whiteTexture);

            SetFloatIf(material, blendMode, 3f); // Normal, Add, Screen, Multiply => 3
            SetFloatIf(material, alphaMode, 0f);
            SetFloatIf(material, enableLighting, 1f);
            SetFloatIf(material, uvMode, uvChannel);
            SetFloatIf(material, angle, 0f);
            SetVectorIf(material, scroll, Vector4.zero);
            SetFloatIf(material, cull, 0f);
            SetVectorIf(material, distanceFade, new Vector4(0.1f, 0.01f, 0f, 0f));

            SetFloatIf(material, "_Main" + suffix + "TexIsDecal", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexIsLeftOnly", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexIsRightOnly", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexShouldCopy", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexShouldFlipMirror", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexShouldFlipCopy", 0f);
            SetFloatIf(material, "_Main" + suffix + "TexIsMSDF", 0f);
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = meshRenderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            return null;
        }

        private static void SetSharedMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.sharedMesh = mesh;
                return;
            }

            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = mesh;
            }
        }

        private static int ChooseEmissionLayer(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return 0;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission"))) return 1;
            if (materials.All(m => IsUnusedToggle(m, "_UseEmission2nd"))) return 2;
            return 0;
        }

        private static bool IsUnusedToggle(Material material, string property) =>
            material != null && material.HasProperty(property) && material.GetFloat(property) < 0.5f;

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

        private static void SetFloatIf(Material m, string property, float value)
        {
            if (m.HasProperty(property)) m.SetFloat(property, value);
        }

        private static void SetColorIf(Material m, string property, Color value)
        {
            if (m.HasProperty(property)) m.SetColor(property, value);
        }

        private static void SetVectorIf(Material m, string property, Vector4 value)
        {
            if (m.HasProperty(property)) m.SetVector(property, value);
        }
    }
}
