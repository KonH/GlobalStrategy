using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GS.Core.Map;

namespace GS.Unity.Map {
	public static class MapMeshBuilder {
		const int MaxRingVerts = 500;

		public static Mesh BuildFeatureMesh(MapFeature feature) {
			var allVertices = new List<Vector3>();
			var allTriangles = new List<int>();

			foreach (var polygon in feature.Polygons) {
				if (polygon.Rings.Count == 0) continue;
				AppendRingMesh(polygon.Rings[0], allVertices, allTriangles);
			}

			if (allVertices.Count == 0) return null;

			var mesh = new Mesh();
			mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(allVertices);
			mesh.SetTriangles(allTriangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		static void AppendRingMesh(Ring ring, List<Vector3> vertices, List<int> triangles) {
			var points = ring.Points;
			int count = points.Count;

			// Remove closing point if ring is closed
			if (count > 1) {
				var first = points[0];
				var last = points[count - 1];
				if (Math.Abs(first.Lon - last.Lon) < 1e-9 && Math.Abs(first.Lat - last.Lat) < 1e-9)
					count--;
			}
			if (count < 3) return;

			// Unwrap longitude: remove antimeridian jumps so the ring stays continuous.
			// A >180° jump means the segment crossed ±180° — shift subsequent points.
			var unwrappedLon = new double[count];
			unwrappedLon[0] = points[0].Lon;
			for (int i = 1; i < count; i++) {
				double delta = points[i].Lon - unwrappedLon[i - 1];
				if (delta > 180.0) delta -= 360.0;
				else if (delta < -180.0) delta += 360.0;
				unwrappedLon[i] = unwrappedLon[i - 1] + delta;
			}

			// Re-center ring if unwrapping drifted its centroid outside [-180, 180]
			double lonSum = 0;
			for (int i = 0; i < count; i++) lonSum += unwrappedLon[i];
			double lonShift = Math.Round(lonSum / count / 360.0) * 360.0;
			if (Math.Abs(lonShift) > 0.5) {
				for (int i = 0; i < count; i++) unwrappedLon[i] -= lonShift;
			}

			// Subsample if too dense for the prototype triangulator
			Vector2[] verts;
			if (count > MaxRingVerts) {
				verts = new Vector2[MaxRingVerts];
				float step = count / (float)MaxRingVerts;
				for (int i = 0; i < MaxRingVerts; i++) {
					int idx = (int)(i * step);
					verts[i] = CoordinateConverter.ToWorld(new Vector2d(unwrappedLon[idx], points[idx].Lat));
				}
			} else {
				verts = new Vector2[count];
				for (int i = 0; i < count; i++)
					verts[i] = CoordinateConverter.ToWorld(new Vector2d(unwrappedLon[i], points[i].Lat));
			}

			int baseIndex = vertices.Count;
			foreach (var v in verts)
				vertices.Add(new Vector3(v.x, v.y, 0f));

			var tris = Triangulate(verts);
			foreach (var t in tris)
				triangles.Add(baseIndex + t);
		}

		static int[] Triangulate(Vector2[] pts) {
			var result = new List<int>();
			var indices = new List<int>(pts.Length);
			for (int i = 0; i < pts.Length; i++) indices.Add(i);

			// Ensure CCW winding for correct ear detection
			if (SignedArea(pts) < 0) indices.Reverse();

			int safety = indices.Count * indices.Count + indices.Count;
			int cur = 0;
			while (indices.Count > 3 && safety-- > 0) {
				int n = indices.Count;
				int prev = (cur - 1 + n) % n;
				int next = (cur + 1) % n;
				if (IsEar(pts, indices, prev, cur, next)) {
					result.Add(indices[prev]);
					result.Add(indices[cur]);
					result.Add(indices[next]);
					indices.RemoveAt(cur);
					if (cur >= indices.Count) cur = 0;
				} else {
					cur = (cur + 1) % indices.Count;
				}
			}
			if (indices.Count == 3) {
				result.Add(indices[0]);
				result.Add(indices[1]);
				result.Add(indices[2]);
			}
			return result.ToArray();
		}

		static float SignedArea(Vector2[] pts) {
			float area = 0;
			int n = pts.Length;
			for (int i = 0; i < n; i++) {
				var a = pts[i];
				var b = pts[(i + 1) % n];
				area += a.x * b.y - b.x * a.y;
			}
			return area;
		}

		static bool IsEar(Vector2[] pts, List<int> idx, int prev, int cur, int next) {
			var a = pts[idx[prev]];
			var b = pts[idx[cur]];
			var c = pts[idx[next]];
			if (Cross2D(a, b, c) <= 0) return false;
			for (int i = 0; i < idx.Count; i++) {
				if (i == prev || i == cur || i == next) continue;
				if (InsideTriangle(pts[idx[i]], a, b, c)) return false;
			}
			return true;
		}

		static float Cross2D(Vector2 a, Vector2 b, Vector2 c) =>
			(b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

		static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) {
			float d1 = Cross2D(a, b, p);
			float d2 = Cross2D(b, c, p);
			float d3 = Cross2D(c, a, p);
			bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
			bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
			return !(hasNeg && hasPos);
		}
	}
}
