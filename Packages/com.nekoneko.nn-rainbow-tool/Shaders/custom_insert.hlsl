void lilNNRainbowSpatial(inout lilFragData fd)
{
    // Type 1 (global hue) intentionally stays on the stable Animator _Color path.
    if(_NNRMode < 1.5 || _NNRBlend <= 0.0001) return;

    // Fur forward pass always carries positionWS.
    // Convert it back to the renderer's local/object space so the rainbow stays attached
    // to the avatar and does not depend on UV layout or world movement.
    float3 positionOS = lilTransformWStoOS(fd.positionWS);

    // Upper-left -> lower-right diagonal.
    float t = (positionOS.x - positionOS.y) * _NNRSpatialScale + _NNRSpatialOffset;

    // Type 3 moves the exact same diagonal pattern; Type 2 stays fixed.
    if(_NNRMode > 2.5)
    {
        t += LIL_TIME * _NNRScroll;
    }

    float3 rainbow = LIL_SAMPLE_2D(
        _NNRRainbowTex,
        lil_sampler_linear_repeat,
        float2(frac(t), 0.5)
    ).rgb;

    // fd.col already contains the material's original _MainTex result.
    // Fur alpha/density from _FurNoiseMask has also already been applied by lilToon.
    // Keep the main texture's luminance/detail while replacing its hue with the rainbow.
    float baseLuma = max(dot(fd.col.rgb, float3(0.299, 0.587, 0.114)), 0.08);
    float detail = lerp(0.45, 1.15, saturate(baseLuma));
    float3 texturedRainbow = rainbow * detail;

    // Base mode is intentionally strong enough to read as a real rainbow.
    // Emission ON reaches full replacement.
    float spatialBlend = saturate(_NNRBlend * 3.0);
    fd.col.rgb = lerp(fd.col.rgb, texturedRainbow, spatialBlend);
    fd.albedo = lerp(fd.albedo, texturedRainbow, spatialBlend);
}
