using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
	public interface IDistanceField
	{
		float SignedDistance(Vector3 worldP, out Vector3 outwardNormal);
	}

	/// <summary>
	/// Simple distance approximation using overlap and short raycasts.
	/// Positive outside, negative inside. Returns an approximate outward normal.
	/// </summary>
	public class RaycastDistanceField : IDistanceField
	{
		private readonly LayerMask layerMask;
		private readonly float probeRadius;
		private readonly float rayLength;

		public RaycastDistanceField(LayerMask mask, float probeRadius = 0.002f, float rayLength = 0.05f)
		{
			this.layerMask = mask;
			this.probeRadius = Mathf.Max(0.0005f, probeRadius);
			this.rayLength = Mathf.Max(0.005f, rayLength);
		}

		public float SignedDistance(Vector3 x, out Vector3 n)
		{
			n = Vector3.up;
			// Inside check via overlap
			var hits = Physics.OverlapSphere(x, probeRadius, layerMask, QueryTriggerInteraction.Ignore);
			if (hits != null && hits.Length > 0)
			{
				Vector3 cp = hits[0].ClosestPoint(x);
				n = (x - cp).sqrMagnitude > 1e-8f ? (x - cp).normalized : Vector3.up;
				return -Vector3.Distance(x, cp);
			}

			// Outside: sample a few short rays to estimate distance/normal
			Vector3[] dirs = new Vector3[]
			{
				Vector3.down,
				Vector3.up,
				Vector3.forward,
				Vector3.back,
				Vector3.left,
				Vector3.right
			};
			float best = float.MaxValue;
			Vector3 bestN = Vector3.up;
			for (int i = 0; i < dirs.Length; i++)
			{
				if (Physics.Raycast(x, dirs[i], out var rh, rayLength, layerMask, QueryTriggerInteraction.Ignore))
				{
					if (rh.distance < best)
					{
						best = rh.distance;
						bestN = rh.normal;
					}
				}
			}
			if (best < float.MaxValue)
			{
				n = bestN;
				return best;
			}

			return rayLength;
		}
	}

	/// <summary>
	/// Combines multiple distance fields; returns the minimum signed distance and its normal.
	/// </summary>
	public class CombinedDistanceField : IDistanceField
	{
		private readonly System.Collections.Generic.List<IDistanceField> fields = new System.Collections.Generic.List<IDistanceField>();

		public CombinedDistanceField(params IDistanceField[] fs)
		{
			if (fs != null) fields.AddRange(fs);
		}

		public void Add(IDistanceField f)
		{
			if (f != null) fields.Add(f);
		}

		public float SignedDistance(Vector3 worldP, out Vector3 outwardNormal)
		{
			outwardNormal = Vector3.up;
			if (fields.Count == 0) return 0.05f;
			float best = float.MaxValue; Vector3 bestN = Vector3.up;
			for (int i = 0; i < fields.Count; i++)
			{
				Vector3 n;
				float d = fields[i].SignedDistance(worldP, out n);
				if (d < best)
				{
					best = d; bestN = n;
				}
			}
			outwardNormal = bestN;
			return best;
		}
	}
}


