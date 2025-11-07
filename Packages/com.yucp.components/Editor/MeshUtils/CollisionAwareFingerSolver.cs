using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
	public static class CollisionAwareFingerSolver
	{
		private static readonly Dictionary<Transform, Quaternion> s_BindLocal = new Dictionary<Transform, Quaternion>();

		private static Quaternion GetBindLocal(Transform t)
		{
			if (t == null) return Quaternion.identity;
			if (!s_BindLocal.TryGetValue(t, out var q))
			{
				q = t.localRotation;
				s_BindLocal[t] = q;
			}
			return q;
		}
		[System.Serializable]
		public struct FingerLimits
		{
			public float mcpFlexMin, mcpFlexMax;
			public float mcpAbdMin, mcpAbdMax; // ab/adduction (horizontal spread)
			public float pipMin, pipMax;
			public float dipMin, dipMax;
			public float dipFromPip;

			public static FingerLimits Defaults()
			{
				return new FingerLimits
				{
					mcpFlexMin = 0f, mcpFlexMax = 90f,
					mcpAbdMin = -20f, mcpAbdMax = 20f,
					pipMin = 0f, pipMax = 110f,
					dipMin = 0f, dipMax = 80f,
					dipFromPip = 0.66f
				};
			}
		}

		[System.Serializable]
		public struct Capsule
		{
			public Transform bone; // local space
			public Vector3 p0Local;
			public Vector3 p1Local;
			public float radius;
		}

		public static bool CloseFinger(
			Transform mcp, Transform pip, Transform dip,
			Capsule[] phalanxCapsules,
			FingerLimits lim,
			IDistanceField df,
			float gap,
			float safetyPadding,
			int maxIters = 40)
		{
			float mcpF = 0f, pipF = 0f, dipF = 0f, mcpAbd = 0f; // radians
			for (int it = 0; it < maxIters; it++)
			{
				float step = Mathf.Deg2Rad * 2.5f;
				pipF = Mathf.Min(pipF + step, lim.pipMax * Mathf.Deg2Rad);
				dipF = Mathf.Clamp(lim.dipFromPip * pipF, lim.dipMin * Mathf.Deg2Rad, lim.dipMax * Mathf.Deg2Rad);
				mcpF = Mathf.Min(mcpF + step * 0.5f, lim.mcpFlexMax * Mathf.Deg2Rad);
				// Constrain ab/adduction to realistic limits
				mcpAbd = Mathf.Clamp(mcpAbd, lim.mcpAbdMin * Mathf.Deg2Rad, lim.mcpAbdMax * Mathf.Deg2Rad);

				ApplyLocalFlex(mcp, pip, dip, mcpF, pipF, dipF, mcpAbd);

				float worstPen = 0f; Vector3 worstN = Vector3.zero;
				foreach (var cap in phalanxCapsules)
				{
					foreach (var p in SampleCapsuleWorldPoints(cap))
					{
						float sd = df.SignedDistance(p, out var nrm) - (cap.radius + Mathf.Max(0f, safetyPadding));
						if (sd < -worstPen) { worstPen = -sd; worstN = nrm; }
					}
				}

				if (worstPen > 0f)
				{
					const float dead = 0.0003f; // 0.3 mm dead-zone
					const float kPush = 0.5f;    // 50% push-out along normal
					float back = Mathf.Min(step, worstPen * 15f);
					pipF = Mathf.Max(lim.pipMin * Mathf.Deg2Rad, pipF - back);
					dipF = Mathf.Clamp(lim.dipFromPip * pipF, lim.dipMin * Mathf.Deg2Rad, lim.dipMax * Mathf.Deg2Rad);
					ApplyLocalFlex(mcp, pip, dip, mcpF, pipF, dipF, mcpAbd);
					if (worstPen > dead && worstN.sqrMagnitude > 1e-8f)
					{
						// Micro-translate MCP outward to build clearance
						mcp.position += worstN.normalized * (kPush * worstPen);
					}
				}

				if (TipSatisfied(dip, gap, df) && worstPen <= 1e-4f) return true;
			}
			return false;
		}

		private static void ApplyLocalFlex(Transform mcp, Transform pip, Transform dip, float mcpF, float pipF, float dipF, float mcpAbduction = 0f)
		{
			// Compose against bind-local using stable local axes
			Quaternion bindM = GetBindLocal(mcp);
			Quaternion bindP = GetBindLocal(pip);
			Quaternion bindD = GetBindLocal(dip);
			
			Quaternion yawQ = Quaternion.AngleAxis(mcpAbduction * Mathf.Rad2Deg, Vector3.forward); // local Z = ab/adduction
			Quaternion flexM = Quaternion.AngleAxis(mcpF * Mathf.Rad2Deg, Vector3.right); // local X = flex
			mcp.localRotation = bindM * yawQ * flexM;
			
			Quaternion flexP = Quaternion.AngleAxis(pipF * Mathf.Rad2Deg, Vector3.right);
			Quaternion flexD = Quaternion.AngleAxis(dipF * Mathf.Rad2Deg, Vector3.right);
			pip.localRotation = bindP * flexP;
			dip.localRotation = bindD * flexD;
		}

		private static bool TipSatisfied(Transform dip, float gap, IDistanceField df)
		{
			float sd = df.SignedDistance(dip.position, out _);
			return sd <= gap && sd >= -1e-4f;
		}

		private static IEnumerable<Vector3> SampleCapsuleWorldPoints(Capsule c)
		{
			for (int i = 0; i < 5; i++)
			{
				float t = i / 4f;
				Vector3 pL = Vector3.Lerp(c.p0Local, c.p1Local, t);
				yield return c.bone.TransformPoint(pL);
			}
		}
	}
}


