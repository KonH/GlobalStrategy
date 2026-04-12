using GS.Main;
using Newtonsoft.Json;

namespace GS.Unity.Save {
	public class NewtonsoftSnapshotSerializer : ISnapshotSerializer {
		static readonly JsonSerializerSettings _settings = new JsonSerializerSettings {
			Formatting = Formatting.Indented
		};

		public string Serialize(WorldSnapshot snapshot) =>
			JsonConvert.SerializeObject(snapshot, _settings);

		public WorldSnapshot Deserialize(string json) =>
			JsonConvert.DeserializeObject<WorldSnapshot>(json)!;
	}
}
