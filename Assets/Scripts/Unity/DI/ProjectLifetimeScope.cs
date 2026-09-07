using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Main;
using GS.Unity.Common;
using GS.Unity.Save;
using GS.Unity.UI;
using GS.Unity.E2E;

namespace GS.Unity.DI {
	public class ProjectLifetimeScope : LifetimeScope {
		[SerializeField] LocalizationConfig _localizationConfig;

		protected override void Configure(IContainerBuilder builder) {
			builder.RegisterInstance<IPersistentStorage>(new PersistentStorage(E2ERunContext.StorageRootOrDefault()));
			builder.RegisterInstance(_localizationConfig);
			builder.Register<SettingsStorage>(Lifetime.Singleton);
			builder.Register<ILocalization, CustomLocalization>(Lifetime.Singleton);
			builder.Register<SceneLoader>(Lifetime.Singleton);
		}
	}
}
