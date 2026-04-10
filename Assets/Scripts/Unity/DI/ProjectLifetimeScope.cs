using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Unity.UI;

namespace GS.Unity.DI {
	public class ProjectLifetimeScope : LifetimeScope {
		[SerializeField] LocalizationConfig _localizationConfig;

		protected override void Configure(IContainerBuilder builder) {
			builder.Register<ILocalization>(_ => new CustomLocalization(_localizationConfig), Lifetime.Singleton);
		}
	}
}
