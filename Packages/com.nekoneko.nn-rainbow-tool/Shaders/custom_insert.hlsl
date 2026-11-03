void lilNNRainbowFur(inout lilFragData fd)
{
    if(_NNRMode <= 0.5 || _NNRBlend <= 0.0001) return;

    float nnrTiling = (_NNRMode < 1.5) ? 0.001 : 1.0;
    float nnrScrollEnabled = (_NNRMode < 1.5 || _NNRMode > 2.5) ? 1.0 : 0.0;
    float2 nnrUV = fd.uv0 * nnrTiling;
    nnrUV.x += frac(LIL_TIME * _NNRScroll * nnrScrollEnabled);

    float3 nnrRainbow = LIL_SAMPLE_2D(_NNRRainbowTex, lil_sampler_linear_repeat, nnrUV).rgb;
    fd.col.rgb = lilBlendColor(fd.col.rgb, nnrRainbow, _NNRBlend, 1);
}
