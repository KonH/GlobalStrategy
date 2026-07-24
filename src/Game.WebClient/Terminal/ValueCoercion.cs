using System;

namespace GS.Game.WebClient.Terminal {
	// Converts one raw string argument into the CLR type a command parameter actually needs.
	// Never throws - a bad value (e.g. "abc" for an int) is expected user-input error, not a
	// bug, so failures come back as a bool + friendly message for the caller to display.
	public static class ValueCoercion {
		public static bool TryCoerce(string raw, Type targetType, out object? value, out string? error) {
			if (targetType == typeof(string)) {
				value = raw;
				error = null;
				return true;
			}

			if (targetType == typeof(int)) {
				if (int.TryParse(raw, out var intValue)) {
					value = intValue;
					error = null;
					return true;
				}
				return Fail(out value, out error, raw, "int");
			}

			if (targetType == typeof(double)) {
				if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue)) {
					value = doubleValue;
					error = null;
					return true;
				}
				return Fail(out value, out error, raw, "double");
			}

			if (targetType == typeof(bool)) {
				// Accepts case-insensitive true/false, plus 1/0 as a convenience for a debug terminal.
				if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1") {
					value = true;
					error = null;
					return true;
				}
				if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0") {
					value = false;
					error = null;
					return true;
				}
				return Fail(out value, out error, raw, "bool");
			}

			if (targetType.IsEnum) {
				if (Enum.TryParse(targetType, raw, ignoreCase: true, out var enumValue) && Enum.IsDefined(targetType, enumValue!)) {
					value = enumValue;
					error = null;
					return true;
				}
				return Fail(out value, out error, raw, targetType.Name);
			}

			value = null;
			error = $"Unsupported argument type '{targetType.Name}'.";
			return false;
		}

		static bool Fail(out object? value, out string? error, string raw, string expectedTypeName) {
			value = null;
			error = $"Invalid value '{raw}' (expected {expectedTypeName}).";
			return false;
		}
	}
}
