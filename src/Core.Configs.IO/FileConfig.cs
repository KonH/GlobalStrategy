using System.IO;
using System.Text.Json;
using GS.Configs;

namespace GS.Configs.IO {
	public class FileConfig<TConfig> : IConfigSource<TConfig> {
		readonly string _filePath;

		static readonly JsonSerializerOptions _options = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
		};

		public FileConfig(string filePath) {
			_filePath = filePath;
		}

		public TConfig Load() {
			string json = File.ReadAllText(_filePath);
			return JsonSerializer.Deserialize<TConfig>(json, _options)
				?? throw new System.InvalidOperationException(
					$"Failed to deserialize {typeof(TConfig).Name} from {_filePath}");
		}
	}
}
