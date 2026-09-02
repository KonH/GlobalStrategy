using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	// Debug-menu-only, pull-only: "My org" and "Selected org" each show one org's full card
	// availability (org-owned cards, e.g. master/agent recruitment, merged with country-owned
	// cards, e.g. relation/control actions) regardless of what's currently selected on the map.
	// Formerly VisualStateConverter.UpdateDebugOrgCardAvailability/BuildDebugOrgCardAvailability,
	// gated per-tick by the now-removed DebugOrgCardVisibility flags; callers now decide when to
	// project (submenu open, or a coarse refresh timer while it stays open) and pass the
	// deck/hand-detail-open flags explicitly instead of reading them from a shared flag object.
	public static class DebugOrgCardAvailabilityProjector {
		// "Selected org" = the org dominating the currently selected country while the org lens is
		// active (same resolution VisualStateConverter.UpdateResources uses for
		// OrgLensOrganizationResources).
		public static string ResolveSelectedOrgId(MapLens lens, bool orgLensOrganizationResourcesValid, string orgLensOrganizationResourcesCountryId) {
			return lens == MapLens.Org && orgLensOrganizationResourcesValid ? orgLensOrganizationResourcesCountryId : "";
		}

		public static void Project(
			IReadOnlyWorld world,
			OrgCardAvailabilityState target,
			string orgId,
			DateTime currentTime,
			bool deckDetailOpen,
			bool handDetailOpen,
			ActionConfig? actionConfig,
			EffectConfig? effectConfig,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string> hqCountryByOrgId,
			int maxControlPool) {
			if (string.IsNullOrEmpty(orgId) || actionConfig == null) {
				target.Set(false, "", new List<ActionCardEntry>(), new List<ActionCardEntry>());
				return;
			}

			string countryContextId = hqCountryByOrgId.TryGetValue(orgId, out string hqCountryId) ? hqCountryId : "";
			ControlWarSnapshot? snapshotOrNull = null;
			ControlWarSnapshot GetSnapshot() => snapshotOrNull ??= ControlWarSnapshot.Build(world);

			var hand = new List<ActionCardEntry>();
			var deck = new List<ActionCardEntry>();

			// No CardOwnerType filter - org cards (master/agent recruitment, ...) and country
			// cards (relation/control actions, ...) are merged into the same debug listing.
			int[] handReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardOwnerType>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId) { continue; }
					var entry = handDetailOpen
						? ActionCardEntryProjector.BuildEntry(
							world, actionConfig, effectConfig, resources, relations, hqCountryByOrgId, maxControlPool,
							orgId, countryContextId, arch.Entities[i], actions[i].ActionId, hands[i].SlotIndex, true,
							false, currentTime, Array.Empty<string>(), GetSnapshot())
						: ActionCardEntryProjector.BuildCheapEntry(actionConfig, actions[i].ActionId, hands[i].SlotIndex, true);
					if (entry != null) { hand.Add(entry); }
				}
			}

			int[] deckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardOwnerType>.Value };
			int[] excludeInHandOrChoice = { TypeId<CardInHand>.Value, TypeId<CardDrawChoice>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHandOrChoice)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId) { continue; }
					var entry = deckDetailOpen
						? ActionCardEntryProjector.BuildEntry(
							world, actionConfig, null, resources, relations, hqCountryByOrgId, maxControlPool,
							orgId, countryContextId, arch.Entities[i], actions[i].ActionId, -1, false,
							false, currentTime, Array.Empty<string>(), GetSnapshot())
						: ActionCardEntryProjector.BuildCheapEntry(actionConfig, actions[i].ActionId, -1, false);
					if (entry != null) { deck.Add(entry); }
				}
			}

			hand.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
			target.Set(true, orgId, hand, deck);
		}
	}
}
