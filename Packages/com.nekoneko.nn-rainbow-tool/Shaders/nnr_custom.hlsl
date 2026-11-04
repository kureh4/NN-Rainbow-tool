//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow: lilToon Fur extension
//
// Keeps lilToon's own MainTex / FurNoise / lighting / alpha / Fur geometry.
// Only the final albedo hue is replaced for Gradient / Flowing modes.

#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRSpeed; \
    float _NNRScale; \
    float _NNROffset; \
    float _NNRStrength; \
    float _NNRGlow;

float3 nnrHsvToRgb(float h)
{
    float3 p = abs(frac(float3(h,h,h) + float3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return saturate(p - 1.0);
}

#define BEFORE_SHADOW \
    if(_NNRMode > 1.5) \
    { \
        float3 nnrPositionOS = lilTransformWStoOS(fd.positionWS); \
        float nnrHue = (nnrPositionOS.x - nnrPositionOS.y) * _NNRScale + _NNROffset; \
        if(_NNRMode > 2.5) nnrHue += LIL_TIME * _NNRSpeed; \
        float3 nnrRainbow = nnrHsvToRgb(frac(nnrHue)); \
        float nnrLuma = saturate(dot(fd.albedo, float3(0.299, 0.587, 0.114))); \
        float nnrDetail = lerp(0.35, 1.15, nnrLuma); \
        float3 nnrResult = nnrRainbow * nnrDetail; \
        nnrResult *= lerp(1.0, 1.65, saturate(_NNRGlow)); \
        fd.albedo = lerp(fd.albedo, nnrResult, saturate(_NNRStrength)); \
        fd.col.rgb = fd.albedo; \
    }
