using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Runtime component for Attach to Blendshape (migrated to VRCFury FullController).
    /// This component no longer applies transforms manually - all transform animation is handled
    /// by VRCFury's FullController through Unity's animator system.
    /// This stub component is kept for backward compatibility but does nothing at runtime.
    /// </summary>
    public class AttachToBlendshapeRuntime : MonoBehaviour
    {
        // This component is now a no-op - all transform animation is handled by VRCFury FullController
        // which is injected during build via AttachToBlendshapeProcessor.InjectDirectBlendTreeController()
    }
}
