void lilNNRainbowSpatial(inout lilFragData fd)
{
    // Type 1 (global hue) intentionally stays on the stable Animator _Color path.
    if(_NNRMode < 1.5 || _NNRBlend <= 0.0001) return;

    // Object-space diagonal: upper-left -> lower-right uses X - Y.
    float t = (fd.positionOS.x - fd.positionOS.y) * _NNRSpatialScale + _NNRSpatialOffset;

    // Type 3 flows along the exact same diagonal. Type 2 stays fixed.
    if(_NNRMode > 2.5)
    {
        t += LIL_TIME * _NNRScroll;
    }

    float3 rainbow = LIL_SAMPLE_2D(
        _NNRRainbowTex,
        lil_sampler_linear_repeat,
        float2(frac(t), 0.5)
    ).rgb;

    // Existing base strength 0.32 becomes clearly visible while glow 0.90 reaches full rainbow.
    float spatialBlend = saturate(_NNRBlend * 2.5);
    fd.col.rgb = lerp(fd.col.rgb, rainbow, spatialBlend);
    fd.albedo = lerp(fd.albedo, rainbow, spatialBlend);
}
