using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Ecs {
	public static class EcsWorldSnapshotJson {
		static readonly JsonSerializerOptions Options = CreateOptions();

		public static EcsWorldSnapshot? Parse(string json) {
			if (string.IsNullOrWhiteSpace(json)) {
				return null;
			}

			string trimmed = json.TrimStart();
			if (trimmed.StartsWith("<", StringComparison.Ordinal) || trimmed.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase)) {
				return null;
			}

			return JsonSerializer.Deserialize<EcsWorldSnapshot>(json, Options);
		}

		static JsonSerializerOptions CreateOptions() {
			var options = new JsonSerializerOptions {
				PropertyNameCaseInsensitive = true,
				NumberHandling = JsonNumberHandling.AllowReadingFromString
			};
			options.Converters.Add(new JsonStringEnumConverter());
			return options;
		}
	}
}
