using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowMaterialService
    {
        private const string RainbowTextureGuid = "89070c92845f40b5917a66d82036f5ab";
        private const int FurBakeSize = 256;
        private const int FurFlowFrames = 12;
        private const int FurFramePadding = 1;
        private const int FurAtlasFrameHeight = FurBakeSize + FurFramePadding * 2;
        private const float SpatialRainbowSpan = 5f / 6f;

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
                            name = original.name + (isFur ? "__NNR_FUR_BAKED" : "__NNR_STOCK_EMISSION")
                        };

                        if (isFur)
                        {
                            if (TryPrepareBakedFur(context, target, renderer, slot, clone, original, out var bakeError))
                            {
                                Debug.Log("[NN - Rainbow] Baked Fur rainbow active: renderer=" + renderer.name +
                                          ", material=" + original.name +
                                          ", slot=" + slot);
                            }
                            else
                            {
                                Debug.LogWarning("[NN - Rainbow] Fur gradient/flow disabled for '" + original.name + "': " + bakeError);
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
                        Debug.LogWarning($"[NN - Rainbow] '{target.DisplayName}/{renderer.name}' already uses both Emission slots. Rainbow is skipped for non-Fur materials on this renderer to preserve the original material.");
                }
            }

            return true;
        }

        private static bool TryPrepareBakedFur(
            nadena.dev.ndmf.BuildContext context,
            NNRainbowTarget target,
            Renderer renderer,
            int slot,
            Material clone,
            Material original,
            out string error)
        {
            error = null;
            if (clone == null || original == null || renderer == null)
            {
                error = "material or renderer was null";
                return false;
            }

            var mainLayer = ChooseMainOverlayLayer(original);
            if (mainLayer == 0)
            {
                error = "Main 2nd and Main 3rd are both already in use";
                return false;
            }

            var mesh = GetSharedMesh(renderer);
            if (mesh == null)
            {
                error = "mesh not found";
                return false;
            }

            var rendererToTarget = target.Root != null
                ? target.Root.worldToLocalMatrix * renderer.transform.localToWorldMatrix
                : Matrix4x4.identity;

            if (!TryBakeFurAtlas(
                    context,
                    mesh,
                    slot,
                    rendererToTarget,
                    renderer.name,
                    original.name,
                    out var atlas,
                    out var frameCount,
                    out var vScale,
                    out var vOffsets,
                    out error))
                return false;

            PrepareMainOverlay(clone, mainLayer, atlas, vScale, vOffsets[0]);

            if (!target.FurBakedSlots.TryGetValue(renderer, out var bySlot))
            {
                bySlot = new Dictionary<int, NNRainbowFurBakeData>();
                target.FurBakedSlots[renderer] = bySlot;
            }

            bySlot[slot] = new NNRainbowFurBakeData
            {
                MainLayer = mainLayer,
                Atlas = atlas,
                FrameCount = frameCount,
                VScale = vScale,
                VOffsets = vOffsets
            };

            return true;
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

        private static void PrepareMainOverlay(Material material, int layer, Texture2D texture, float vScale, float vOffset)
        {
            if (material == null || (layer != 2 && layer != 3)) return;

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
                material.SetTexture(tex, texture);
                material.SetTextureScale(tex, new Vector2(1f, vScale));
                material.SetTextureOffset(tex, new Vector2(0f, vOffset));
            }

            if (material.HasProperty(blendMask))
                material.SetTexture(blendMask, Texture2D.whiteTexture);

            SetFloatIf(material, blendMode, 3f); // Multiply: original MainTex * rainbow
            SetFloatIf(material, alphaMode, 0f);
            SetFloatIf(material, enableLighting, 1f);
            SetFloatIf(material, uvMode, 0f);
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

        private static bool TryBakeFurAtlas(
            nadena.dev.ndmf.BuildContext context,
            Mesh mesh,
            int subMesh,
            Matrix4x4 rendererToTarget,
            string rendererName,
            string materialName,
            out Texture2D atlas,
            out int frameCount,
            out float vScale,
            out float[] vOffsets,
            out string error)
        {
            atlas = null;
            frameCount = FurFlowFrames;
            vScale = 0f;
            vOffsets = null;
            error = null;

            if (mesh == null)
            {
                error = "mesh was null";
                return false;
            }

            try
            {
                using (var meshDataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = meshDataArray[0];
                    if (subMesh < 0 || subMesh >= data.subMeshCount)
                    {
                        error = "material slot does not have a matching submesh";
                        return false;
                    }

                    var vertexCount = data.vertexCount;
                    if (vertexCount <= 0)
                    {
                        error = "mesh has no vertices";
                        return false;
                    }

                    var sub = data.GetSubMesh(subMesh);
                    if (sub.indexCount < 3)
                    {
                        error = "submesh has no triangles";
                        return false;
                    }

                    using (var vertices = new NativeArray<Vector3>(vertexCount, Allocator.Temp))
                    using (var uvs = new NativeArray<Vector2>(vertexCount, Allocator.Temp))
                    using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                    {
                        data.GetVertices(vertices);
                        data.GetUVs(0, uvs);
                        data.GetIndices(indices, subMesh, true);

                        var positions = new Vector3[vertexCount];
                        for (var i = 0; i < vertexCount; i++)
                            positions[i] = rendererToTarget.MultiplyPoint3x4(vertices[i]);

                        var hueMap = new float[FurBakeSize * FurBakeSize];
                        var coverage = new bool[hueMap.Length];

                        var minDiagonal = float.PositiveInfinity;
                        var maxDiagonal = float.NegativeInfinity;
                        for (var i = 0; i < indices.Length; i++)
                        {
                            var vi = indices[i];
                            if (vi < 0 || vi >= vertexCount) continue;
                            var d = positions[vi].x - positions[vi].y;
                            if (d < minDiagonal) minDiagonal = d;
                            if (d > maxDiagonal) maxDiagonal = d;
                        }

                        var range = Mathf.Max(0.0001f, maxDiagonal - minDiagonal);

                        for (var i = 0; i + 2 < indices.Length; i += 3)
                        {
                            var i0 = indices[i];
                            var i1 = indices[i + 1];
                            var i2 = indices[i + 2];
                            if ((uint)i0 >= vertexCount || (uint)i1 >= vertexCount || (uint)i2 >= vertexCount) continue;

                            RasterizeTriangle(
                                uvs[i0], uvs[i1], uvs[i2],
                                positions[i0], positions[i1], positions[i2],
                                minDiagonal, range,
                                hueMap, coverage);
                        }

                        if (!coverage.Any(x => x))
                        {
                            error = "no UV0 triangle pixels could be baked";
                            return false;
                        }

                        var atlasHeight = FurAtlasFrameHeight * FurFlowFrames;
                        var colors = new Color32[FurBakeSize * atlasHeight];
                        var transparentWhite = new Color32(255, 255, 255, 0);
                        for (var i = 0; i < colors.Length; i++) colors[i] = transparentWhite;

                        vScale = FurBakeSize / (float)atlasHeight;
                        vOffsets = new float[FurFlowFrames];

                        for (var frame = 0; frame < FurFlowFrames; frame++)
                        {
                            var phase = (float)frame / FurFlowFrames;
                            var frameBaseY = frame * FurAtlasFrameHeight;
                            var innerStartY = frameBaseY + FurFramePadding;
                            vOffsets[frame] = innerStartY / (float)atlasHeight;

                            for (var y = 0; y < FurBakeSize; y++)
                            {
                                for (var x = 0; x < FurBakeSize; x++)
                                {
                                    var sourceIndex = y * FurBakeSize + x;
                                    if (!coverage[sourceIndex]) continue;

                                    var hue = Mathf.Repeat(hueMap[sourceIndex] + phase, 1f);
                                    var rgb = Color.HSVToRGB(hue, 1f, 1f);
                                    var color = (Color32)new Color(rgb.r, rgb.g, rgb.b, 1f);

                                    var atlasY = innerStartY + y;
                                    colors[atlasY * FurBakeSize + x] = color;
                                }
                            }

                            // Duplicate the first/last inner rows into the padding to avoid bilinear bleeding
                            // between adjacent phase frames.
                            for (var x = 0; x < FurBakeSize; x++)
                            {
                                colors[frameBaseY * FurBakeSize + x] =
                                    colors[innerStartY * FurBakeSize + x];

                                colors[(innerStartY + FurBakeSize) * FurBakeSize + x] =
                                    colors[(innerStartY + FurBakeSize - 1) * FurBakeSize + x];
                            }
                        }

                        atlas = new Texture2D(FurBakeSize, atlasHeight, TextureFormat.RGBA32, false, false)
                        {
                            name = $"NNR_FurAtlas_{Sanitize(rendererName)}_{Sanitize(materialName)}_{subMesh}",
                            wrapMode = TextureWrapMode.Clamp,
                            filterMode = FilterMode.Bilinear,
                            anisoLevel = 1
                        };
                        atlas.SetPixels32(colors);
                        atlas.Apply(false, false);
                        context.AssetSaver.SaveAsset(atlas);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private static void RasterizeTriangle(
            Vector2 uv0,
            Vector2 uv1,
            Vector2 uv2,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            float minDiagonal,
            float diagonalRange,
            float[] hueMap,
            bool[] coverage)
        {
            // Unwrap the triangle around uv0 so islands crossing 0/1 are still rasterized correctly.
            uv1.x += Mathf.Round(uv0.x - uv1.x);
            uv2.x += Mathf.Round(uv0.x - uv2.x);
            uv1.y += Mathf.Round(uv0.y - uv1.y);
            uv2.y += Mathf.Round(uv0.y - uv2.y);

            for (var sx = -1; sx <= 1; sx++)
            for (var sy = -1; sy <= 1; sy++)
            {
                var a = uv0 + new Vector2(sx, sy);
                var b = uv1 + new Vector2(sx, sy);
                var c = uv2 + new Vector2(sx, sy);

                var minU = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                var maxU = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                var minV = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                var maxV = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                if (maxU < 0f || minU > 1f || maxV < 0f || minV > 1f) continue;

                var minX = Mathf.Clamp(Mathf.FloorToInt(minU * FurBakeSize), 0, FurBakeSize - 1);
                var maxX = Mathf.Clamp(Mathf.CeilToInt(maxU * FurBakeSize), 0, FurBakeSize - 1);
                var minY = Mathf.Clamp(Mathf.FloorToInt(minV * FurBakeSize), 0, FurBakeSize - 1);
                var maxY = Mathf.Clamp(Mathf.CeilToInt(maxV * FurBakeSize), 0, FurBakeSize - 1);

                var denom = Cross(b - a, c - a);
                if (Mathf.Abs(denom) < 1e-8f) continue;

                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var uv = new Vector2((x + 0.5f) / FurBakeSize, (y + 0.5f) / FurBakeSize);
                    var w1 = Cross(uv - a, c - a) / denom;
                    var w2 = Cross(b - a, uv - a) / denom;
                    var w0 = 1f - w1 - w2;

                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                    var position = p0 * w0 + p1 * w1 + p2 * w2;
                    var normalized = Mathf.Clamp01((position.x - position.y - minDiagonal) / diagonalRange);
                    var hue = normalized * SpatialRainbowSpan;

                    var index = y * FurBakeSize + x;
                    hueMap[index] = hue;
                    coverage[index] = true;
                }
            }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

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

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Unnamed";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Replace('/', '_').Replace('\\', '_');
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
