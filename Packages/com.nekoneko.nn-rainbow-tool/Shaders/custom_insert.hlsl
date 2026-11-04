float3 nnrHsvToRgb(float h, float s, float v)
{
    float3 p = abs(frac(h.xxx + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return v * lerp(1.0.xxx, saturate(p - 1.0), s);
}

void lilNNRainbowSpatial(inout lilFragData fd)
{
    // Type 1 (global hue) intentionally stays on the stable Animator _Color path.
    if(_NNRMode < 1.5 || _NNRBlend <= 0.0001) return;

    // lilToon 2.4.1 Fur forward pass always carries positionWS.
    // Convert it back to local/object space so the pattern stays attached to the avatar.
    float3 positionOS = lilTransformWStoOS(fd.positionWS);

    // Upper-left -> lower-right:
    // upper-left = smaller X and larger Y => lower hue value (red side)
    // lower-right = larger X and smaller Y => higher hue value (purple side)
    float hue = (positionOS.x - positionOS.y) * _NNRSpatialScale + _NNRSpatialOffset;

    if(_NNRMode > 2.5)
    {
        hue += LIL_TIME * _NNRScroll;
    }

    hue = frac(hue);

    // Generate rainbow directly in HLSL.
    // No texture sampling/import direction/filtering can collapse this into one color.
    float3 rainbow = nnrHsvToRgb(hue, 1.0, 1.0);

    // fd.col is already the result of the original _MainTex and Fur alpha/noise processing.
    // Keep luminance/detail from the assigned main texture while replacing hue with rainbow.
    float baseLuma = max(dot(fd.col.rgb, float3(0.299, 0.587, 0.114)), 0.08);
    float detail = lerp(0.50, 1.10, saturate(baseLuma));
    float3 texturedRainbow = rainbow * detail;

    float spatialBlend = saturate(_NNRBlend * 3.0);
    fd.col.rgb = lerp(fd.col.rgb, texturedRainbow, spatialBlend);
    fd.albedo = lerp(fd.albedo, texturedRainbow, spatialBlend);
}
