using System.Text.Json;
using GS.Configs;

namespace GS.Configs.IO {
	public class StringConfig<TConfig> : IConfigSource<TConfig> {
		readonly string _json;

		public StringConfig(string json) {
			_json = json;
		}

		public TConfig Load() {
			return JsonSerializer.Deserialize<TConfig>(_json, ConfigJsonOptions.Value)
				?? throw new System.InvalidOperationException(
					$"Failed to deserialize {typeof(TConfig).Name} from provided JSON string");
		}
	}
}
