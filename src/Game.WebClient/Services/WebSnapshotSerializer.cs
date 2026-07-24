using GS.Main;
using Newtonsoft.Json;

namespace GS.Game.WebClient.Services {
	// Web-side counterpart of Assets/Scripts/Unity/Save/NewtonsoftSnapshotSerializer.cs -
	// same WorldSnapshot model, same Newtonsoft settings. Cross-client save
	// compatibility is not a requirement; web saves live and die in the browser.
	public class WebSnapshotSerializer : ISnapshotSerializer {
		static readonly JsonSerializerSettings _settings = new JsonSerializerSettings {
			Formatting = Formatting.Indented
		};

		public string Serialize(WorldSnapshot snapshot) =>
			JsonConvert.SerializeObject(snapshot, _settings);

		public WorldSnapshot Deserialize(string json) =>
			JsonConvert.DeserializeObject<WorldSnapshot>(json)!;
	}
}
