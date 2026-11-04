//----------------------------------------------------------------------------------------------------------------------
// NN - Rainbow spatial Fur extension

#define LIL_CUSTOM_PROPERTIES \
    float _NNRMode; \
    float _NNRScroll; \
    float _NNRBlend; \
    float _NNRSpatialScale; \
    float _NNRSpatialOffset;


#define LIL_CUSTOM_VERT_COPY

#define BEFORE_SHADOW lilNNRainbowSpatial(fd);
