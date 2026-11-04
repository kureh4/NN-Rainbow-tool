//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow / lilToon custom insert

float3 nnrHsvToRgb(float h)
{
    float3 p = abs(frac(float3(h,h,h) + float3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return saturate(p - 1.0);
}

void nnrApplyRainbow(inout lilFragData fd)
{
    if(_NNRMode < 1.5 || _NNRStrength <= 0.0001) return;

    float3 nnrPositionOS = lilTransformWStoOS(fd.positionWS);
    float nnrHue = (nnrPositionOS.x - nnrPositionOS.y) * _NNRScale + _NNROffset;

    if(_NNRMode > 2.5)
        nnrHue += LIL_TIME * _NNRSpeed;

    float3 nnrRainbow = nnrHsvToRgb(frac(nnrHue));
    float nnrLuma = saturate(dot(fd.albedo, float3(0.299, 0.587, 0.114)));
    float nnrDetail = lerp(0.35, 1.15, nnrLuma);
    float3 nnrResult = nnrRainbow * nnrDetail;
    nnrResult *= lerp(1.0, 1.65, saturate(_NNRGlow));

    fd.albedo = lerp(fd.albedo, nnrResult, saturate(_NNRStrength));
    fd.col.rgb = fd.albedo;
}
