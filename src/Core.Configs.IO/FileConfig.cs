using System.IO;
using Newtonsoft.Json;
using GS.Configs;

namespace GS.Configs.IO {
	public class FileConfig<TConfig> : IReadOnlyConfigSource<TConfig> {
		readonly string _filePath;

		public FileConfig(string filePath) {
			_filePath = filePath;
		}

		public TConfig Load() {
			string json = File.ReadAllText(_filePath);
			return JsonConvert.DeserializeObject<TConfig>(json)
				?? throw new System.InvalidOperationException(
					$"Failed to deserialize {typeof(TConfig).Name} from {_filePath}");
		}
	}
}
