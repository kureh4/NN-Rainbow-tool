//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow spatial Fur extension

#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRScroll; \
    float _NNRBlend; \
    float _NNRSpatialScale; \
    float _NNRSpatialOffset;

#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_NNRRainbowTex);

#define LIL_V2F_FORCE_POSITION_OS
#define LIL_CUSTOM_VERT_COPY

#define BEFORE_SHADOW lilNNRainbowSpatial(fd);
