using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowAnimatorService
    {
        private const float MaxScrollSpeed = 0.80f;
        private const float BaseRainbowBlend = 0.32f;
        private const float GlowRainbowBlend = 0.90f;
        private const float DiagonalGradientTiling = 0.70710678f; // 1 / sqrt(2)
        private const float DiagonalGradientAngle = 0.78539816f; // 45 degrees

        public static void Install(BuildContext context, AnimatorServicesContext animatorServices, Transform avatarRoot, IReadOnlyList<NNRainbowTarget> targets)
        {
            var cc = animatorServices.ControllerContext;
            if (!cc.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx))
            {
                fx = VirtualAnimatorController.Create(cc.CloneContext, "NNR FX");
                cc.Controllers[VRCAvatarDescriptor.AnimLayerType.FX] = fx;
            }

            var totalBindings = 0;
            foreach (var target in targets)
            {
                var enable = NNRainbowExpressionService.Param(target, "Enable");
                var type = NNRainbowExpressionService.Param(target, "Type");
                var speed = NNRainbowExpressionService.Param(target, "Speed");
                var speedEdit = NNRainbowExpressionService.Param(target, "SpeedEdit");
                var speedEditOpen = NNRainbowExpressionService.Param(target, "SpeedEditOpen");
                var emission = NNRainbowExpressionService.Param(target, "Emission");

                AddParameter(fx, enable, AnimatorControllerParameterType.Bool, 0f);
                AddParameter(fx, type, AnimatorControllerParameterType.Int, 1f);
                AddParameter(fx, speed, AnimatorControllerParameterType.Float, 0.5f);
                AddParameter(fx, speedEdit, AnimatorControllerParameterType.Float, 0.5f);
                AddParameter(fx, speedEditOpen, AnimatorControllerParameterType.Bool, 0f);
                AddParameter(fx, emission, AnimatorControllerParameterType.Bool, 0f);

                AddSpeedCommitLayer(fx, target, speed, speedEdit, speedEditOpen);
                totalBindings += AddRainbowModeLayer(fx, animatorServices, avatarRoot, target, enable, type, speed);
                totalBindings += AddStrengthLayer(fx, animatorServices, avatarRoot, target, enable, emission);
                totalBindings += AddFurColorLayer(fx, animatorServices, avatarRoot, target, enable, type, speed);
                totalBindings += AddFakeShadowLayer(fx, animatorServices, avatarRoot, target, enable);
            }

            Debug.Log($"[NN - Rainbow] Emission-route FX installed. targets={targets.Count}, bindings={totalBindings}");
        }

        private static void AddParameter(VirtualAnimatorController fx, string name, AnimatorControllerParameterType type, float defaultValue)
        {
            fx.Parameters = fx.Parameters.SetItem(name, new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = defaultValue,
                defaultInt = Mathf.RoundToInt(defaultValue),
                defaultBool = defaultValue > 0.5f
            });
        }

        private static void AddSpeedCommitLayer(
            VirtualAnimatorController fx,
            NNRainbowTarget target,
            string appliedSpeedParameter,
            string editSpeedParameter,
            string editOpenParameter)
        {
            var layer = fx.AddLayer(LayerPriority.Default, $"NNR/{target.Id}/Speed Commit");
            layer.DefaultWeight = 1f;
            var sm = layer.StateMachine;

            var idle = sm.AddState("Idle");
            var editing = sm.AddState("Editing");
            var commit = sm.AddState("Commit");
            idle.WriteDefaultValues = false;
            editing.WriteDefaultValues = false;
            commit.WriteDefaultValues = false;
            sm.DefaultState = idle;

            AddCopyDriver(commit, editSpeedParameter, appliedSpeedParameter, localOnly: true);

            AddTransition(idle, editing, CondIf(editOpenParameter));
            AddTransition(editing, commit, CondIfNot(editOpenParameter));
            AddTransition(commit, idle);
        }

        private static void AddCopyDriver(VirtualState state, string source, string destination, bool localOnly)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.name = $"NNR Copy {source} -> {destination}";
            driver.localOnly = localOnly;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
            {
                new VRC_AvatarParameterDriver.Parameter
                {
                    name = destination,
                    source = source,
                    type = VRC_AvatarParameterDriver.ChangeType.Copy
                }
            };
            state.Behaviours = state.Behaviours.Add(driver);
        }

        private static int AddRainbowModeLayer(VirtualAnimatorController fx, AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, string enableParameter, string typeParameter, string speedParameter)
        {
            var off = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.Off, 0f, out var c0);
            var global0 = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.GlobalHue, 0f, out var c10);
            var global1 = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.GlobalHue, 1f, out var c11);
            var gradient = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.Gradient, 0f, out var c2);
            var flow0 = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.Flowing, 0f, out var c30);
            var flow1 = MakeModeClip(animatorServices, avatarRoot, target, RainbowMode.Flowing, 1f, out var c31);
            var maxBindings = Math.Max(Math.Max(Math.Max(c0, c10), Math.Max(c11, c2)), Math.Max(c30, c31));
            if (maxBindings == 0) return 0;

            var globalTree = MakeSpeedTree(target, "Global Hue", speedParameter, global0, global1);
            var flowTree = MakeSpeedTree(target, "Flowing Rainbow", speedParameter, flow0, flow1);

            var layer = fx.AddLayer(LayerPriority.Default, $"NNR/{target.Id}/Rainbow Mode");
            layer.DefaultWeight = 1f;
            var sm = layer.StateMachine;
            var sOff = sm.AddState("Rainbow OFF", off);
            var s1 = sm.AddState("全体色相", globalTree);
            var s2 = sm.AddState("グラデーション", gradient);
            var s3 = sm.AddState("流れる虹", flowTree);
            sOff.WriteDefaultValues = false;
            s1.WriteDefaultValues = false;
            s2.WriteDefaultValues = false;
            s3.WriteDefaultValues = false;
            sm.DefaultState = sOff;

            AddTransition(sOff, s1, CondIf(enableParameter), CondEquals(typeParameter, 1f));
            AddTransition(sOff, s2, CondIf(enableParameter), CondEquals(typeParameter, 2f));
            AddTransition(sOff, s3, CondIf(enableParameter), CondEquals(typeParameter, 3f));
            AddTransition(s1, sOff, CondIfNot(enableParameter));
            AddTransition(s2, sOff, CondIfNot(enableParameter));
            AddTransition(s3, sOff, CondIfNot(enableParameter));
            AddTransition(s1, s2, CondEquals(typeParameter, 2f));
            AddTransition(s1, s3, CondEquals(typeParameter, 3f));
            AddTransition(s2, s1, CondEquals(typeParameter, 1f));
            AddTransition(s2, s3, CondEquals(typeParameter, 3f));
            AddTransition(s3, s1, CondEquals(typeParameter, 1f));
            AddTransition(s3, s2, CondEquals(typeParameter, 2f));
            return maxBindings;
        }

        private static VirtualBlendTree MakeSpeedTree(NNRainbowTarget target, string label, string speedParameter, VirtualClip zero, VirtualClip max)
        {
            var tree = VirtualBlendTree.Create($"NNR/{target.Id}/{label} Speed");
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = speedParameter;
            tree.UseAutomaticThresholds = false;
            tree.MinThreshold = 0f;
            tree.MaxThreshold = 1f;
            tree.Children = ImmutableList.Create(
                new VirtualBlendTree.VirtualChildMotion { Motion = zero, Threshold = 0f, TimeScale = 1f },
                new VirtualBlendTree.VirtualChildMotion { Motion = max, Threshold = 1f, TimeScale = 1f });
            return tree;
        }

        private static int AddStrengthLayer(VirtualAnimatorController fx, AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, string enableParameter, string glowParameter)
        {
            var off = MakeBlendClip(animatorServices, avatarRoot, target, 0f, out var c0);
            var baseRainbow = MakeBlendClip(animatorServices, avatarRoot, target, BaseRainbowBlend, out var c1);
            var glow = MakeBlendClip(animatorServices, avatarRoot, target, GlowRainbowBlend, out var c2);
            var maxBindings = Math.Max(c0, Math.Max(c1, c2));
            if (maxBindings == 0) return 0;

            var layer = fx.AddLayer(LayerPriority.Default, $"NNR/{target.Id}/Rainbow Strength");
            layer.DefaultWeight = 1f;
            var sm = layer.StateMachine;
            var sOff = sm.AddState("Rainbow OFF", off);
            var sBase = sm.AddState("Rainbow", baseRainbow);
            var sGlow = sm.AddState("Rainbow + 発光", glow);
            sOff.WriteDefaultValues = false;
            sBase.WriteDefaultValues = false;
            sGlow.WriteDefaultValues = false;
            sm.DefaultState = sOff;

            AddTransition(sOff, sBase, CondIf(enableParameter), CondIfNot(glowParameter));
            AddTransition(sOff, sGlow, CondIf(enableParameter), CondIf(glowParameter));
            AddTransition(sBase, sOff, CondIfNot(enableParameter));
            AddTransition(sBase, sGlow, CondIf(glowParameter));
            AddTransition(sGlow, sOff, CondIfNot(enableParameter));
            AddTransition(sGlow, sBase, CondIfNot(glowParameter));
            return maxBindings;
        }

        private static int AddFurColorLayer(
            VirtualAnimatorController fx,
            AnimatorServicesContext animatorServices,
            Transform avatarRoot,
            NNRainbowTarget target,
            string enableParameter,
            string typeParameter,
            string speedParameter)
        {
            if (target.FurSlots.Count == 0) return 0;

            // Type=1 は従来どおり Fur を残した全体色相。
            // Type=2 / Type=3 も Fur は消さず、元の _MainTex / _FurNoiseMask / Fur設定をそのまま使う。
            // 斜め虹そのものは spatial Fur shader 側で最終色へ合成する。
            var original = MakeFurColorOffClip(animatorServices, avatarRoot, target, out var c0);
            var rainbow = MakeFurRainbowClip(animatorServices, avatarRoot, target, out var c1);
            var maxBindings = Math.Max(c0, c1);
            if (maxBindings == 0) return 0;

            var layer = fx.AddLayer(LayerPriority.Default, $"NNR/{target.Id}/Fur Color");
            layer.DefaultWeight = 1f;
            var sm = layer.StateMachine;

            var sOriginal = sm.AddState("Fur Original", original);
            var sRainbow = sm.AddState("Fur 全体色相", rainbow);
            sOriginal.WriteDefaultValues = false;
            sRainbow.WriteDefaultValues = false;
            sRainbow.Speed = 1f;
            sRainbow.SpeedParameter = speedParameter;
            sm.DefaultState = sOriginal;

            AddTransition(sOriginal, sRainbow, CondIf(enableParameter), CondEquals(typeParameter, 1f));

            AddTransition(sRainbow, sOriginal, CondIfNot(enableParameter));
            AddTransition(sRainbow, sOriginal, CondEquals(typeParameter, 2f));
            AddTransition(sRainbow, sOriginal, CondEquals(typeParameter, 3f));

            return maxBindings;
        }

        private static VirtualClip MakeFurColorOffClip(
            AnimatorServicesContext animatorServices,
            Transform avatarRoot,
            NNRainbowTarget target,
            out int bindingCount)
        {
            var native = new AnimationClip
            {
                name = $"NNR_{target.Id}_Fur_Original",
                frameRate = 60f
            };
            bindingCount = 0;

            foreach (var pair in target.FurSlots)
            {
                var renderer = pair.Key;
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var path = ResolvePath(animatorServices, avatarRoot, renderer);

                foreach (var slot in pair.Value)
                {
                    if (slot < 0 || slot >= materials.Length) continue;
                    var mat = materials[slot];
                    if (mat == null || !mat.HasProperty("_Color")) continue;

                    var color = mat.GetColor("_Color");
                    AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.r"), color.r);
                    AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.g"), color.g);
                    AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.b"), color.b);
                    AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.a"), color.a);
                    bindingCount += 4;

                    // グラデーションで触る Fur Rim も、OFF時には必ず元へ戻す。
                    if (mat.HasProperty("_FurRimColor"))
                    {
                        var rim = mat.GetColor("_FurRimColor");
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.r"), rim.r);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.g"), rim.g);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.b"), rim.b);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.a"), rim.a);
                        bindingCount += 4;
                    }
                    if (mat.HasProperty("_FurRimFresnelPower"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimFresnelPower"), mat.GetFloat("_FurRimFresnelPower"));
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurRimAntiLight"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimAntiLight"), mat.GetFloat("_FurRimAntiLight"));
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurVector"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurVector.w"), mat.GetVector("_FurVector").w);
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurLayerNum"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurLayerNum"), mat.GetFloat("_FurLayerNum"));
                        bindingCount++;
                    }
                }
            }

            var result = VirtualClip.Clone(animatorServices.ControllerContext.CloneContext, native);
            UnityEngine.Object.DestroyImmediate(native);
            return result;
        }

        private static VirtualClip MakeFurRainbowClip(
            AnimatorServicesContext animatorServices,
            Transform avatarRoot,
            NNRainbowTarget target,
            out int bindingCount)
        {
            var native = new AnimationClip
            {
                name = $"NNR_{target.Id}_Fur_Rainbow",
                frameRate = 60f
            };

            var settings = AnimationUtility.GetAnimationClipSettings(native);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(native, settings);

            bindingCount = 0;

            foreach (var pair in target.FurSlots)
            {
                var renderer = pair.Key;
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var path = ResolvePath(animatorServices, avatarRoot, renderer);

                foreach (var slot in pair.Value)
                {
                    if (slot < 0 || slot >= materials.Length) continue;
                    var mat = materials[slot];
                    if (mat == null || !mat.HasProperty("_Color")) continue;

                    var original = mat.GetColor("_Color");
                    var brightness = Mathf.Clamp(Mathf.Max(original.r, Mathf.Max(original.g, original.b)), 0.35f, 1f);

                    AddCurve(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.r"), MakeRainbowCurve(brightness, 0));
                    AddCurve(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.g"), MakeRainbowCurve(brightness, 1));
                    AddCurve(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.b"), MakeRainbowCurve(brightness, 2));
                    AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_Color.a"), original.a);
                    bindingCount += 4;

                    // グラデーションから戻った時に Rim 色を残さない。
                    if (mat.HasProperty("_FurRimColor"))
                    {
                        var rim = mat.GetColor("_FurRimColor");
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.r"), rim.r);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.g"), rim.g);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.b"), rim.b);
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimColor.a"), rim.a);
                        bindingCount += 4;
                    }
                    if (mat.HasProperty("_FurRimFresnelPower"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimFresnelPower"), mat.GetFloat("_FurRimFresnelPower"));
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurRimAntiLight"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurRimAntiLight"), mat.GetFloat("_FurRimAntiLight"));
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurVector"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurVector.w"), mat.GetVector("_FurVector").w);
                        bindingCount++;
                    }
                    if (mat.HasProperty("_FurLayerNum"))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_FurLayerNum"), mat.GetFloat("_FurLayerNum"));
                        bindingCount++;
                    }
                }
            }

            var result = VirtualClip.Clone(animatorServices.ControllerContext.CloneContext, native);
            UnityEngine.Object.DestroyImmediate(native);
            return result;
        }

        private static AnimationCurve MakeRainbowCurve(float brightness, int channel)
        {
            var curve = new AnimationCurve();
            for (var i = 0; i <= 6; i++)
            {
                var t = i / 6f;
                var c = Color.HSVToRGB(t, 1f, brightness);
                var value = channel == 0 ? c.r : channel == 1 ? c.g : c.b;
                curve.AddKey(new Keyframe(t, value));
            }

            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }

            curve.preWrapMode = WrapMode.Loop;
            curve.postWrapMode = WrapMode.Loop;
            return curve;
        }

        private static void AddCurve(AnimationClip clip, string path, Type type, string propertyName, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                new EditorCurveBinding { path = path, type = type, propertyName = propertyName },
                curve);
        }

        private static int AddFakeShadowLayer(VirtualAnimatorController fx, AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, string enableParameter)
        {
            if (target.FakeShadowSlots.Count == 0) return 0;

            var visible = MakeFakeShadowClip(animatorServices, avatarRoot, target, false, out var c0);
            var hidden = MakeFakeShadowClip(animatorServices, avatarRoot, target, true, out var c1);
            var maxBindings = Math.Max(c0, c1);
            if (maxBindings == 0) return 0;

            var layer = fx.AddLayer(LayerPriority.Default, $"NNR/{target.Id}/FakeShadow");
            layer.DefaultWeight = 1f;
            var sm = layer.StateMachine;
            var sVisible = sm.AddState("FakeShadow ON", visible);
            var sHidden = sm.AddState("FakeShadow Hidden", hidden);
            sVisible.WriteDefaultValues = false;
            sHidden.WriteDefaultValues = false;
            sm.DefaultState = sVisible;

            AddTransition(sVisible, sHidden, CondIf(enableParameter));
            AddTransition(sHidden, sVisible, CondIfNot(enableParameter));
            return maxBindings;
        }

        private static VirtualClip MakeFakeShadowClip(AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, bool invisible, out int bindingCount)
        {
            var native = new AnimationClip
            {
                name = $"NNR_{target.Id}_FakeShadow_{(invisible ? "Hidden" : "Visible")}",
                frameRate = 60f
            };
            bindingCount = 0;

            foreach (var pair in target.FakeShadowSlots)
            {
                var renderer = pair.Key;
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var path = ResolvePath(animatorServices, avatarRoot, renderer);

                foreach (var slot in pair.Value)
                {
                    if (slot < 0 || slot >= materials.Length) continue;
                    var mat = materials[slot];
                    if (mat == null || !mat.HasProperty("_Invisible")) continue;

                    var property = materials.Length <= 1
                        ? "material._Invisible"
                        : $"material.[{slot}]._Invisible";

                    AddConstant(native, path, typeof(Renderer), property, invisible ? 1f : 0f);
                    bindingCount++;
                }
            }

            var result = VirtualClip.Clone(animatorServices.ControllerContext.CloneContext, native);
            UnityEngine.Object.DestroyImmediate(native);
            return result;
        }

        private static VirtualClip MakeModeClip(AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, RainbowMode mode, float speed01, out int bindingCount)
        {
            var native = new AnimationClip { name = $"NNR_{target.Id}_{mode}_{speed01:0.##}", frameRate = 60f };
            bindingCount = 0;

            foreach (var renderer in target.Renderers)
            {
                if (renderer == null) continue;
                if (!target.SupportedSlots.TryGetValue(renderer, out var slots) || slots.Count == 0) continue;

                var emissionLayer = target.EmissionLayerByRenderer.TryGetValue(renderer, out var el) ? el : 0;
                var hasEmissionLayer = emissionLayer != 0;

                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var path = ResolvePath(animatorServices, avatarRoot, renderer);
                var suffix = emissionLayer == 1 ? "" : "2nd";
                var isGlobal = mode == RainbowMode.GlobalHue;
                var isGradient = mode == RainbowMode.Gradient;
                var isFlow = mode == RainbowMode.Flowing;
                var isDiagonal = isGradient || isFlow;

                // Stock lilToon fallback for normal materials.
                var tiling = isGlobal ? 0.001f : isDiagonal ? DiagonalGradientTiling : 1f;
                var scrollX = (isGlobal || isFlow) ? Mathf.Clamp01(speed01) * MaxScrollSpeed : 0f;
                var rotation = isDiagonal ? DiagonalGradientAngle : 0f;

                // Spatial Fur shader ignores UV layout and uses object-space X-Y instead.
                var spatialMode = isGradient ? 2f : isFlow ? 3f : 0f;
                var spatialScroll = isFlow ? Mathf.Clamp01(speed01) * MaxScrollSpeed : 0f;

                foreach (var slot in slots)
                {
                    if (slot < 0 || slot >= materials.Length || materials[slot] == null) continue;

                    if (hasEmissionLayer)
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ST.x"), tiling); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ST.y"), tiling); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ST.z"), 0f); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ST.w"), 0f); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ScrollRotate.x"), scrollX); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ScrollRotate.y"), 0f); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ScrollRotate.z"), rotation); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Map_ScrollRotate.w"), 0f); bindingCount++;
                    }

                    if (IsSpatialFurSlot(target, renderer, slot))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_NNRMode"), spatialMode); bindingCount++;
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_NNRScroll"), spatialScroll); bindingCount++;
                    }
                }
            }

            var result = VirtualClip.Clone(animatorServices.ControllerContext.CloneContext, native);
            UnityEngine.Object.DestroyImmediate(native);
            return result;
        }

        private static VirtualClip MakeBlendClip(AnimatorServicesContext animatorServices, Transform avatarRoot, NNRainbowTarget target, float blend, out int bindingCount)
        {
            var native = new AnimationClip { name = $"NNR_{target.Id}_Blend_{blend:0.##}", frameRate = 60f };
            bindingCount = 0;
            foreach (var renderer in target.Renderers)
            {
                if (renderer == null) continue;
                if (!target.SupportedSlots.TryGetValue(renderer, out var slots) || slots.Count == 0) continue;

                var emissionLayer = target.EmissionLayerByRenderer.TryGetValue(renderer, out var el) ? el : 0;
                var hasEmissionLayer = emissionLayer != 0;

                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                var path = ResolvePath(animatorServices, avatarRoot, renderer);
                var suffix = emissionLayer == 1 ? "" : "2nd";

                foreach (var slot in slots)
                {
                    if (slot < 0 || slot >= materials.Length || materials[slot] == null) continue;

                    if (hasEmissionLayer)
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, $"_Emission{suffix}Blend"), blend);
                        bindingCount++;
                    }

                    if (IsSpatialFurSlot(target, renderer, slot))
                    {
                        AddConstant(native, path, typeof(Renderer), MaterialProperty(materials.Length, slot, "_NNRBlend"), blend);
                        bindingCount++;
                    }
                }
            }
            var result = VirtualClip.Clone(animatorServices.ControllerContext.CloneContext, native);
            UnityEngine.Object.DestroyImmediate(native);
            return result;
        }

        private static bool IsSpatialFurSlot(NNRainbowTarget target, Renderer renderer, int slot)
        {
            return target != null &&
                   renderer != null &&
                   target.FurSpatialSlots.TryGetValue(renderer, out var slots) &&
                   slots.Contains(slot);
        }

        private static string MaterialProperty(int materialCount, int slot, string property)
        {
            return materialCount <= 1
                ? $"material.{property}"
                : $"material.[{slot}].{property}";
        }

        private static string ResolvePath(AnimatorServicesContext animatorServices, Transform avatarRoot, Renderer renderer)
        {
            var path = animatorServices.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject);
            if (string.IsNullOrEmpty(path) && renderer.transform != avatarRoot)
                path = AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot);
            return path;
        }

        private static void AddConstant(AnimationClip clip, string path, Type type, string propertyName, float value)
        {
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding { path = path, type = type, propertyName = propertyName }, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        private static AnimatorCondition CondIf(string parameter) => new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = parameter, threshold = 0f };
        private static AnimatorCondition CondIfNot(string parameter) => new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = parameter, threshold = 0f };
        private static AnimatorCondition CondEquals(string parameter, float value) => new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = parameter, threshold = value };

        private static void AddTransition(VirtualState from, VirtualState to, params AnimatorCondition[] conditions)
        {
            var tr = VirtualStateTransition.Create();
            tr.SetDestination(to);
            tr.Duration = 0f;
            tr.HasFixedDuration = true;
            tr.ExitTime = null;
            tr.Conditions = ImmutableList.CreateRange(conditions);
            from.Transitions = from.Transitions.Add(tr);
        }

        private enum RainbowMode { Off, GlobalHue, Gradient, Flowing }
    }
}
