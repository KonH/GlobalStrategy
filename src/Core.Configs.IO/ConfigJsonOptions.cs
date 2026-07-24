using System.Text.Json;
using System.Text.Json.Serialization;

namespace GS.Configs.IO {
	static class ConfigJsonOptions {
		public static readonly JsonSerializerOptions Value = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
			Converters = { new JsonStringEnumConverter(), new ActionEffectDefinitionListConverter() },
		};
	}
}
