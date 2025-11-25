// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Core/Math/UxrUniversalLocalAxes.cs
// Adapted and simplified for YUCP Components – VRChat avatars

using System;
using UnityEngine;

namespace YUCP.Components.HandPoses
{
    [Serializable]
    public class YUCPUniversalLocalAxes
    {
        [SerializeField] private Vector3 _localRight = Vector3.right;
        [SerializeField] private Vector3 _localUp = Vector3.up;
        [SerializeField] private Vector3 _localForward = Vector3.forward;

        public Vector3 LocalRight
        {
            get => _localRight;
            set => _localRight = value;
        }

        public Vector3 LocalUp
        {
            get => _localUp;
            set => _localUp = value;
        }

        public Vector3 LocalForward
        {
            get => _localForward;
            set => _localForward = value;
        }

        public YUCPUniversalLocalAxes()
        {
            _localRight = Vector3.right;
            _localUp = Vector3.up;
            _localForward = Vector3.forward;
        }

        public YUCPUniversalLocalAxes(Vector3 localRight, Vector3 localUp, Vector3 localForward)
        {
            _localRight = localRight;
            _localUp = localUp;
            _localForward = localForward;
        }

        public static YUCPUniversalLocalAxes FromTransform(Transform transform, Transform universalReference)
        {
            Vector3 localRight = transform.InverseTransformDirection(universalReference != null ? universalReference.right : Vector3.right);
            Vector3 localUp = transform.InverseTransformDirection(universalReference != null ? universalReference.up : Vector3.up);
            Vector3 localForward = transform.InverseTransformDirection(universalReference != null ? universalReference.forward : Vector3.forward);

            return new YUCPUniversalLocalAxes(localRight, localUp, localForward);
        }
    }
}
