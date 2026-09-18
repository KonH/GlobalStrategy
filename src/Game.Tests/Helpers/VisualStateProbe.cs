using System.Collections.Generic;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;

namespace GS.Game.Tests.Helpers {
	/// <summary>
	/// Bundles the <see cref="VisualState"/> / <see cref="VisualStateConverter"/> /
	/// <see cref="ResourceQuery"/> / <see cref="CountryRelations"/> quartet that every
	/// converter test otherwise wires up by hand, and pulls the game-time / locale / org
	/// entity ids straight off the <see cref="TestWorld"/> being projected.
	/// </summary>
	public sealed class VisualStateProbe {
		readonly VisualStateConverter _converter;

		public VisualState State { get; } = new VisualState();

		/// <summary>For the projections that are pulled on demand rather than run by <see cref="Update"/>.</summary>
		public VisualStateConverter Converter => _converter;

		public ResourceQuery Resources { get; }
		public CountryRelations Relations { get; }

		public VisualStateProbe(
			ActionConfig? actionConfig = null,
			ResourceQuery? resources = null,
			CountryRelations? relations = null,
			CountryConfig? countryConfig = null,
			EventNotificationSettings? eventNotifications = null,
			EffectConfig? effectConfig = null,
			TasksConfig? tasksConfig = null,
			CountryActionsVisibility? actionsVisibility = null,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			int maxControlPool = 100) {
			Resources = resources ?? new ResourceQuery();
			Relations = relations ?? new CountryRelations();
			_converter = new VisualStateConverter(
				State,
				Resources,
				Relations,
				actionConfig,
				hqCountryByOrgId,
				countryConfig: countryConfig,
				eventNotifications: eventNotifications,
				maxControlPool: maxControlPool,
				effectConfig: effectConfig,
				tasksConfig: tasksConfig,
				actionsVisibility: actionsVisibility);
		}

		/// <summary>Projects <paramref name="world"/> into <see cref="State"/>.</summary>
		public VisualStateProbe Update(TestWorld world, float deltaTime = 0f) {
			_converter.Update(deltaTime, world.World, world.GameTimeEntity, world.LocaleEntity, world.OrgEntity);
			return this;
		}

		/// <summary>The selected country's projected hand.</summary>
		public IReadOnlyList<ActionCardEntry> CountryHand => State.SelectedCountry.CountryActions.Hand;

		/// <summary>The selected country's hand entry for <paramref name="actionId"/>; fails the test when absent.</summary>
		public ActionCardEntry Card(string actionId) => Hand.Require(CountryHand, actionId);
	}
}
