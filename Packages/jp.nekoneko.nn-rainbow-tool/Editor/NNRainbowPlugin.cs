using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Nekoneko.NNRainbow.Editor.NNRainbowPlugin))]

namespace Nekoneko.NNRainbow.Editor
{
    internal sealed class NNRainbowPlugin : Plugin<NNRainbowPlugin>
    {
        public override string QualifiedName => "jp.nekoneko.nn-rainbow";
        public override string DisplayName => "NN - Rainbow";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                {
                    seq.AfterPlugin("nadena.dev.modular-avatar")
                       .Run("NN - Rainbow (stock lilToon)", Build);
                });
        }

        protected override void OnUnhandledException(Exception e)
        {
            Debug.LogError("[NN - Rainbow] NDMF pass failed.");
            Debug.LogException(e);
        }

        private static void Build(BuildContext context)
        {
            var stage = "start";
            try
            {
                var root = context.AvatarRootObject;
                if (root == null) return;

                stage = "find markers";
                var markers = root.GetComponentsInChildren<NNRainbowMarker>(true)
                    .Where(x => x != null)
                    .ToArray();
                if (markers.Length == 0) return;

                stage = "avatar descriptor";
                var descriptor = root.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null)
                    throw new InvalidOperationException("VRC_AvatarDescriptor was not found on the avatar root.");

                foreach (var marker in markers)
                {
                    if (marker.transform.parent == null)
                        throw new InvalidOperationException(
                            $"Marker '{marker.name}' has no parent. Put NN_Rainbow directly under the GameObject you want to rainbow.");
                }

                stage = "scan targets";
                var targets = NNRainbowTargetScanner.Scan(root);
                if (targets.Count == 0)
                    throw new InvalidOperationException(
                        "No supported lilToon renderer was found below an NN_Rainbow parent object.");

                var unsupportedCount = targets.Sum(t => t.UnsupportedMaterials.Count);
                if (unsupportedCount > 0)
                {
                    var sample = targets
                        .SelectMany(t => t.UnsupportedMaterials.Select(u => $"{t.DisplayName}/{u.go.name}: {u.shader}"))
                        .Take(8);
                    Debug.LogWarning("[NN - Rainbow] Unsupported material slots were skipped:\n" + string.Join("\n", sample));
                }

                stage = "parameter budget";
                var existingBits = NNRainbowExpressionService.ExistingSyncedBits(descriptor.expressionParameters);
                var extraBits = targets.Count * NNRainbowExpressionService.BitsPerTarget;
                var useNetworkSync = existingBits + extraBits <= NNRainbowExpressionService.SyncedBudgetBits;
                if (!useNetworkSync)
                {
                    Debug.LogWarning(
                        $"[NN - Rainbow] Synced parameter budget would exceed {NNRainbowExpressionService.SyncedBudgetBits} bit. " +
                        "NN Rainbow parameters will be generated as local-only for this build.");
                }

                stage = "stock lilToon material clones";
                if (!NNRainbowMaterialService.ApplyBuildClones(context, targets, out var materialError))
                    throw new InvalidOperationException(materialError ?? "Failed to create stock lilToon Rainbow material clones.");

                stage = "expressions";
                NNRainbowExpressionService.Install(context, targets, useNetworkSync);

                stage = "FX animator";
                var animatorServices = context.Extension<AnimatorServicesContext>();
                if (animatorServices == null)
                    throw new InvalidOperationException("NDMF AnimatorServicesContext was not available.");
                NNRainbowAnimatorService.Install(context, animatorServices, root.transform, targets);

                stage = "cleanup markers";
                foreach (var marker in markers)
                {
                    if (marker != null) UnityEngine.Object.DestroyImmediate(marker.gameObject);
                }

                var overlayFallbacks = targets.Sum(t => t.OverlayLayerByRenderer.Values.Count(x => x == 0));
                var emissionFallbacks = targets.Sum(t => t.EmissionLayerByRenderer.Values.Count(x => x == 0));
                Debug.Log(
                    $"[NN - Rainbow] Build completed. targets={targets.Count}, " +
                    $"networkSync={(useNetworkSync ? "ON" : "LOCAL")}, " +
                    $"overlayUnavailableRenderers={overlayFallbacks}, emissionUnavailableRenderers={emissionFallbacks}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[NN - Rainbow] BUILD FAILED at stage '{stage}': {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
                throw;
            }
        }
    }
}
