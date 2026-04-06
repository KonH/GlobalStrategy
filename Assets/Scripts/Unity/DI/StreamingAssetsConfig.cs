using System.IO;
using Newtonsoft.Json;
using GS.Configs;

namespace GS.Unity.DI {
	class StreamingAssetsConfig<TConfig> : IConfigSource<TConfig> {
		readonly string _path;

		public StreamingAssetsConfig(string path) {
			_path = path;
		}

		public TConfig Load() {
			string json = File.ReadAllText(_path);
			return JsonConvert.DeserializeObject<TConfig>(json)!;
		}
	}
}
