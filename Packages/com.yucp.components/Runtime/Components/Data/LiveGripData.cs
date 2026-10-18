using System;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Poses a hand's fingers around this object.
    ///
    /// At build, each finger closes until its segments touch the object, the way a hand closes
    /// around something. Live mode keeps them there in game with Final IK: when the object is
    /// moved in the hand the tips slide over its surface. Baked mode stores the grip as a hand pose instead and adds
    /// no IK. A toggle turns the grip on and off: its own menu toggle, or an existing VRCFury
    /// toggle. It is off whenever the object is hidden.
    ///
    /// Without Live Grip Points the object is held as placed. Grip points choose where the hand
    /// holds it, pin fingers to spots on it, and with two or more the grip parameter moves the
    /// object through the hand between them; the object then hangs from the hand through Live Grip.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Live Grip")]
    [SupportBanner]
    public class LiveGripData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public enum Hand { Left, Right }

        public enum Mode { Live, Baked }

        [Tooltip("Hand that holds the object. Set to the nearest hand when the component is added.")]
        public Hand hand;

        [Tooltip("Live: Final IK keeps the fingers on the object as it moves. Baked: the grip is saved as a hand pose, with no IK on the avatar.")]
        public Mode mode = Mode.Live;

        [Tooltip("How far the finger bones stay off the surface, in metres: roughly the finger's radius. Raise it if fingers sink into the object, lower it if they float.")]
        [Range(0f, 0.03f)]
        public float fingerThickness = 0.008f;

        [Header("Grip Points")]
        [Tooltip("Global parameter that moves the object through its Live Grip Points: 0 = first, 1 = last, in hierarchy order. Drive it from your own animations or the radial the menu adds. Empty uses \"LiveGrip/<object>\". Used with two or more grip points.")]
        public string gripParameter = "";

        [Header("Toggle")]
        [Tooltip("Turn the grip on and off with an existing VRCFury toggle instead of adding a menu toggle.")]
        public bool useExistingToggle;

        [Tooltip("The VRCFury toggle that turns the grip on. Empty uses the first toggle on this object or its children.")]
        public Component selectedToggle;

        [Tooltip("Turn the grip on with this existing Bool parameter instead of a toggle, e.g. one a generator drives. Adds no menu item or parameter.")]
        public string toggleParameter = "";

        [Tooltip("Submenu path for the grip toggle, e.g. \"Props\". Empty puts it at the root.")]
        public string menuPath = "";

        [Tooltip("Name of the grip toggle. Empty uses \"<object> Grip\".")]
        public string menuName = "";

        [Tooltip("Whether the grip starts on.")]
        public bool defaultOn = true;

        [SerializeField, HideInInspector] private string lastBuildSummary;
        [SerializeField, HideInInspector] private long lastBuildTicks;

        public int PreprocessOrder => 0;

        public bool OnPreprocess() => true;

        public void SetBuildSummary(string summary)
        {
            lastBuildSummary = summary;
            lastBuildTicks = DateTime.UtcNow.Ticks;
        }

        public string GetBuildSummary() => lastBuildSummary;

        private void Reset()
        {
            var animator = GetComponentInParent<Animator>(true);
            if (animator == null || !animator.isHuman) return;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (left == null || right == null) return;
            hand = Vector3.Distance(transform.position, left.position) <= Vector3.Distance(transform.position, right.position)
                ? Hand.Left : Hand.Right;
        }
    }
}
