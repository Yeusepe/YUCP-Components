using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor
{
    internal static class ClosestBoneFinder
    {
        internal static Transform Find(Animator animator, Transform attachment, float maxDistance,
            bool ignoreHumanoidBones, string includeName, string excludeName = null)
        {
            var humanoidBones = new HashSet<Transform>();
            if (ignoreHumanoidBones && animator.isHuman)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    humanoidBones.Add(animator.GetBoneTransform((HumanBodyBones)i));
            }

            Transform closest = null;
            float closestDistance = float.PositiveInfinity;
            float maxDistanceSquared = maxDistance * maxDistance;
            Vector3 position = attachment.position;
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (bone.IsChildOf(attachment) || bone.GetComponent<Animator>() != null || humanoidBones.Contains(bone))
                    continue;
                if (!string.IsNullOrEmpty(includeName) && bone.name.IndexOf(includeName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!string.IsNullOrEmpty(excludeName) && bone.name.IndexOf(excludeName, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                float distance = (position - bone.position).sqrMagnitude;
                if (maxDistance > 0 && distance > maxDistanceSquared)
                    continue;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = bone;
                }
            }

            return closest;
        }
    }
}
