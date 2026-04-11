using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.UI;
using GS.Unity.EcsViewer;

namespace GS.Unity.DI {
	public class GameLifetimeScope : LifetimeScope {
		[SerializeField] GS.Unity.Map.CountryConfig _countryConfig;
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] MapCameraConfig _mapCameraConfig;

		protected override void Configure(IContainerBuilder builder) {
			var ctx = new GameLogicContext(
				new StreamingAssetsConfig<GeoJsonConfig>(ConfigPath("geojson_world.json")),
				new StreamingAssetsConfig<MapEntryConfig>(ConfigPath("map_entry_config.json")),
				new StreamingAssetsConfig<GS.Game.Configs.CountryConfig>(ConfigPath("country_config.json")),
				new StreamingAssetsConfig<GameSettings>(ConfigPath("game_settings.json")),
				new StreamingAssetsConfig<ResourceConfig>(ConfigPath("resource_config.json"))
			);

			builder.RegisterInstance(ctx);
			builder.Register<GameLogic>(Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().VisualState, Lifetime.Singleton);
			builder.Register<IWriteOnlyCommandAccessor>(c => c.Resolve<GameLogic>().Commands, Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().ResourceConfig, Lifetime.Singleton);

			builder.RegisterInstance(_countryConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();
			builder.RegisterComponentInHierarchy<TimeInputHandler>();

			builder.Register<ECS.Viewer.PauseToken>(VContainer.Lifetime.Singleton);
			builder.RegisterEntryPoint<GameLoopRunner>();
			builder.RegisterComponentInHierarchy<EcsViewerBridge>();
		}

		static string ConfigPath(string file) =>
			Path.Combine(Application.streamingAssetsPath, "Configs", file);
	}
}
