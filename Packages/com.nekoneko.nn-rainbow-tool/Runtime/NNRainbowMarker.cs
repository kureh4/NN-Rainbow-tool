using nadena.dev.ndmf;
using UnityEngine;

namespace Nekoneko.NNRainbow
{
    /// <summary>
    /// Build-time-only marker for NN - Rainbow.
    /// NDMF consumes this component during avatar build; it is never intended to remain on the uploaded avatar.
    /// INDMFEditorOnly derives from VRChat's IEditorOnly when the VRCSDK is present, so SDK validation ignores it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("NN - Rainbow/NN Rainbow Marker")]
    public sealed class NNRainbowMarker : MonoBehaviour, INDMFEditorOnly
    {
    }
}
