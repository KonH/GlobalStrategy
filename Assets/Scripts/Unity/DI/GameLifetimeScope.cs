using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.Save;
using GS.Unity.UI;
using GS.Unity.EcsViewer;
using GS.Unity.Common;

namespace GS.Unity.DI {
	public class GameLifetimeScope : LifetimeScope {
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] MapCameraConfig _mapCameraConfig;
		[SerializeField] TextAsset _geoJsonConfig;
		[SerializeField] TextAsset _mapEntryConfig;
		[SerializeField] TextAsset _countryConfigAsset;
		[SerializeField] TextAsset _gameSettings;
		[SerializeField] TextAsset _resourceConfig;
		[SerializeField] TextAsset _organizationsConfigAsset;
		[SerializeField] TextAsset _characterConfigAsset;

		protected override void Configure(IContainerBuilder builder) {
			var storage = new PersistentStorage();
			var serializer = new NewtonsoftSnapshotSerializer();

			string initialPlayer = SceneTransitionArgs.InitialPlayerCountry ?? "Russian_Empire";
			string initialOrgId = SceneTransitionArgs.OrganizationId ?? "";

			var ctx = new GameLogicContext(
				new TextAssetConfig<GeoJsonConfig>(_geoJsonConfig),
				new TextAssetConfig<MapEntryConfig>(_mapEntryConfig),
				new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset),
				new TextAssetConfig<GameSettings>(_gameSettings),
				new TextAssetConfig<ResourceConfig>(_resourceConfig),
				new TextAssetConfig<OrganizationConfig>(_organizationsConfigAsset),
				storage,
				serializer,
				new UnityGameLogger(),
				initialPlayer,
				initialOrgId,
				character: _characterConfigAsset != null ? new TextAssetConfig<GS.Game.Configs.CharacterConfig>(_characterConfigAsset) : null
			);

			var domainCountryConfig = new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset).Load();
			builder.RegisterInstance(domainCountryConfig);

			builder.RegisterInstance(ctx);
			builder.Register<GameLogic>(Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().VisualState, Lifetime.Singleton);
			builder.Register<IWriteOnlyCommandAccessor>(c => c.Resolve<GameLogic>().Commands, Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().ResourceConfig, Lifetime.Singleton);
			builder.Register(c => c.Resolve<GameLogic>().CharacterConfig, Lifetime.Singleton);

			builder.RegisterInstance<IPersistentStorage>(storage);
			builder.RegisterInstance<ISnapshotSerializer>(serializer);
			builder.Register<SaveFileManager>(Lifetime.Singleton);

			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();
			builder.RegisterComponentInHierarchy<TimeInputHandler>();

			builder.Register<ECS.Viewer.PauseToken>(VContainer.Lifetime.Singleton);
			builder.RegisterEntryPoint<GameLoopRunner>();
			builder.RegisterComponentInHierarchy<EcsViewerBridge>();

			builder.RegisterComponentInHierarchy<GameMenuDocument>();
			builder.RegisterComponentInHierarchy<SettingsWindowDocument>();
		}

		protected override void OnDestroy() {
			base.OnDestroy();
			SceneTransitionArgs.Clear();
		}
	}
}
