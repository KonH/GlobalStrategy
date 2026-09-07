using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GS.Game.E2E {
	public static class E2EJson {
		public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.Indented,
			MissingMemberHandling = MissingMemberHandling.Ignore
		};

		public static T Deserialize<T>(string json) {
			var value = JsonConvert.DeserializeObject<T>(json, Settings);
			if (value == null) {
				throw new JsonSerializationException("JSON deserialized to null.");
			}
			return value;
		}

		public static string Serialize<T>(T value) {
			return JsonConvert.SerializeObject(value, Settings);
		}
	}

	public static class StepScriptSerializer {
		public static StepScript Deserialize(string json) {
			return E2EJson.Deserialize<StepScript>(json);
		}

		public static string Serialize(StepScript script) {
			return E2EJson.Serialize(script);
		}
	}

	public static class RunRequestSerializer {
		public static RunRequest Deserialize(string json) {
			return E2EJson.Deserialize<RunRequest>(json);
		}

		public static string Serialize(RunRequest request) {
			return E2EJson.Serialize(request);
		}
	}

	public static class RunReportSerializer {
		public static RunReport Deserialize(string json) {
			return E2EJson.Deserialize<RunReport>(json);
		}

		public static string Serialize(RunReport report) {
			return E2EJson.Serialize(report);
		}
	}
}
