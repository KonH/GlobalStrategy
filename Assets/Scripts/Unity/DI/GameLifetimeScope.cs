using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.DI {
	public class GameLifetimeScope : LifetimeScope {
		[SerializeField] GS.Unity.Map.CountryConfig _countryConfig;
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] MapCameraConfig _mapCameraConfig;
		[SerializeField] LocalizationConfig _localizationConfig;

		protected override void Configure(IContainerBuilder builder) {
			var ctx = new GameLogicContext(
				new StreamingAssetsConfig<GeoJsonConfig>(ConfigPath("geojson_world.json")),
				new StreamingAssetsConfig<MapEntryConfig>(ConfigPath("map_entry_config.json")),
				new StreamingAssetsConfig<GS.Game.Configs.CountryConfig>(ConfigPath("country_config.json")),
				new StreamingAssetsConfig<GameSettings>(ConfigPath("game_settings.json"))
			);

			builder.RegisterInstance(ctx);
			builder.Register<GameLogic>(Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().VisualState, Lifetime.Singleton);
			builder.Register<IWriteOnlyCommandAccessor>(c => c.Resolve<GameLogic>().Commands, Lifetime.Singleton);

			builder.RegisterInstance(_countryConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.Register<ILocalization>(_ => new CustomLocalization(_localizationConfig), Lifetime.Singleton);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();
			builder.RegisterComponentInHierarchy<TimeInputHandler>();

			builder.RegisterEntryPoint<GameLoopRunner>();
		}

		static string ConfigPath(string file) =>
			Path.Combine(Application.streamingAssetsPath, "Configs", file);
	}
}
