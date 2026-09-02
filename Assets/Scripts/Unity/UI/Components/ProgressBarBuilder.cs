using System;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// ProgressBar atom - a track plus a percentage-width fill. Replaces the hand-built
	/// goals-progress-track/goals-progress-fill pair (GoalsWindow.uss); also used by WarProgress.
	/// </summary>
	public static class ProgressBarBuilder {
		public struct Elements {
			public VisualElement Track;
			public VisualElement Fill;
		}

		public static Elements Build() {
			var track = new VisualElement();
			track.AddToClassList("progress-bar-track");

			var fill = new VisualElement();
			fill.AddToClassList("progress-bar-fill");
			track.Add(fill);

			return new Elements { Track = track, Fill = fill };
		}

		/// <summary>fraction is clamped to [0, 1].</summary>
		public static void Bind(Elements elements, float fraction) {
			float clamped = Math.Clamp(fraction, 0f, 1f);
			elements.Fill.style.width = new Length(clamped * 100f, LengthUnit.Percent);
		}
	}
}
