using System;
using System.Collections.Generic;

namespace GS.Game.WebClient.Terminal {
	// Dumb, string-only parse result: "Name key=value key2=value2 ...". Coercion of the raw
	// argument strings into actual field/property types happens later in CommandExecutor via
	// ValueCoercion - this type never inspects command shape, only tokenizes the input.
	public readonly struct ParsedCommandLine {
		public bool Success { get; }
		public string CommandName { get; }
		public IReadOnlyDictionary<string, string> Arguments { get; }
		public string? Error { get; }

		ParsedCommandLine(bool success, string commandName, IReadOnlyDictionary<string, string> arguments, string? error) {
			Success = success;
			CommandName = commandName;
			Arguments = arguments;
			Error = error;
		}

		public static ParsedCommandLine Ok(string commandName, IReadOnlyDictionary<string, string> arguments) {
			return new ParsedCommandLine(true, commandName, arguments, null);
		}

		public static ParsedCommandLine Fail(string error) {
			return new ParsedCommandLine(false, "", new Dictionary<string, string>(), error);
		}
	}

	public class TerminalParser {
		public ParsedCommandLine Parse(string input) {
			if (string.IsNullOrWhiteSpace(input)) {
				return ParsedCommandLine.Fail("Empty input.");
			}

			var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			string commandName = tokens[0];
			if (commandName.Contains('=')) {
				return ParsedCommandLine.Fail($"Invalid command name '{commandName}': command name must not contain '='.");
			}

			var arguments = new Dictionary<string, string>();
			for (int i = 1; i < tokens.Length; i++) {
				string token = tokens[i];
				int separatorIndex = token.IndexOf('=');
				if (separatorIndex < 0) {
					return ParsedCommandLine.Fail($"Malformed argument '{token}': expected 'key=value'.");
				}
				string key = token.Substring(0, separatorIndex);
				string value = token.Substring(separatorIndex + 1);
				if (string.IsNullOrEmpty(key)) {
					return ParsedCommandLine.Fail($"Malformed argument '{token}': argument name is empty.");
				}
				arguments[key] = value;
			}

			return ParsedCommandLine.Ok(commandName, arguments);
		}
	}
}
