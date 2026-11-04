using System;
using UnityEditor;
using UnityEngine;

namespace Nekoneko.NNRainbow.Editor
{
    [InitializeOnLoad]
    internal static class NNRainbowShaderBootstrap
    {
        private const string SessionKey = "NNRainbow.CustomFurShaders.Ready.1.0.25";

        internal const string FurShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur.lilcontainer";
        internal const string FurCutoutShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur_cutout.lilcontainer";
        internal const string FurTwoPassShaderPath =
            "Packages/com.nekoneko.nn-rainbow-tool/Shaders/lts_nnr_fur_two.lilcontainer";

        static NNRainbowShaderBootstrap()
        {
            EditorApplication.delayCall += WarmupOnce;
        }

        private static void WarmupOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += WarmupOnce;
                return;
            }

            var ok = true;
            ok &= EnsureShaderReady(FurShaderPath, "Hidden/NNRainbow/Fur", out _, out var e0);
            ok &= EnsureShaderReady(FurCutoutShaderPath, "Hidden/NNRainbow/FurCutout", out _, out var e1);
            ok &= EnsureShaderReady(FurTwoPassShaderPath, "Hidden/NNRainbow/FurTwoPass", out _, out var e2);

            if (ok)
            {
                SessionState.SetBool(SessionKey, true);
                Debug.Log("[NN - Rainbow] lilToon Fur shaders ready.");
            }
            else
            {
                Debug.LogError("[NN - Rainbow] lilToon Fur shader warmup failed.\n" +
                               e0 + "\n" + e1 + "\n" + e2);
            }
        }

        internal static bool EnsureShaderReady(
            string shaderPath,
            string shaderName,
            out Shader shader,
            out string error)
        {
            shader = LoadShader(shaderPath, shaderName);
            if (HasRainbowProperties(shader))
            {
                error = null;
                return true;
            }

            string unpackInfo;
            try
            {
                var source = lilToon.lilShaderContainer.UnpackContainer(shaderPath);
                unpackInfo = source != null && source.Contains("_NNRMode")
                    ? "unpacked source contains _NNRMode"
                    : "unpacked source DOES NOT contain _NNRMode";
            }
            catch (Exception e)
            {
                unpackInfo = "UnpackContainer failed: " + e.GetType().Name + ": " + e.Message;
            }

            try
            {
                AssetDatabase.ImportAsset(
                    shaderPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception e)
            {
                error = "Force reimport failed for " + shaderPath + ": " +
                        e.GetType().Name + ": " + e.Message + "; " + unpackInfo;
                return false;
            }

            shader = LoadShader(shaderPath, shaderName);
            if (HasRainbowProperties(shader))
            {
                error = null;
                return true;
            }

            error = "custom Fur shader still has no NN Rainbow properties after ForceReimport. " +
                    "path=" + shaderPath +
                    ", expectedName=" + shaderName +
                    ", loadedName=" + (shader != null ? shader.name : "<null>") +
                    ", " + unpackInfo;
            return false;
        }

        private static Shader LoadShader(string shaderPath, string shaderName)
        {
            var shader = AssetDatabase.LoadMainAssetAtPath(shaderPath) as Shader;
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
                shader = Shader.Find(shaderName);
            return shader;
        }

        private static bool HasRainbowProperties(Shader shader)
        {
            if (shader == null || !shader.isSupported) return false;

            Material material = null;
            try
            {
                material = new Material(shader);
                return material.HasProperty("_NNRMode") &&
                       material.HasProperty("_NNRSpeed") &&
                       material.HasProperty("_NNRScale") &&
                       material.HasProperty("_NNROffset") &&
                       material.HasProperty("_NNRStrength") &&
                       material.HasProperty("_NNRGlow");
            }
            finally
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
