using UnityEngine;
using VContainer;
using VContainer.Unity;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Gallery's own DI entry point, parented to GS.Unity.DI.ProjectLifetimeScope (set on the
	/// GalleryLifetimeScope component in Assets/Scenes/Gallery.unity), the same way
	/// MainMenuLifetimeScope is parented. ILocalization, SettingsStorage and IPersistentStorage
	/// are NOT re-registered here - they are inherited from the project scope. Only the
	/// gallery-specific configs that used to be [SerializeField] on GalleryDocument are
	/// registered, so every gallery block can resolve them (and ILocalization) through the
	/// container instead of GalleryDocument new-ing them by hand.
	/// </summary>
	public class GalleryLifetimeScope : LifetimeScope {
		[SerializeField] TextAsset _actionConfigAsset;
		[SerializeField] ActionVisualConfig _actionVisualConfig;
		[SerializeField] CountryVisualConfig _countryVisualConfig;
		[SerializeField] CharacterVisualConfig _characterVisualConfig;
		[SerializeField] OrgVisualConfig _orgVisualConfig;

		// _characterConfigAsset is NOT registered here: it is a second TextAsset dependency, and
		// VContainer resolves by type, so registering two TextAsset instances would make it
		// ambiguous which one GalleryDocument's [Inject] TextAsset parameter actually receives.
		// It stays a plain [SerializeField] on GalleryDocument instead, alongside the other
		// Gallery-only sample values (_discardGoldCost, _sampleTargetCountryId) that predate this
		// scope for the same reason.
		protected override void Configure(IContainerBuilder builder) {
			builder.RegisterInstance(_actionConfigAsset);
			builder.RegisterInstance(_actionVisualConfig);
			builder.RegisterInstance(_countryVisualConfig);
			builder.RegisterInstance(_characterVisualConfig);
			builder.RegisterInstance(_orgVisualConfig);
			builder.RegisterComponentInHierarchy<GalleryDocument>();
		}
	}
}
