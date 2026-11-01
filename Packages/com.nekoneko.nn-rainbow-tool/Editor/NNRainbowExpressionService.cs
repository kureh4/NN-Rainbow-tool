using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Nekoneko.NNRainbow.Editor
{
    internal static class NNRainbowExpressionService
    {
        public const int SyncedBudgetBits = 256;
        public const int BitsPerTarget = 18;

        public static void Install(
            nadena.dev.ndmf.BuildContext context,
            IReadOnlyList<NNRainbowTarget> targets,
            bool networkSynced)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (descriptor == null) throw new InvalidOperationException("VRC_AvatarDescriptor not found.");

            var originalParameters = descriptor.expressionParameters;
            var originalMenu = descriptor.expressionsMenu;

            var buildParameters = CloneParameters(originalParameters);
            AddParameters(buildParameters, targets, networkSynced);

            var nnRoot = BuildRainbowMenu(targets);
            var buildRoot = BuildRootMenu(originalMenu, nnRoot);

            descriptor.customExpressions = true;
            descriptor.expressionParameters = buildParameters;
            descriptor.expressionsMenu = buildRoot;
        }

        public static int ExistingSyncedBits(VRCExpressionParameters p)
        {
            if (p == null || p.parameters == null) return 0;
            var bits = 0;
            foreach (var x in p.parameters)
            {
                if (x == null || !x.networkSynced) continue;
                bits += x.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
            }
            return bits;
        }

        private static VRCExpressionParameters CloneParameters(VRCExpressionParameters source)
        {
            var clone = source != null
                ? UnityEngine.Object.Instantiate(source)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();

            clone.name = source != null ? source.name + "__NNR_BUILD" : "NNR_Parameters_BUILD";
            if (clone.parameters == null)
                clone.parameters = Array.Empty<VRCExpressionParameters.Parameter>();
            return clone;
        }

        private static VRCExpressionsMenu BuildRootMenu(VRCExpressionsMenu original, VRCExpressionsMenu nnRoot)
        {
            if (original == null)
            {
                var fresh = MakeMenu("Expressions");
                fresh.controls.Add(SubMenuControl("NN - Rainbow", nnRoot));
                return fresh;
            }

            var existingControls = original.controls ?? new List<VRCExpressionsMenu.Control>();
            if (existingControls.Count < 8)
            {
                var rootClone = UnityEngine.Object.Instantiate(original);
                rootClone.name = original.name + "__NNR_BUILD";
                rootClone.controls = existingControls
                    .Where(c => c != null)
                    .Select(CloneControl)
                    .ToList();
                rootClone.controls.Add(SubMenuControl("NN - Rainbow", nnRoot));
                return rootClone;
            }

            var wrapper = MakeMenu("Expressions");
            wrapper.controls.Add(SubMenuControl("Original", original));
            wrapper.controls.Add(SubMenuControl("NN - Rainbow", nnRoot));
            return wrapper;
        }

        private static VRCExpressionsMenu BuildRainbowMenu(IReadOnlyList<NNRainbowTarget> targets)
        {
            var nnRoot = MakeMenu("NN - Rainbow");
            foreach (var target in targets)
            {
                var targetMenu = MakeTargetMenu(target);
                AddSubmenuPaged(nnRoot, target.DisplayName, targetMenu);
            }
            return nnRoot;
        }

        private static VRCExpressionsMenu.Control CloneControl(VRCExpressionsMenu.Control c)
        {
            if (c == null) return null;
            return new VRCExpressionsMenu.Control
            {
                name = c.name,
                icon = c.icon,
                type = c.type,
                parameter = c.parameter == null ? null : new VRCExpressionsMenu.Control.Parameter { name = c.parameter.name },
                value = c.value,
                subMenu = c.subMenu,
                subParameters = c.subParameters == null
                    ? Array.Empty<VRCExpressionsMenu.Control.Parameter>()
                    : c.subParameters.Select(p => p == null ? null : new VRCExpressionsMenu.Control.Parameter { name = p.name }).ToArray(),
                labels = c.labels
            };
        }

        private static void AddParameters(
            VRCExpressionParameters asset,
            IReadOnlyList<NNRainbowTarget> targets,
            bool networkSynced)
        {
            var list = asset.parameters?.Where(x => x != null).ToList()
                       ?? new List<VRCExpressionParameters.Parameter>();
            var names = new HashSet<string>(list.Select(x => x.name));

            foreach (var t in targets)
            {
                Add(Param(t, "Enable"), VRCExpressionParameters.ValueType.Bool, 0f);
                Add(Param(t, "Type"), VRCExpressionParameters.ValueType.Int, 1f);
                Add(Param(t, "Speed"), VRCExpressionParameters.ValueType.Float, 0.5f, saved: true, synced: networkSynced);
                Add(Param(t, "SpeedEdit"), VRCExpressionParameters.ValueType.Float, 0.5f, saved: true, synced: false);
                Add(Param(t, "SpeedEditOpen"), VRCExpressionParameters.ValueType.Bool, 0f, saved: false, synced: false);
                Add(Param(t, "Emission"), VRCExpressionParameters.ValueType.Bool, 0f);
            }

            asset.parameters = list.ToArray();

            void Add(
                string name,
                VRCExpressionParameters.ValueType type,
                float defaultValue,
                bool saved = true,
                bool? synced = null)
            {
                if (!names.Add(name))
                    throw new InvalidOperationException($"Expression parameter collision: {name}");

                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = type,
                    defaultValue = defaultValue,
                    saved = saved,
                    networkSynced = synced ?? networkSynced
                });
            }
        }

        private static VRCExpressionsMenu MakeTargetMenu(NNRainbowTarget target)
        {
            var menu = MakeMenu(target.DisplayName);

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Rainbow",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = P(Param(target, "Enable")),
                value = 1f
            });

            var typeMenu = MakeMenu("虹色タイプ");
            typeMenu.controls.Add(ToggleInt("全体色相", Param(target, "Type"), 1f));
            typeMenu.controls.Add(ToggleInt("グラデーション", Param(target, "Type"), 2f));
            typeMenu.controls.Add(ToggleInt("流れる虹", Param(target, "Type"), 3f));
            menu.controls.Add(SubMenuControl("虹色タイプ", typeMenu));

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "速度",
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                parameter = P(Param(target, "SpeedEditOpen")),
                value = 1f,
                subParameters = new[] { P(Param(target, "SpeedEdit")) }
            });

            menu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "発光",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = P(Param(target, "Emission")),
                value = 1f
            });

            return menu;
        }

        private static void AddSubmenuPaged(VRCExpressionsMenu root, string label, VRCExpressionsMenu child)
        {
            if (root.controls == null) root.controls = new List<VRCExpressionsMenu.Control>();
            if (root.controls.Count < 8)
            {
                root.controls.Add(SubMenuControl(label, child));
                return;
            }

            var all = root.controls.ToList();
            all.Add(SubMenuControl(label, child));
            root.controls.Clear();

            var pages = new List<VRCExpressionsMenu>();
            var index = 0;
            while (index < all.Count)
            {
                var page = pages.Count == 0 ? root : MakeMenu($"NN - Rainbow {pages.Count + 1}");
                pages.Add(page);

                var take = Math.Min(7, all.Count - index);
                for (var i = 0; i < take; i++) page.controls.Add(all[index++]);
            }

            for (var i = 0; i < pages.Count - 1; i++)
                pages[i].controls.Add(SubMenuControl("Next", pages[i + 1]));
        }

        private static VRCExpressionsMenu MakeMenu(string name)
        {
            var x = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            x.name = name;
            x.controls = new List<VRCExpressionsMenu.Control>();
            return x;
        }

        private static VRCExpressionsMenu.Control ToggleInt(string name, string parameter, float value)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = P(parameter),
                value = value
            };
        }

        private static VRCExpressionsMenu.Control SubMenuControl(string name, VRCExpressionsMenu menu)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = menu
            };
        }

        private static VRCExpressionsMenu.Control.Parameter P(string name) =>
            new VRCExpressionsMenu.Control.Parameter { name = name };

        public static string Param(NNRainbowTarget t, string suffix) => $"NNR/{t.Id}/{suffix}";
    }
}
