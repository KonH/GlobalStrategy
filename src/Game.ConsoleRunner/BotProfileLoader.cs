using System;
using System.IO;
using System.Text.Json;
using GS.Game.Bots;

namespace GS.Game.ConsoleRunner {
	public static class BotProfileLoader {
		static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true
		};

		public static BotProfile Load(string path) {
			if (!File.Exists(path)) {
				throw new ArgumentException($"Bot profile file not found: '{path}'.");
			}

			string json = File.ReadAllText(path);
			BotProfile? profile;
			try {
				profile = JsonSerializer.Deserialize<BotProfile>(json, s_jsonOptions);
			} catch (JsonException ex) {
				throw new ArgumentException($"Malformed bot profile JSON in '{path}': {ex.Message}");
			}

			if (profile == null || string.IsNullOrEmpty(profile.OrgId)) {
				throw new ArgumentException($"Bot profile '{path}' is missing a non-empty 'orgId'.");
			}

			return profile;
		}
	}
}
