using System;
using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CompletionConditionFactory {
		public static ICompletionCondition Create(CompletionConditionConfig config, int maxControlPool) {
			if (maxControlPool <= 0) {
				throw new ArgumentOutOfRangeException(nameof(maxControlPool), maxControlPool,
					"Completion-condition control capacity must be positive.");
			}
			return Create(config, "completionCondition");
		}

		static ICompletionCondition Create(CompletionConditionConfig? config, string path) {
			if (config == null) {
				throw new ArgumentException($"Completion condition at '{path}' cannot be null.");
			}

			if (!CompletionConditionTypeParser.TryParse(config.Type, out var type)) {
				throw new ArgumentException($"Unknown completion condition type '{config.Type}' at '{path}'.");
			}

			switch (type) {
				case CompletionConditionType.Any:
					if (config.Members == null || config.Members.Count == 0) {
						throw new ArgumentException($"Completion condition at '{path}' of type 'any' must contain at least one member.");
					}
					var members = new List<ICompletionCondition>(config.Members.Count);
					for (int i = 0; i < config.Members.Count; i++) {
						members.Add(Create(config.Members[i], $"{path}.members[{i}]"));
					}
					return new AnyCompletionCondition(members);
				case CompletionConditionType.TotalControl:
					return CreateTotalControl(config.Value, path);
				case CompletionConditionType.FullControlCountries:
					return CreateFullControl(config.Value, path);
				default:
					throw new ArgumentException($"Unknown completion condition type '{config.Type}' at '{path}'.");
			}
		}

		static ICompletionCondition CreateTotalControl(double value, string path) {
			try {
				return new TotalControlCondition(value);
			} catch (ArgumentOutOfRangeException exception) {
				throw new ArgumentException($"Invalid completion condition at '{path}': {exception.Message}", exception);
			}
		}

		static ICompletionCondition CreateFullControl(double value, string path) {
			try {
				return new FullControlCondition(value);
			} catch (ArgumentOutOfRangeException exception) {
				throw new ArgumentException($"Invalid completion condition at '{path}': {exception.Message}", exception);
			}
		}
	}
}
