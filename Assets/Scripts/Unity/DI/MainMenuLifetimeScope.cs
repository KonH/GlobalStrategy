using VContainer;
using VContainer.Unity;
using UnityEngine;
using GS.Main;
using GS.Unity.Map;
using GS.Unity.Save;
using GS.Unity.UI;

namespace GS.Unity.DI {
	public class MainMenuLifetimeScope : LifetimeScope {
		[SerializeField] TextAsset _countryConfigAsset;
		[SerializeField] CountryVisualConfig _countryVisualConfig;

		protected override void Configure(IContainerBuilder builder) {
			var storage = new PersistentStorage();
			var serializer = new NewtonsoftSnapshotSerializer();
			builder.RegisterInstance<IPersistentStorage>(storage);
			builder.RegisterInstance<ISnapshotSerializer>(serializer);
			builder.Register<SaveFileManager>(Lifetime.Singleton);

			var domainCountryConfig = new TextAssetConfig<GS.Game.Configs.CountryConfig>(_countryConfigAsset).Load();
			builder.RegisterInstance(domainCountryConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterComponentInHierarchy<Camera>();
			builder.RegisterComponentInHierarchy<MapLoader>();
			builder.RegisterComponentInHierarchy<MapController>();

			builder.Register(c => {
				string locale = c.Resolve<ILocalization>().CurrentLocale;
				return new StaticGameLogic(locale);
			}, Lifetime.Singleton);
			builder.Register<IWriteOnlyCommandAccessor>(c => c.Resolve<StaticGameLogic>().Commands, Lifetime.Singleton);
			builder.Register(c => c.Resolve<StaticGameLogic>().VisualState, Lifetime.Singleton);
			builder.RegisterEntryPoint<StaticGameLoopRunner>();

			builder.RegisterComponentInHierarchy<LoadWindowDocument>();
			builder.RegisterComponentInHierarchy<SettingsWindowDocument>();
		}
	}
}
