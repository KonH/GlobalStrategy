using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.Save;

namespace GS.Unity.DI {
	public class SelectCountryLifetimeScope : LifetimeScope {
		[SerializeField] GS.Unity.Map.CountryConfig _countryConfig;
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] MapCameraConfig _mapCameraConfig;
		[SerializeField] TextAsset _countryConfigAsset;

		protected override void Configure(IContainerBuilder builder) {
			var countryConfigSource = new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset);

			builder.Register(_ => new SelectCountryLogic(countryConfigSource), Lifetime.Singleton);

			builder.RegisterInstance(_countryConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();

			// Map clicks need IWriteOnlyCommandAccessor — wire through SelectCountryLogic
			builder.Register<IWriteOnlyCommandAccessor>(
				c => c.Resolve<SelectCountryLogic>().Commands, Lifetime.Singleton);

			var storage = new PersistentStorage();
			var serializer = new NewtonsoftSnapshotSerializer();
			builder.RegisterInstance<IPersistentStorage>(storage);
			builder.RegisterInstance<ISnapshotSerializer>(serializer);
			builder.Register<SaveFileManager>(Lifetime.Singleton);
		}
	}
}
