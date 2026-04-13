using UnityEngine;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Renderer))]
	public class MapImageOverlay : MonoBehaviour {
		public void Setup(Texture2D texture) {
			var mat = new Material(Shader.Find("Unlit/Texture"));
			mat.mainTexture = texture;
			GetComponent<Renderer>().material = mat;
			transform.localScale = new Vector3(CoordinateConverter.MapWidth, CoordinateConverter.MapHeight, 1f);
		}

		public void Setup(Texture2D[] tiles, int cols, int rows) {
			GetComponent<Renderer>().enabled = false;

			float tileWidth = CoordinateConverter.MapWidth / cols;
			float tileHeight = CoordinateConverter.MapHeight / rows;

			for (int r = 0; r < rows; r++) {
				for (int c = 0; c < cols; c++) {
					var tile = tiles[r * cols + c];
					if (tile == null) {
						continue;
					}

					var go = new GameObject($"tile_r{r}_c{c}");
					go.transform.SetParent(transform, false);

					float x = -CoordinateConverter.MapWidth / 2f + (c + 0.5f) * tileWidth;
					float y = CoordinateConverter.MapHeight / 2f - (r + 0.5f) * tileHeight;
					go.transform.localPosition = new Vector3(x, y, 0f);
					go.transform.localScale = new Vector3(tileWidth, tileHeight, 1f);

					go.AddComponent<MeshFilter>().mesh = CreateQuadMesh();

					var mat = new Material(Shader.Find("Unlit/Texture"));
					mat.mainTexture = tile;
					go.AddComponent<MeshRenderer>().material = mat;
				}
			}
		}

		static Mesh CreateQuadMesh() {
			var mesh = new Mesh();
			mesh.vertices = new Vector3[] {
				new Vector3(-0.5f, -0.5f, 0f),
				new Vector3( 0.5f, -0.5f, 0f),
				new Vector3(-0.5f,  0.5f, 0f),
				new Vector3( 0.5f,  0.5f, 0f),
			};
			mesh.uv = new Vector2[] {
				new Vector2(0, 0),
				new Vector2(1, 0),
				new Vector2(0, 1),
				new Vector2(1, 1),
			};
			mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
			mesh.RecalculateNormals();
			return mesh;
		}
	}
}
