using System.IO;
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

		protected override void Configure(IContainerBuilder builder) {
			var countryConfigSource = new StreamingAssetsConfig<GS.Game.Configs.CountryConfig>(
				Path.Combine(Application.streamingAssetsPath, "Configs", "country_config.json"));

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
