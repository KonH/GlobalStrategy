using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GS.Game.WebClient.LocaleTool {
	// Line-parses the flat "Entries: [{Key, Value}]" list of a Unity-YAML
	// Assets/Localization/{locale}.asset file. This is intentionally not a general
	// YAML parser - the asset shape is a fixed two-line-per-entry list
	// ("  - Key: <key>" then "    Value: <value>"), so plain string parsing is enough.
	public static class LocaleAssetParser {
		const string KeyPrefix = "- Key:";
		const string ValuePrefix = "Value:";

		public static Dictionary<string, string> Parse(string assetFileContent) {
			var result = new Dictionary<string, string>();
			if (string.IsNullOrEmpty(assetFileContent)) {
				return result;
			}

			string[] lines = assetFileContent.Replace("\r\n", "\n").Split('\n');
			string? pendingKey = null;
			foreach (string rawLine in lines) {
				string line = rawLine.TrimStart();
				if (line.StartsWith(KeyPrefix, StringComparison.Ordinal)) {
					string key = line.Substring(KeyPrefix.Length).Trim();
					pendingKey = key.Length > 0 ? key : null;
					continue;
				}
				if (line.StartsWith(ValuePrefix, StringComparison.Ordinal) && pendingKey != null) {
					string rawValue = line.Substring(ValuePrefix.Length).Trim();
					result[pendingKey] = Unquote(rawValue);
					pendingKey = null;
				}
			}
			return result;
		}

		static string Unquote(string rawValue) {
			if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[rawValue.Length - 1] == '"') {
				return UnescapeDoubleQuoted(rawValue.Substring(1, rawValue.Length - 2));
			}
			if (rawValue.Length >= 2 && rawValue[0] == '\'' && rawValue[rawValue.Length - 1] == '\'') {
				return rawValue.Substring(1, rawValue.Length - 2).Replace("''", "'");
			}
			return rawValue;
		}

		static string UnescapeDoubleQuoted(string value) {
			var builder = new StringBuilder(value.Length);
			int i = 0;
			while (i < value.Length) {
				char c = value[i];
				if (c == '\\' && i + 1 < value.Length) {
					char next = value[i + 1];
					switch (next) {
						case 'n':
							builder.Append('\n');
							i += 2;
							break;
						case 't':
							builder.Append('\t');
							i += 2;
							break;
						case 'r':
							builder.Append('\r');
							i += 2;
							break;
						case '"':
							builder.Append('"');
							i += 2;
							break;
						case '\\':
							builder.Append('\\');
							i += 2;
							break;
						case 'u':
							if (i + 5 < value.Length && ushort.TryParse(
								value.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code)) {
								builder.Append((char)code);
								i += 6;
							} else {
								builder.Append(c);
								i++;
							}
							break;
						default:
							builder.Append(c);
							i++;
							break;
					}
				} else {
					builder.Append(c);
					i++;
				}
			}
			return builder.ToString();
		}
	}
}
