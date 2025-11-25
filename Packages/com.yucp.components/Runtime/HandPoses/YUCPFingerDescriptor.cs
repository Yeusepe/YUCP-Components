// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Manipulation/HandPoses/UxrFingerDescriptor.cs
// Adapted for YUCP Components – Works with Unity HumanBodyBones

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YUCP.Components.HandPoses
{
    [Serializable]
    public struct YUCPFingerDescriptor
    {
        [SerializeField] private bool                    _hasMetacarpalInfo;
        [SerializeField] private YUCPFingerNodeDescriptor _metacarpal;
        [SerializeField] private YUCPFingerNodeDescriptor _proximal;
        [SerializeField] private YUCPFingerNodeDescriptor _proximalNoMetacarpal;
        [SerializeField] private YUCPFingerNodeDescriptor _intermediate;
        [SerializeField] private YUCPFingerNodeDescriptor _distal;

        public bool HasMetacarpalInfo => _hasMetacarpalInfo;
        public YUCPFingerNodeDescriptor Metacarpal => _metacarpal;
        public YUCPFingerNodeDescriptor Proximal => _proximal;
        public YUCPFingerNodeDescriptor ProximalNoMetacarpal => _proximalNoMetacarpal;
        public YUCPFingerNodeDescriptor Intermediate => _intermediate;
        public YUCPFingerNodeDescriptor Distal => _distal;

        public void Compute(
            Transform wrist,
            Transform metacarpal,
            Transform proximal,
            Transform intermediate,
            Transform distal,
            YUCPUniversalLocalAxes handLocalAxes,
            YUCPUniversalLocalAxes fingerLocalAxes,
            bool computeRelativeMatrixOnly)
        {
            if (metacarpal != null)
            {
                _hasMetacarpalInfo = true;
                _metacarpal.Compute(wrist, wrist, metacarpal, handLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
                _proximal.Compute(wrist, metacarpal, proximal, fingerLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
                _proximalNoMetacarpal.Compute(wrist, wrist, proximal, handLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
            }
            else
            {
                _hasMetacarpalInfo = false;
                _proximal.Compute(wrist, wrist, proximal, handLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
                _proximalNoMetacarpal.Compute(wrist, wrist, proximal, handLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
            }

            if (intermediate != null)
            {
                _intermediate.Compute(wrist, proximal, intermediate, fingerLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
            }

            if (distal != null && intermediate != null)
            {
                _distal.Compute(wrist, intermediate, distal, fingerLocalAxes, fingerLocalAxes, computeRelativeMatrixOnly);
            }
        }

        public void Mirror()
        {
            if (_hasMetacarpalInfo)
            {
                _metacarpal.Mirror();
            }

            _proximal.Mirror();
            _proximalNoMetacarpal.Mirror();
            _intermediate.Mirror();
            _distal.Mirror();
        }

        public void InterpolateTo(YUCPFingerDescriptor to, float t)
        {
            if (_hasMetacarpalInfo)
            {
                _metacarpal.InterpolateTo(to._metacarpal, t);
            }

            _proximal.InterpolateTo(to._proximal, t);
            _proximalNoMetacarpal.InterpolateTo(to._proximalNoMetacarpal, t);
            _intermediate.InterpolateTo(to._intermediate, t);
            _distal.InterpolateTo(to._distal, t);
        }

#if UNITY_EDITOR
        public void DrawEditorDebugLabels(string prefix)
        {
            EditorGUILayout.LabelField(prefix + _proximal.Right);
            EditorGUILayout.LabelField(prefix + _proximal.Up);
            EditorGUILayout.LabelField(prefix + _proximal.Forward);
        }
#endif

        public bool Equals(YUCPFingerDescriptor other)
        {
            if (_hasMetacarpalInfo != other._hasMetacarpalInfo)
            {
                return false;
            }

            if (_hasMetacarpalInfo)
            {
                return _metacarpal.Equals(other._metacarpal)
                       && _proximal.Equals(other._proximal)
                       && _intermediate.Equals(other._intermediate)
                       && _distal.Equals(other._distal);
            }

            return _proximal.Equals(other._proximal)
                   && _intermediate.Equals(other._intermediate)
                   && _distal.Equals(other._distal);
        }
    }
}
