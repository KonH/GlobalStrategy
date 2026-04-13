using UnityEngine;
using Newtonsoft.Json;
using GS.Configs;

namespace GS.Unity.DI {
	class TextAssetConfig<TConfig> : IConfigSource<TConfig> {
		readonly TextAsset _asset;

		public TextAssetConfig(TextAsset asset) {
			_asset = asset;
		}

		public TConfig Load() {
			return JsonConvert.DeserializeObject<TConfig>(_asset.text)!;
		}
	}
}
