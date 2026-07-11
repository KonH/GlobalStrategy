using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Map;
using GS.Unity.Save;
using GS.Unity.UI;

namespace GS.Unity.DI {
	public class SelectCountryLifetimeScope : LifetimeScope {
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] OrgVisualConfig _orgVisualConfig;
		[SerializeField] MapCameraConfig _mapCameraConfig;
		[SerializeField] TextAsset _countryConfigAsset;
		[SerializeField] TextAsset _organizationsConfigAsset;
		[SerializeField] TextAsset _resourceConfigAsset;
		[SerializeField] TextAsset _provinceConfigAsset;

		protected override void Configure(IContainerBuilder builder) {
			var countryConfigSource = new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset);
			var domainCountryConfig = countryConfigSource.Load();
			var orgConfigSource = new TextAssetConfig<OrganizationConfig>(_organizationsConfigAsset);
			var resourceConfig = new TextAssetConfig<ResourceConfig>(_resourceConfigAsset).Load();
			var provinceConfig = new TextAssetConfig<GS.Game.Configs.ProvinceConfig>(_provinceConfigAsset).Load();

			builder.RegisterInstance(domainCountryConfig);
			builder.Register(_ => new SelectOrgLogic(countryConfigSource, orgConfigSource, resourceConfig), Lifetime.Singleton);
			builder.Register(c => c.Resolve<SelectOrgLogic>().VisualState, Lifetime.Singleton);

			builder.RegisterInstance(provinceConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_orgVisualConfig);
			builder.RegisterInstance(_mapCameraConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();
			builder.RegisterComponentInHierarchy<MapClickHandler>();
			builder.RegisterComponentInHierarchy<SelectOrgMapFilter>();
			builder.RegisterComponentInHierarchy<SelectOrgDocument>();

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
