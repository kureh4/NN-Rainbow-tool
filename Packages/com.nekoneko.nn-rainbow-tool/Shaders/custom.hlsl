#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRScroll; \
    float _NNRBlend;

#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_NNRRainbowTex);

#define BEFORE_FUR \
    if(_NNRMode > 0.5 && _NNRBlend > 0.0001) \
    { \
        float nnrTiling = (_NNRMode < 1.5) ? 0.001 : 1.0; \
        float nnrUseScroll = (_NNRMode < 1.5 || _NNRMode > 2.5) ? 1.0 : 0.0; \
        float2 nnrUV = fd.uv0 * nnrTiling + float2(frac(LIL_TIME * _NNRScroll * nnrUseScroll), 0.0); \
        float3 nnrRainbow = LIL_SAMPLE_2D(_NNRRainbowTex, sampler_MainTex, nnrUV).rgb; \
        fd.col.rgb += nnrRainbow * _NNRBlend; \
    }
