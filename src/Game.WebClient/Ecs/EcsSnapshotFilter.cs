using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ECS.Viewer;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Ecs {
	public enum ComponentFilterMode {
		Off,
		Require,
		Exclude
	}

	public enum NumberFilterMode {
		Exact,
		MinMax
	}

	public enum StringFilterMode {
		Exact,
		Contains,
		Regex
	}

	public sealed class FieldFilterRow {
		public string ComponentType { get; set; } = "";
		public string FieldName { get; set; } = "";
		public FieldSnapshotKind Kind { get; set; } = FieldSnapshotKind.String;
		public string Value { get; set; } = "";
		public NumberFilterMode NumberMode { get; set; } = NumberFilterMode.Exact;
		public string Min { get; set; } = "";
		public string Max { get; set; } = "";
		public StringFilterMode StringMode { get; set; } = StringFilterMode.Exact;
		public string? RegexError { get; set; }
	}

	public static class EcsSnapshotFilter {
		public static IReadOnlyList<EntitySnapshot> Apply(
			EcsWorldSnapshot? snapshot,
			IReadOnlyDictionary<string, ComponentFilterMode> chips,
			string entityIdSubstring,
			IReadOnlyList<FieldFilterRow> fieldRows
		) {
			var result = new List<EntitySnapshot>();
			if (snapshot == null || snapshot.Entities == null) {
				return result;
			}

			foreach (EntitySnapshot entity in snapshot.Entities) {
				if (!PassesChips(entity, chips)) {
					continue;
				}
				if (!PassesEntityId(entity, entityIdSubstring)) {
					continue;
				}
				if (!PassesFieldRows(entity, fieldRows)) {
					continue;
				}
				result.Add(entity);
			}
			return result;
		}

		public static bool PassesChips(EntitySnapshot entity, IReadOnlyDictionary<string, ComponentFilterMode> chips) {
			if (chips == null || chips.Count == 0) {
				return true;
			}
			var names = new HashSet<string>(StringComparer.Ordinal);
			if (entity.Components != null) {
				foreach (ComponentSnapshot component in entity.Components) {
					names.Add(component.TypeName);
				}
			}
			foreach (KeyValuePair<string, ComponentFilterMode> pair in chips) {
				if (pair.Value == ComponentFilterMode.Require && !names.Contains(pair.Key)) {
					return false;
				}
				if (pair.Value == ComponentFilterMode.Exclude && names.Contains(pair.Key)) {
					return false;
				}
			}
			return true;
		}

		public static bool PassesEntityId(EntitySnapshot entity, string substring) {
			if (string.IsNullOrEmpty(substring)) {
				return true;
			}
			return entity.Id.ToString(CultureInfo.InvariantCulture).Contains(substring);
		}

		public static bool PassesFieldRows(EntitySnapshot entity, IReadOnlyList<FieldFilterRow> rows) {
			if (rows == null || rows.Count == 0) {
				return true;
			}
			foreach (FieldFilterRow row in rows) {
				if (IsIncomplete(row)) {
					continue;
				}
				if (row.Kind == FieldSnapshotKind.String && row.StringMode == StringFilterMode.Regex) {
					try {
						_ = new Regex(row.Value);
						row.RegexError = null;
					} catch (ArgumentException ex) {
						row.RegexError = ex.Message;
						continue;
					}
				} else {
					row.RegexError = null;
				}
				if (!RowMatches(entity, row)) {
					return false;
				}
			}
			return true;
		}

		static bool IsIncomplete(FieldFilterRow row) {
			if (string.IsNullOrEmpty(row.ComponentType) || string.IsNullOrEmpty(row.FieldName)) {
				return true;
			}
			if (row.Kind == FieldSnapshotKind.Number && row.NumberMode == NumberFilterMode.MinMax) {
				return string.IsNullOrEmpty(row.Min) && string.IsNullOrEmpty(row.Max);
			}
			if (row.Kind == FieldSnapshotKind.Number && row.NumberMode == NumberFilterMode.Exact) {
				return string.IsNullOrEmpty(row.Value);
			}
			if (row.Kind == FieldSnapshotKind.Bool || row.Kind == FieldSnapshotKind.Enum || row.Kind == FieldSnapshotKind.String) {
				return string.IsNullOrEmpty(row.Value);
			}
			return false;
		}

		static bool RowMatches(EntitySnapshot entity, FieldFilterRow row) {
			foreach (ComponentSnapshot component in entity.Components) {
				if (!string.Equals(component.TypeName, row.ComponentType, StringComparison.Ordinal)) {
					continue;
				}
				foreach (FieldSnapshot field in component.Fields) {
					if (!string.Equals(field.Name, row.FieldName, StringComparison.Ordinal)) {
						continue;
					}
					return FieldMatches(field, row);
				}
			}
			return false;
		}

		static bool FieldMatches(FieldSnapshot field, FieldFilterRow row) {
			string raw = field.Value?.ToString() ?? "";
			switch (row.Kind) {
				case FieldSnapshotKind.Enum:
				case FieldSnapshotKind.Bool:
					return string.Equals(raw, row.Value, StringComparison.OrdinalIgnoreCase);
				case FieldSnapshotKind.Number:
					if (!TryParseNumber(raw, out double actual)) {
						return false;
					}
					if (row.NumberMode == NumberFilterMode.Exact) {
						return TryParseNumber(row.Value, out double exact) && actual == exact;
					}
					bool minOk = string.IsNullOrEmpty(row.Min) || (TryParseNumber(row.Min, out double min) && actual >= min);
					bool maxOk = string.IsNullOrEmpty(row.Max) || (TryParseNumber(row.Max, out double max) && actual <= max);
					return minOk && maxOk;
				default:
					if (row.StringMode == StringFilterMode.Exact) {
						return string.Equals(raw, row.Value, StringComparison.Ordinal);
					}
					if (row.StringMode == StringFilterMode.Contains) {
						return raw.IndexOf(row.Value, StringComparison.OrdinalIgnoreCase) >= 0;
					}
					try {
						return Regex.IsMatch(raw, row.Value);
					} catch (ArgumentException) {
						return true;
					}
			}
		}

		static bool TryParseNumber(string raw, out double value) {
			return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}
	}
}
