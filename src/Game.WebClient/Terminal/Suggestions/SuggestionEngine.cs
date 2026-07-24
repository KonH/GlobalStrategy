using System;
using System.Collections.Generic;
using System.Linq;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public enum SuggestionKind {
		None,
		CommandName,
		ArgumentName,
		ArgumentValue
	}

	// Result of classifying "what is being typed right now" at the end of the input (this
	// terminal is single-line with the cursor always at the end - no mid-string cursor support).
	public sealed class SuggestionResult {
		readonly int _tokenStart;
		readonly bool _insertLeadingSpace;

		public SuggestionKind Kind { get; }
		public IReadOnlyList<SuggestionItem> Items { get; }
		public bool IsSingleMatch => Items.Count == 1;

		public static SuggestionResult None { get; } = new(SuggestionKind.None, Array.Empty<SuggestionItem>(), 0);

		internal SuggestionResult(SuggestionKind kind, IReadOnlyList<SuggestionItem> items, int tokenStart, bool insertLeadingSpace = false) {
			Kind = kind;
			Items = items;
			_tokenStart = tokenStart;
			_insertLeadingSpace = insertLeadingSpace;
		}

		// Builds the completed input text by replacing the in-progress token with the chosen
		// suggestion's raw Value. Argument names additionally get a trailing '=' so the next Tab
		// press lands straight into the value-suggestion phase, per the spec's Tab scenarios.
		// insertLeadingSpace covers the one case where _tokenStart sits right after a command
		// name that has no real trailing space in the input yet (the "already-complete command
		// name" fallthrough below) - without it the argument name would glue directly onto the
		// command name with no separator.
		public string Complete(string input, string chosenValue) {
			string prefix = input.Substring(0, _tokenStart);
			if (_insertLeadingSpace) {
				prefix += " ";
			}
			return Kind == SuggestionKind.ArgumentName ? prefix + chosenValue + "=" : prefix + chosenValue;
		}
	}

	// Classifies the terminal's current input into "what Tab should complete" and resolves the
	// matching candidates - command names, argument names, or labeled argument values - never
	// throwing on garbage input (unknown command/argument => SuggestionResult.None).
	public class SuggestionEngine {
		readonly CommandRegistry _registry;
		readonly SuggestionValueResolver _valueResolver;

		public SuggestionEngine(CommandRegistry registry, SuggestionValueResolver valueResolver) {
			_registry = registry;
			_valueResolver = valueResolver;
		}

		public SuggestionResult GetSuggestions(string input) {
			input ??= "";
			bool endsWithWhitespace = input.Length > 0 && char.IsWhiteSpace(input[input.Length - 1]);
			var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

			if (tokens.Length == 0) {
				return CommandNameSuggestions("", input.Length);
			}

			bool needsSyntheticSeparator = false;
			if (tokens.Length == 1 && !endsWithWhitespace) {
				var exactCommand = _registry.Find(tokens[0]);
				bool isCompleteCommandName = exactCommand != null
					&& string.Equals(exactCommand.Name, tokens[0], StringComparison.Ordinal);
				if (!isCompleteCommandName) {
					return CommandNameSuggestions(tokens[0], input.Length - tokens[0].Length);
				}
				// The typed text already matches a command's exact registry casing - there is
				// nothing left to complete on the name itself (a same-text Complete() would be a
				// no-op), so treat this call as if a trailing space were already there and fall
				// through to argument-name suggestions instead of leaving Tab stuck. The input
				// itself has no real space character yet, so Complete() must synthesize one.
				endsWithWhitespace = true;
				needsSyntheticSeparator = true;
			}

			var descriptor = _registry.Find(tokens[0]);
			if (descriptor == null) {
				return SuggestionResult.None;
			}

			string currentToken = endsWithWhitespace ? "" : tokens[tokens.Length - 1];
			int currentTokenStart = endsWithWhitespace ? input.Length : input.Length - currentToken.Length;

			int equalsIndex = currentToken.IndexOf('=');
			if (equalsIndex >= 0) {
				string key = currentToken.Substring(0, equalsIndex);
				string valuePrefix = currentToken.Substring(equalsIndex + 1);
				var parameter = descriptor.Parameters
					.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
				if (parameter == null) {
					return SuggestionResult.None;
				}
				var valueCandidates = _valueResolver.Resolve(parameter)
					.Where(i => i.Value.StartsWith(valuePrefix, StringComparison.OrdinalIgnoreCase))
					.ToList();
				int valueStart = currentTokenStart + equalsIndex + 1;
				return new SuggestionResult(SuggestionKind.ArgumentValue, valueCandidates, valueStart, needsSyntheticSeparator);
			}

			var nameCandidates = descriptor.Parameters
				.Select(p => p.Name)
				.Where(n => n.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n, StringComparer.Ordinal)
				.Select(n => new SuggestionItem(n, n))
				.ToList();
			return new SuggestionResult(SuggestionKind.ArgumentName, nameCandidates, currentTokenStart, needsSyntheticSeparator);
		}

		SuggestionResult CommandNameSuggestions(string prefix, int tokenStart) {
			var candidates = _registry.Commands
				.Select(c => c.Name)
				.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n, StringComparer.Ordinal)
				.Select(n => new SuggestionItem(n, n))
				.ToList();
			return new SuggestionResult(SuggestionKind.CommandName, candidates, tokenStart);
		}
	}
}
