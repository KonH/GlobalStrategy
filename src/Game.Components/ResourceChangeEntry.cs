using System;
using System.Collections.Generic;
using System.Globalization;

namespace GS.Game.Components {
	public struct ResourceChangeEntry {
		public string EffectId;
		public double AppliedDelta;
		public DateTime Timestamp;

		public static string Encode(IReadOnlyList<ResourceChangeEntry> entries) {
			if (entries == null || entries.Count == 0) {
				return "";
			}
			var parts = new string[entries.Count];
			for (int i = 0; i < entries.Count; i++) {
				ResourceChangeEntry entry = entries[i];
				parts[i] = string.Format(
					CultureInfo.InvariantCulture,
					"{0}\x1E{1:R}\x1E{2:O}",
					entry.EffectId,
					entry.AppliedDelta,
					entry.Timestamp);
			}
			return string.Join("\x1F", parts);
		}

		public static List<ResourceChangeEntry> Decode(string encoded) {
			if (string.IsNullOrEmpty(encoded)) {
				return new List<ResourceChangeEntry>();
			}
			var result = new List<ResourceChangeEntry>();
			foreach (string part in encoded.Split('\x1F')) {
				string[] fields = part.Split('\x1E');
				if (fields.Length != 3) {
					throw new FormatException(
						$"Malformed ResourceChangeEntry segment '{part}' (expected 3 fields, got {fields.Length}).");
				}
				result.Add(new ResourceChangeEntry {
					EffectId = fields[0],
					AppliedDelta = double.Parse(fields[1], CultureInfo.InvariantCulture),
					Timestamp = DateTime.ParseExact(fields[2], "O", CultureInfo.InvariantCulture)
				});
			}
			return result;
		}
	}
}
