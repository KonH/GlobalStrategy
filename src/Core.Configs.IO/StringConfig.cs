using Newtonsoft.Json;
using GS.Configs;

namespace GS.Configs.IO {
	public class StringConfig<TConfig> : IReadOnlyConfigSource<TConfig> {
		readonly string _json;

		public StringConfig(string json) {
			_json = json;
		}

		public TConfig Load() {
			return JsonConvert.DeserializeObject<TConfig>(_json)
				?? throw new System.InvalidOperationException(
					$"Failed to deserialize {typeof(TConfig).Name} from provided JSON string");
		}
	}
}
