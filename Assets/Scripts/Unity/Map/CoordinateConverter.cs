using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public static class CoordinateConverter {
		public const float Scale = 3f;
		public const float MapWidth = 360f * Scale;
		public const float MapHeight = 180f * Scale;

		public static Vector2 ToWorld(Vector2d coord) =>
			new Vector2((float)coord.Lon * Scale, (float)coord.Lat * Scale);
	}
}
