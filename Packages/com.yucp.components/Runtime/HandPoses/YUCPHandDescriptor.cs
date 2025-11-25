// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Manipulation/HandPoses/UxrHandDescriptor.cs
// Adapted for YUCP Components – Works with Unity HumanBodyBones

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YUCP.Components.HandPoses
{
    [Serializable]
    public class YUCPHandDescriptor
    {
        [SerializeField] private YUCPFingerDescriptor _index;
        [SerializeField] private YUCPFingerDescriptor _middle;
        [SerializeField] private YUCPFingerDescriptor _ring;
        [SerializeField] private YUCPFingerDescriptor _little;
        [SerializeField] private YUCPFingerDescriptor _thumb;

        public YUCPFingerDescriptor Index => _index;
        public YUCPFingerDescriptor Middle => _middle;
        public YUCPFingerDescriptor Ring => _ring;
        public YUCPFingerDescriptor Little => _little;
        public YUCPFingerDescriptor Thumb => _thumb;

        public YUCPHandDescriptor()
        {
            _index  = new YUCPFingerDescriptor();
            _middle = new YUCPFingerDescriptor();
            _ring   = new YUCPFingerDescriptor();
            _little = new YUCPFingerDescriptor();
            _thumb  = new YUCPFingerDescriptor();
        }

        public YUCPFingerDescriptor GetFinger(YUCPFingerType fingerType)
        {
            switch (fingerType)
            {
                case YUCPFingerType.Thumb:  return Thumb;
                case YUCPFingerType.Index:  return Index;
                case YUCPFingerType.Middle: return Middle;
                case YUCPFingerType.Ring:   return Ring;
                case YUCPFingerType.Little: return Little;
                default:                    throw new ArgumentOutOfRangeException(nameof(fingerType), fingerType, null);
            }
        }

        public void SetFinger(YUCPFingerType fingerType, YUCPFingerDescriptor descriptor)
        {
            switch (fingerType)
            {
                case YUCPFingerType.Thumb:
                    _thumb = descriptor;
                    break;
                case YUCPFingerType.Index:
                    _index = descriptor;
                    break;
                case YUCPFingerType.Middle:
                    _middle = descriptor;
                    break;
                case YUCPFingerType.Ring:
                    _ring = descriptor;
                    break;
                case YUCPFingerType.Little:
                    _little = descriptor;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fingerType), fingerType, null);
            }
        }

        public void CopyFrom(YUCPHandDescriptor src)
        {
            if (src == null)
            {
                return;
            }

            _index  = src._index;
            _middle = src._middle;
            _ring   = src._ring;
            _little = src._little;
            _thumb  = src._thumb;
        }

        public void InterpolateTo(YUCPHandDescriptor to, float t)
        {
            if (to == null)
            {
                return;
            }

            _index.InterpolateTo(to._index, t);
            _middle.InterpolateTo(to._middle, t);
            _ring.InterpolateTo(to._ring, t);
            _little.InterpolateTo(to._little, t);
            _thumb.InterpolateTo(to._thumb, t);
        }

#if UNITY_EDITOR
        public void DrawEditorDebugLabels()
        {
            _index.DrawEditorDebugLabels("index: ");
            _middle.DrawEditorDebugLabels("middle: ");
            _ring.DrawEditorDebugLabels("ring: ");
            _little.DrawEditorDebugLabels("little: ");
            _thumb.DrawEditorDebugLabels("thumb: ");
        }
#endif

        public YUCPHandDescriptor Mirrored()
        {
            var mirrored = new YUCPHandDescriptor();
            mirrored.CopyFrom(this);

            mirrored._index.Mirror();
            mirrored._middle.Mirror();
            mirrored._ring.Mirror();
            mirrored._little.Mirror();
            mirrored._thumb.Mirror();

            return mirrored;
        }

        public bool Equals(YUCPHandDescriptor other)
        {
            if (other == null)
            {
                return false;
            }

            return _index.Equals(other._index)
                   && _middle.Equals(other._middle)
                   && _ring.Equals(other._ring)
                   && _little.Equals(other._little)
                   && _thumb.Equals(other._thumb);
        }
    }
}
