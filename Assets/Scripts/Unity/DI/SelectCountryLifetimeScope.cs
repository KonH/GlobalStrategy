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
		[SerializeField] TextAsset _organizationsConfigAsset;

		protected override void Configure(IContainerBuilder builder) {
			var countryConfigSource = new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset);
			var orgConfigSource = new TextAssetConfig<OrganizationConfig>(_organizationsConfigAsset);

			builder.Register(_ => new SelectOrgLogic(countryConfigSource, orgConfigSource), Lifetime.Singleton);

			builder.RegisterInstance(_countryConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();
			builder.RegisterComponentInHierarchy<SelectOrgMapFilter>();

			// Map clicks need IWriteOnlyCommandAccessor — wire through SelectOrgLogic
			builder.Register<IWriteOnlyCommandAccessor>(
				c => c.Resolve<SelectOrgLogic>().Commands, Lifetime.Singleton);

			var storage = new PersistentStorage();
			var serializer = new NewtonsoftSnapshotSerializer();
			builder.RegisterInstance<IPersistentStorage>(storage);
			builder.RegisterInstance<ISnapshotSerializer>(serializer);
			builder.Register<SaveFileManager>(Lifetime.Singleton);
		}
	}
}
