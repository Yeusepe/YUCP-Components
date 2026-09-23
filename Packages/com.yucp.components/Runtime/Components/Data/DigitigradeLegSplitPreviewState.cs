using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Records everything a scene preview changed, so it can be undone without relying on Unity's
    /// undo stack.
    ///
    /// The undo stack is wiped by entering Play Mode and by every domain reload. A preview that
    /// depends on it silently becomes unrevertable, and applying a second preview on top then leaves
    /// destroyed bones referenced by the skins -- which collapses the mesh, because vertices weighted
    /// to a null bone snap to the origin. Serialising the record on the avatar survives both.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class DigitigradeLegSplitPreviewState : MonoBehaviour, IEditorOnly
    {
        [Serializable]
        public class SkinRecord
        {
            public SkinnedMeshRenderer skin;
            public Mesh originalMesh;
            public Transform[] originalBones;
        }

        [Serializable]
        public class MoveRecord
        {
            public Transform bone;
            public Transform originalParent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        public List<GameObject> createdBones = new List<GameObject>();
        public List<SkinRecord> skins = new List<SkinRecord>();
        public List<MoveRecord> movedBones = new List<MoveRecord>();

        public Animator animator;
        public Avatar originalAvatar;

        public void RecordSkin(SkinnedMeshRenderer skin)
        {
            if (skin == null) return;
            foreach (var existing in skins)
            {
                if (existing.skin == skin) return;   // first record wins -- it holds the pristine state
            }

            skins.Add(new SkinRecord
            {
                skin = skin,
                originalMesh = skin.sharedMesh,
                originalBones = (Transform[])skin.bones.Clone()
            });
        }

        public void RecordMove(Transform bone)
        {
            if (bone == null) return;
            foreach (var existing in movedBones)
            {
                if (existing.bone == bone) return;
            }

            movedBones.Add(new MoveRecord
            {
                bone = bone,
                originalParent = bone.parent,
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale
            });
        }
    }
}
