using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal sealed class NNRainbowTarget
    {
        public NNRainbowMarker Marker;
        public Transform Root;
        public string Id;
        public string DisplayName;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<Material> SupportedMaterials = new List<Material>();
        public readonly Dictionary<Renderer, List<int>> SupportedSlots = new Dictionary<Renderer, List<int>>();
        public readonly Dictionary<Renderer, List<int>> FakeShadowSlots = new Dictionary<Renderer, List<int>>();
        public readonly List<(GameObject go, Material material, string shader)> UnsupportedMaterials =
            new List<(GameObject, Material, string)>();
        public readonly Dictionary<Renderer, int> OverlayLayerByRenderer = new Dictionary<Renderer, int>();
        public readonly Dictionary<Renderer, int> EmissionLayerByRenderer = new Dictionary<Renderer, int>();
    }

    internal static class NNRainbowTargetScanner
    {
        public static List<NNRainbowTarget> Scan(GameObject avatarRoot)
        {
            var result = new List<NNRainbowTarget>();
            if (avatarRoot == null) return result;

            var registeredParents = new HashSet<Transform>();

            foreach (var marker in avatarRoot.GetComponentsInChildren<NNRainbowMarker>(true))
            {
                if (marker == null) continue;

                var targetRoot = marker.transform.parent;
                if (targetRoot == null) continue;
                if (!registeredParents.Add(targetRoot)) continue;

                var renderers = targetRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                    .ToArray();
                if (renderers.Length == 0) continue;

                var target = new NNRainbowTarget
                {
                    Marker = marker,
                    Root = targetRoot,
                    Id = NNRainbowStableId.For(avatarRoot.transform, marker, targetRoot),
                    DisplayName = targetRoot.name
                };

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    var materials = renderer.sharedMaterials ?? new Material[0];
                    var supportedSlots = new List<int>();
                    var fakeShadowSlots = new List<int>();

                    for (var slot = 0; slot < materials.Length; slot++)
                    {
                        var mat = materials[slot];
                        if (mat == null) continue;

                        var shaderName = mat.shader != null ? mat.shader.name : "<missing shader>";

                        if (IsLilToonFakeShadow(mat))
                            fakeShadowSlots.Add(slot);
                        else if (IsSupportedLilToon(mat))
                        {
                            supportedSlots.Add(slot);
                            if (!target.SupportedMaterials.Contains(mat)) target.SupportedMaterials.Add(mat);
                        }
                        else
                            target.UnsupportedMaterials.Add((renderer.gameObject, mat, shaderName));
                    }

                    if (supportedSlots.Count > 0 || fakeShadowSlots.Count > 0)
                    {
                        target.Renderers.Add(renderer);
                        if (supportedSlots.Count > 0)
                            target.SupportedSlots[renderer] = supportedSlots;
                        if (fakeShadowSlots.Count > 0)
                            target.FakeShadowSlots[renderer] = fakeShadowSlots;
                    }
                }

                if (target.Renderers.Count > 0) result.Add(target);
            }

            ApplyDisambiguatedDisplayNames(result);
            return result;
        }

        private static bool IsLilToonFakeShadow(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            var shaderName = mat.shader.name ?? string.Empty;
            return shaderName.IndexOf("lilToonFakeShadow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSupportedLilToon(Material mat)
        {
            return NNRainbowShaderVariants.TryGetVariant(mat, out _);
        }

        private static void ApplyDisambiguatedDisplayNames(List<NNRainbowTarget> targets)
        {
            foreach (var group in targets.GroupBy(x => x.DisplayName).Where(g => g.Count() > 1))
            {
                foreach (var t in group)
                {
                    var parent = t.Root != null ? t.Root.parent : null;
                    var suffix = parent != null ? parent.name : t.Id;
                    t.DisplayName = $"{t.DisplayName} ({suffix})";
                }
            }
        }
    }
}
