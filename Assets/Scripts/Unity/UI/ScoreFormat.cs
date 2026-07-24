using System;
using System.Globalization;

namespace GS.Unity.UI {
	public static class ScoreFormat {
		static readonly NumberFormatInfo s_scoreFormat = new NumberFormatInfo {
			NumberGroupSeparator = " ",
			NumberGroupSizes = new[] { 3 }
		};

		public static string Format(double value) {
			return Math.Round(value, MidpointRounding.AwayFromZero).ToString("#,0", s_scoreFormat);
		}
	}
}
