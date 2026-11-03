using System;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    internal enum NNRainbowVariant
    {
        Opaque,
        OpaqueOutline,
        Cutout,
        CutoutOutline,
        Transparent,
        TransparentOutline,
        Fur,
        FurCutout,
        FurTwoPass
    }

    internal static class NNRainbowShaderVariants
    {
        public static bool TryGetVariant(Material material, out NNRainbowVariant variant)
        {
            variant = default;
            if (material == null || material.shader == null) return false;

            var shaderName = material.shader.name ?? string.Empty;
            if (shaderName.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // Standard lilToon Fur variants use the same Emission / Emission2nd
            // properties as the normal shader family, so NN - Rainbow can keep
            // the original Fur shader and drive only its unused emission slot.
            if (TryGetFurVariant(shaderName, out variant))
                return true;

            if (ContainsAny(shaderName, "Lite", "Fur", "Gem", "Refraction", "Tessellation", "FakeShadow"))
                return false;

            var outline = shaderName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0;
            if (material.HasProperty("_UseOutline"))
                outline = material.GetFloat("_UseOutline") > 0.5f;

            int mode;
            if (material.HasProperty("_TransparentMode"))
                mode = Mathf.RoundToInt(material.GetFloat("_TransparentMode"));
            else if (shaderName.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0)
                mode = 1;
            else if (shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0)
                mode = 2;
            else
                mode = 0;

            switch (mode)
            {
                case 0:
                    variant = outline ? NNRainbowVariant.OpaqueOutline : NNRainbowVariant.Opaque;
                    return true;
                case 1:
                    variant = outline ? NNRainbowVariant.CutoutOutline : NNRainbowVariant.Cutout;
                    return true;
                case 2:
                    variant = outline ? NNRainbowVariant.TransparentOutline : NNRainbowVariant.Transparent;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsFur(Material material)
        {
            if (!TryGetVariant(material, out var variant)) return false;
            return variant == NNRainbowVariant.Fur ||
                   variant == NNRainbowVariant.FurCutout ||
                   variant == NNRainbowVariant.FurTwoPass;
        }

        private static bool TryGetFurVariant(string shaderName, out NNRainbowVariant variant)
        {
            if (string.Equals(shaderName, "Hidden/lilToonFur", StringComparison.OrdinalIgnoreCase))
            {
                variant = NNRainbowVariant.Fur;
                return true;
            }

            if (string.Equals(shaderName, "Hidden/lilToonFurCutout", StringComparison.OrdinalIgnoreCase))
            {
                variant = NNRainbowVariant.FurCutout;
                return true;
            }

            if (string.Equals(shaderName, "Hidden/lilToonFurTwoPass", StringComparison.OrdinalIgnoreCase))
            {
                variant = NNRainbowVariant.FurTwoPass;
                return true;
            }

            variant = default;
            return false;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var n in needles)
                if (value.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static string CustomShaderName(NNRainbowVariant variant)
        {
            switch (variant)
            {
                case NNRainbowVariant.Opaque: return "nekoneko/NN Rainbow/lilToon";
                case NNRainbowVariant.OpaqueOutline: return "Hidden/nekoneko/NN Rainbow/OpaqueOutline";
                case NNRainbowVariant.Cutout: return "Hidden/nekoneko/NN Rainbow/Cutout";
                case NNRainbowVariant.CutoutOutline: return "Hidden/nekoneko/NN Rainbow/CutoutOutline";
                case NNRainbowVariant.Transparent: return "Hidden/nekoneko/NN Rainbow/Transparent";
                case NNRainbowVariant.TransparentOutline: return "Hidden/nekoneko/NN Rainbow/TransparentOutline";
                case NNRainbowVariant.Fur: return "Hidden/lilToonFur";
                case NNRainbowVariant.FurCutout: return "Hidden/lilToonFurCutout";
                case NNRainbowVariant.FurTwoPass: return "Hidden/lilToonFurTwoPass";
                default: throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
            }
        }

        public static int TransparentMode(NNRainbowVariant variant)
        {
            switch (variant)
            {
                case NNRainbowVariant.Cutout:
                case NNRainbowVariant.CutoutOutline:
                    return 1;
                case NNRainbowVariant.Transparent:
                case NNRainbowVariant.TransparentOutline:
                    return 2;
                case NNRainbowVariant.Fur:
                    return 5;
                case NNRainbowVariant.FurCutout:
                    return 6;
                case NNRainbowVariant.FurTwoPass:
                    return 7;
                default:
                    return 0;
            }
        }

        public static bool HasOutline(NNRainbowVariant variant)
        {
            return variant == NNRainbowVariant.OpaqueOutline ||
                   variant == NNRainbowVariant.CutoutOutline ||
                   variant == NNRainbowVariant.TransparentOutline;
        }
    }
}
