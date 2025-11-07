using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
	/// <summary>
	/// FK curl-driven, mesh-aware finger solver.
	/// Produces natural joint-bounded rotations without tip look-at.
	/// </summary>
	public static class FingerCurlSolver
	{
		private static readonly Dictionary<Transform, Vector3> bendAxisLocalCache = new Dictionary<Transform, Vector3>();

		public static FingerTipSolver.FingerTipResult SolveFingerCurl(
			Animator animator,
			FingerTipSolver.FingerTipTarget targets,
			bool isLeftHand)
		{
			var result = new FingerTipSolver.FingerTipResult
			{
				solvedRotations = new Dictionary<HumanBodyBones, Quaternion>(),
				success = true,
				errorMessage = "",
				muscleValues = new Dictionary<string, float>()
			};

			Transform hand = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
			Transform middleDistal = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
			Vector3 palmCenter = (hand != null && middleDistal != null)
				? (hand.position + (middleDistal.position + middleDistal.forward * 0.02f)) * 0.5f
				: (hand != null ? hand.position : Vector3.zero);

			void SolveOne(string finger,
				HumanBodyBones prox, HumanBodyBones inter, HumanBodyBones dist,
				Vector3 tipTarget)
			{
				if (tipTarget == Vector3.zero) return;

				Transform tProx = animator.GetBoneTransform(prox);
				Transform tInter = animator.GetBoneTransform(inter);
				Transform tDist = animator.GetBoneTransform(dist);
				if (tProx == null || tInter == null || tDist == null) return;

				// Segment lengths
				float L1 = Vector3.Distance(tProx.position, tInter.position);
				float L2 = Vector3.Distance(tInter.position, tDist.position);
				float L3 = 0.018f; // tip pad length

				Vector3 basePos = tProx.position;
				float reach = Vector3.Distance(basePos, tipTarget);
				float straight = Mathf.Max(0.01f, L1 + L2 + L3);
				reach = Mathf.Clamp(reach, 0.02f, straight);

				// Binary search curl t in [0,1]
				float tLow = 0f, tHigh = 1f;
				for (int it = 0; it < 18; it++)
				{
					float t = 0.5f * (tLow + tHigh);
					float a1 = t * 100f * Mathf.Deg2Rad;
					float a2 = 0.7f * t * 100f * Mathf.Deg2Rad;
					float a3 = 0.66f * (0.7f * t * 100f) * Mathf.Deg2Rad;
					float d = L1 * Mathf.Cos(a1) + L2 * Mathf.Cos(a1 + a2) + L3 * Mathf.Cos(a1 + a2 + a3);
					if (d > reach) tLow = t; else tHigh = t;
				}
				float tFinal = tHigh;
				float deg1 = Mathf.Clamp01(tFinal) * 100f;
				float deg2 = 0.7f * deg1;
				float deg3 = 0.66f * deg2;

				// Calibrate bend axis once per joint by testing candidate local axes so that +angle rotates child toward target
				Vector3 AxisLocal(Transform joint, Transform child, Vector3 targetPos)
				{
					if (bendAxisLocalCache.TryGetValue(joint, out var cached)) return cached;
					Vector3 childRef = child != null ? child.position : (joint.position + joint.forward * 0.03f);
					Vector3[] baseAxes = new Vector3[] { Vector3.right, Vector3.up, Vector3.forward };
					float bestMag = -1f;
					Vector3 best = Vector3.right;
					for (int i = 0; i < baseAxes.Length; i++)
					{
						Vector3 axisW = joint.TransformDirection(baseAxes[i]);
						Vector3 from = childRef - joint.position;
						Vector3 to = targetPos - joint.position;
						Vector3 fromP = Vector3.ProjectOnPlane(from, axisW);
						Vector3 toP = Vector3.ProjectOnPlane(to, axisW);
						float signed = Vector3.SignedAngle(fromP, toP, axisW); // positive means rotate + toward target
						float mag = Mathf.Abs(signed);
						Vector3 localAxisSigned = signed >= 0f ? baseAxes[i] : -baseAxes[i];
						if (mag > bestMag)
						{
							bestMag = mag;
							best = localAxisSigned;
						}
					}
					bendAxisLocalCache[joint] = best;
					return best;
				}

				// Compute final world rotations from rest local rotations
				Quaternion proxRest = tProx.localRotation;
				Quaternion interRest = tInter.localRotation;
				Quaternion distRest = tDist.localRotation;

				Quaternion rProx = proxRest * Quaternion.AngleAxis(deg1, AxisLocal(tProx, tInter, tipTarget));
				Quaternion rInter = interRest * Quaternion.AngleAxis(deg2, AxisLocal(tInter, tDist, tipTarget));
				Quaternion rDist = distRest * Quaternion.AngleAxis(deg3, AxisLocal(tDist, null, tipTarget));

				HumanBodyBones bProx = prox;
				HumanBodyBones bInter = inter;
				HumanBodyBones bDist = dist;
				result.solvedRotations[bProx] = rProx;
				result.solvedRotations[bInter] = rInter;
				result.solvedRotations[bDist] = rDist;
			}

			if (isLeftHand)
			{
				SolveOne("Thumb", HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal, targets.thumbTip);
				SolveOne("Index", HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal, targets.indexTip);
				SolveOne("Middle", HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal, targets.middleTip);
				SolveOne("Ring", HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal, targets.ringTip);
				SolveOne("Little", HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal, targets.littleTip);
			}
			else
			{
				SolveOne("Thumb", HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal, targets.thumbTip);
				SolveOne("Index", HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal, targets.indexTip);
				SolveOne("Middle", HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal, targets.middleTip);
				SolveOne("Ring", HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal, targets.ringTip);
				SolveOne("Little", HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal, targets.littleTip);
			}

			return result;
		}
	}
}


