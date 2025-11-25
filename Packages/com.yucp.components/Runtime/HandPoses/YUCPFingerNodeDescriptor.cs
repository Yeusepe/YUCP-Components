// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Manipulation/HandPoses/UxrFingerNodeDescriptor.cs
// Adapted for YUCP Components

using System;
using UnityEngine;

namespace YUCP.Components.HandPoses
{
    [Serializable]
    public struct YUCPFingerNodeDescriptor
    {
        [SerializeField] private Matrix4x4 _transformRelativeToHand;
        [SerializeField] private Vector3   _right;
        [SerializeField] private Vector3   _up;
        [SerializeField] private Vector3   _forward;

        public Matrix4x4 TransformRelativeToHand => _transformRelativeToHand;
        public Vector3 Right => _right;
        public Vector3 Up => _up;
        public Vector3 Forward => _forward;

        public void Compute(
            Transform hand,
            Transform parent,
            Transform node,
            YUCPUniversalLocalAxes parentLocalAxes,
            YUCPUniversalLocalAxes nodeLocalAxes,
            bool computeRelativeMatrixOnly)
        {
            _transformRelativeToHand = hand.worldToLocalMatrix * node.localToWorldMatrix;

            if (computeRelativeMatrixOnly)
            {
                return;
            }

            Matrix4x4 matrixParent = new Matrix4x4();
            matrixParent.SetColumn(0, parent.TransformVector(parentLocalAxes.LocalRight));
            matrixParent.SetColumn(1, parent.TransformVector(parentLocalAxes.LocalUp));
            matrixParent.SetColumn(2, parent.TransformVector(parentLocalAxes.LocalForward));
            matrixParent.SetColumn(3, new Vector4(parent.position.x, parent.position.y, parent.position.z, 1));

            _right   = matrixParent.inverse.MultiplyVector(node.TransformVector(nodeLocalAxes.LocalRight));
            _up      = matrixParent.inverse.MultiplyVector(node.TransformVector(nodeLocalAxes.LocalUp));
            _forward = matrixParent.inverse.MultiplyVector(node.TransformVector(nodeLocalAxes.LocalForward));
        }

        public void Mirror()
        {
            _right.x   = -_right.x;
            _right     = -_right;
            _up.x      = -_up.x;
            _forward.x = -_forward.x;
        }

        public void InterpolateTo(YUCPFingerNodeDescriptor to, float t)
        {
            Quaternion quatSlerp = Quaternion.Slerp(
                Quaternion.LookRotation(_forward, _up),
                Quaternion.LookRotation(to._forward, to._up),
                t);

            _right   = quatSlerp * Vector3.right;
            _up      = quatSlerp * Vector3.up;
            _forward = quatSlerp * Vector3.forward;
        }

        public bool Equals(YUCPFingerNodeDescriptor other)
        {
            float epsilon = 0.00001f;

            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    if (Mathf.Abs(_transformRelativeToHand[i, j] - other._transformRelativeToHand[i, j]) > epsilon)
                    {
                        return false;
                    }
                }
            }

            return _right == other._right && _up == other._up && _forward == other._forward;
        }
    }
}
