// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Avatar/Rig/UxrRuntimeFingerDescriptor.cs
// Adapted for YUCP Components - Works with Unity Animator and HumanBodyBones

using UnityEngine;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    ///     Runtime, lightweight version of <see cref="YUCPFingerDescriptor" />. See <see cref="YUCPRuntimeHandDescriptor" />.
    /// </summary>
    public class YUCPRuntimeFingerDescriptor
    {
        #region Public Types & Data

        /// <summary>
        ///     Gets whether the descriptor contains metacarpal information.
        /// </summary>
        public bool HasMetacarpalInfo { get; private set; }

        /// <summary>
        ///     Gets the metacarpal local rotation.
        /// </summary>
        public Quaternion MetacarpalRotation { get; private set; }

        /// <summary>
        ///     Gets the proximal local rotation.
        /// </summary>
        public Quaternion ProximalRotation { get; private set; }

        /// <summary>
        ///     Gets the intermediate local rotation.
        /// </summary>
        public Quaternion IntermediateRotation { get; private set; }

        /// <summary>
        ///     Gets the distal local rotation.
        /// </summary>
        public Quaternion DistalRotation { get; private set; }

        #endregion

        #region Constructors & Finalizer

        /// <summary>
        ///     Default constructor.
        /// </summary>
        public YUCPRuntimeFingerDescriptor()
        {
        }

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="animator">Animator to compute the runtime finger descriptor for</param>
        /// <param name="handSide">Which hand to process</param>
        /// <param name="handDescriptor">The source data</param>
        /// <param name="fingerType">Which finger to store</param>
        /// <param name="handLocalAxes">Hand axes system</param>
        /// <param name="fingerLocalAxes">Finger axes system</param>
        public YUCPRuntimeFingerDescriptor(Animator animator, YUCPHandSide handSide, YUCPHandDescriptor handDescriptor, YUCPFingerType fingerType, YUCPUniversalLocalAxes handLocalAxes, YUCPUniversalLocalAxes fingerLocalAxes)
        {
            Transform wrist = YUCPAvatarRigHelper.GetWrist(animator, handSide);
            if (wrist == null) return;

            var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);
            
            if (proximal == null) return;

            YUCPFingerDescriptor fingerDescriptor = handDescriptor.GetFinger(fingerType);

            HasMetacarpalInfo = fingerDescriptor.HasMetacarpalInfo && metacarpal != null;

            Quaternion metacarpalWorldRotation;
            Quaternion proximalWorldRotation;

            if (HasMetacarpalInfo)
            {
                metacarpalWorldRotation = GetRotation(wrist, metacarpal, fingerDescriptor.Metacarpal, handLocalAxes, fingerLocalAxes);
                proximalWorldRotation   = GetRotation(metacarpal, proximal, fingerDescriptor.Proximal, fingerLocalAxes, fingerLocalAxes);
            }
            else
            {
                metacarpalWorldRotation = Quaternion.identity;
                proximalWorldRotation   = GetRotation(wrist, proximal, fingerDescriptor.ProximalNoMetacarpal, handLocalAxes, fingerLocalAxes);
            }

            Quaternion intermediateWorldRotation = intermediate != null 
                ? GetRotation(proximal, intermediate, fingerDescriptor.Intermediate, fingerLocalAxes, fingerLocalAxes)
                : Quaternion.identity;
            
            Quaternion distalWorldRotation = (distal != null && intermediate != null)
                ? GetRotation(intermediate, distal, fingerDescriptor.Distal, fingerLocalAxes, fingerLocalAxes)
                : Quaternion.identity;

            if (HasMetacarpalInfo)
            {
                MetacarpalRotation = Quaternion.Inverse(wrist.rotation) * metacarpalWorldRotation;
                ProximalRotation   = Quaternion.Inverse(metacarpal.rotation) * proximalWorldRotation;
            }
            else
            {
                MetacarpalRotation = Quaternion.identity;
                ProximalRotation   = Quaternion.Inverse(wrist.rotation) * proximalWorldRotation;
            }

            if (intermediate != null)
            {
                IntermediateRotation = Quaternion.Inverse(proximal.rotation) * intermediateWorldRotation;
            }

            if (distal != null && intermediate != null)
            {
                DistalRotation = Quaternion.Inverse(intermediate.rotation) * distalWorldRotation;
            }
        }

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="hasMetacarpalInfo">Whether the finger contains metacarpal information</param>
        /// <param name="metacarpalRotation">Metacarpal local rotation (optional)</param>
        /// <param name="proximalRotation">Proximal local rotation</param>
        /// <param name="intermediateRotation">Intermediate local rotation</param>
        /// <param name="distalRotation">Distal local rotation</param>
        public YUCPRuntimeFingerDescriptor(bool hasMetacarpalInfo, Quaternion metacarpalRotation, Quaternion proximalRotation, Quaternion intermediateRotation, Quaternion distalRotation)
        {
            HasMetacarpalInfo    = hasMetacarpalInfo;
            MetacarpalRotation   = metacarpalRotation;
            ProximalRotation     = proximalRotation;
            IntermediateRotation = intermediateRotation;
            DistalRotation       = distalRotation;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Copies the data from another descriptor.
        /// </summary>
        /// <param name="fingerDescriptor">Descriptor to copy the data from</param>
        public void CopyFrom(YUCPRuntimeFingerDescriptor fingerDescriptor)
        {
            if (fingerDescriptor == null)
            {
                return;
            }

            HasMetacarpalInfo    = fingerDescriptor.HasMetacarpalInfo;
            MetacarpalRotation   = fingerDescriptor.MetacarpalRotation;
            ProximalRotation     = fingerDescriptor.ProximalRotation;
            IntermediateRotation = fingerDescriptor.IntermediateRotation;
            DistalRotation       = fingerDescriptor.DistalRotation;
        }

        /// <summary>
        ///     Interpolates towards another runtime finger descriptor.
        /// </summary>
        /// <param name="fingerDescriptor">Runtime finger descriptor</param>
        /// <param name="blend">Interpolation value [0.0, 1.0]</param>
        public void InterpolateTo(YUCPRuntimeFingerDescriptor fingerDescriptor, float blend)
        {
            if (fingerDescriptor == null)
            {
                return;
            }

            if (HasMetacarpalInfo && fingerDescriptor.HasMetacarpalInfo)
            {
                MetacarpalRotation = Quaternion.Slerp(MetacarpalRotation, fingerDescriptor.MetacarpalRotation, blend);
            }

            ProximalRotation     = Quaternion.Slerp(ProximalRotation,     fingerDescriptor.ProximalRotation,     blend);
            IntermediateRotation = Quaternion.Slerp(IntermediateRotation, fingerDescriptor.IntermediateRotation, blend);
            DistalRotation       = Quaternion.Slerp(DistalRotation,       fingerDescriptor.DistalRotation,       blend);
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Gets the local rotation of a <see cref="YUCPFingerNodeDescriptor" /> when applied to an object.
        /// </summary>
        /// <param name="parent">Parent the node descriptor references its rotation to</param>
        /// <param name="node">Transform to get the local rotation of</param>
        /// <param name="nodeDescriptor">
        ///     Bone information in the well-known coordinate system of a <see cref="YUCPHandPoseAsset" />
        /// </param>
        /// <param name="parentLocalAxes">Coordinate system of the <paramref name="parent" /> transform</param>
        /// <param name="nodeLocalAxes">Coordinate system of the <paramref name="node" /> transform</param>
        /// <returns>
        ///     Local rotation that should be applied to <paramref name="node" /> when using
        ///     <paramref name="nodeDescriptor" />
        /// </returns>
        private static Quaternion GetRotation(Transform parent, Transform node, YUCPFingerNodeDescriptor nodeDescriptor, YUCPUniversalLocalAxes parentLocalAxes, YUCPUniversalLocalAxes nodeLocalAxes)
        {
            Matrix4x4 nodeLocalAxesMatrix = new Matrix4x4();
            nodeLocalAxesMatrix.SetColumn(0, nodeLocalAxes.LocalRight);
            nodeLocalAxesMatrix.SetColumn(1, nodeLocalAxes.LocalUp);
            nodeLocalAxesMatrix.SetColumn(2, nodeLocalAxes.LocalForward);
            nodeLocalAxesMatrix.SetColumn(3, new Vector4(0, 0, 0, 1));
            Quaternion nodeUniversalToActual = Quaternion.Inverse(nodeLocalAxesMatrix.rotation);

            Matrix4x4 parentUniversalMatrix = new Matrix4x4();
            parentUniversalMatrix.SetColumn(0, parent.TransformVector(parentLocalAxes.LocalRight));
            parentUniversalMatrix.SetColumn(1, parent.TransformVector(parentLocalAxes.LocalUp));
            parentUniversalMatrix.SetColumn(2, parent.TransformVector(parentLocalAxes.LocalForward));
            parentUniversalMatrix.SetColumn(3, new Vector4(0, 0, 0, 1));

            Matrix4x4 nodeUniversalMatrix = new Matrix4x4();
            nodeUniversalMatrix.SetColumn(0, parentUniversalMatrix.MultiplyVector(nodeDescriptor.Right));
            nodeUniversalMatrix.SetColumn(1, parentUniversalMatrix.MultiplyVector(nodeDescriptor.Up));
            nodeUniversalMatrix.SetColumn(2, parentUniversalMatrix.MultiplyVector(nodeDescriptor.Forward));
            nodeUniversalMatrix.SetColumn(3, new Vector4(0, 0, 0, 1));

            return nodeUniversalMatrix.rotation * nodeUniversalToActual;
        }

        #endregion
    }
}

