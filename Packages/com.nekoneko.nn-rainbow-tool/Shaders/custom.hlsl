//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow custom Fur extension

#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRScroll; \
    float _NNRBlend;

#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_NNRRainbowTex);

#define LIL_CUSTOM_VERT_COPY

#define BEFORE_FUR lilNNRainbowFur(fd);
