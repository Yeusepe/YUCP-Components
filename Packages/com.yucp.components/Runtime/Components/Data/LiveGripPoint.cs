using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// One way of holding a Live Grip object. This transform is where the hand's wrist bone sits on
    /// the object. A finger with a pin rests its tip on the pin; the others close until they touch,
    /// wrapping over edges. With several grip points the grip parameter moves the object through
    /// the hand from one to the next, in hierarchy order.
    ///
    /// Create it with Live Grip's "Add Grip Point At Current Hand".
    /// </summary>
    [AddComponentMenu("YUCP/Live Grip Point")]
    public class LiveGripPoint : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Optional. Where the thumb tip rests on the object. Empty: the thumb closes until it touches.")]
        public Transform thumb;

        [Tooltip("Optional. Where the index fingertip rests. Empty: it closes until it touches.")]
        public Transform index;

        [Tooltip("Optional. Where the middle fingertip rests. Empty: it closes until it touches.")]
        public Transform middle;

        [Tooltip("Optional. Where the ring fingertip rests. Empty: it closes until it touches.")]
        public Transform ring;

        [Tooltip("Optional. Where the little fingertip rests. Empty: it closes until it touches.")]
        public Transform little;

        /// <summary>Thumb to little finger.</summary>
        public Transform[] Pins => new[] { thumb, index, middle, ring, little };
    }
}
