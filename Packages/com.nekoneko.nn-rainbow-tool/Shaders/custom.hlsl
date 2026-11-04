//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow / lilToon custom shader hook

#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRSpeed; \
    float _NNRScale; \
    float _NNROffset; \
    float _NNRStrength; \
    float _NNRGlow;

#define LIL_CUSTOM_TEXTURES

#define BEFORE_SHADOW nnrApplyRainbow(fd);
