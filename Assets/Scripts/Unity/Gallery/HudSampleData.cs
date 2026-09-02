using System;
using System.Collections.Generic;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Hand-built VisualState substates for the HUD panel gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring
	/// phase 7). Every builder exercises the same Set(...) entry points VisualStateConverter would
	/// use against a real ECS world - no running game, no save - so a block's Refresh call is fed
	/// the same shape of data it would get in the shipping HUD.
	/// </summary>
	static class HudSampleData {
		public static List<ResourceStateEntry> BuildResourceEntries(ResourceConfig config, double baseValue = 240) {
			var entries = new List<ResourceStateEntry>();
			if (config == null) {
				return entries;
			}
			int i = 0;
			foreach (string resourceId in config.DisplayWhitelist) {
				var value = new AnimatableDouble();
				value.SetActual(baseValue + (i * 37));
				var effects = new List<EffectStateEntry> {
					new EffectStateEntry("base_income", 4.5 + i, PayType.Monthly),
					new EffectStateEntry("upkeep", -1.5, PayType.Monthly),
				};
				entries.Add(new ResourceStateEntry(resourceId, value, effects));
				i++;
			}
			return entries;
		}

		public static CountryResourcesState BuildResources(ResourceConfig config, string ownerId = "sample", double baseValue = 240) {
			var state = new CountryResourcesState();
			state.Set(true, ownerId, BuildResourceEntries(config, baseValue));
			return state;
		}

		public static SelectedCountryState BuildSelectedCountry(
			string countryId,
			ResourceConfig resourceConfig,
			IReadOnlyList<string> friends,
			IReadOnlyList<string> rivals,
			IReadOnlyList<string> warOpponents,
			string dominantOrgId = "player_org",
			string dominantOrgName = "Freedonia Council",
			string secondOrgId = "rival_org",
			string secondOrgName = "Sylvanian League") {
			var state = new SelectedCountryState();
			state.Set(true, countryId);
			state.Resources.Set(true, countryId, BuildResourceEntries(resourceConfig));
			state.Control.Set(62, new List<OrgControlEntry> {
				new OrgControlEntry(dominantOrgId, dominantOrgName, 40, 30, 10, 12.5),
				new OrgControlEntry(secondOrgId, secondOrgName, 22, 20, 2, 4.0),
			});
			state.Characters.Set(new List<CharacterStateEntry>());
			state.CountryActions.Set(
				new List<ActionCardEntry>(),
				new List<ActionCardEntry>(),
				new List<CardDrawChoiceEntry>(),
				handSize: 3,
				hasPendingDraw: false,
				canStartDraw: true,
				currentTime: DateTime.UtcNow);
			state.Relations.Set(friends ?? Array.Empty<string>(), rivals ?? Array.Empty<string>());
			state.Wars.Set(warOpponents ?? Array.Empty<string>());
			return state;
		}

		public static OrgMapState BuildOrgMap(string countryId, string topOrgId) {
			var state = new OrgMapState();
			state.Set(new List<OrgCountryEntry> {
				new OrgCountryEntry(countryId, topOrgId, 0.72f),
			});
			return state;
		}

		public static PlayerOrganizationState BuildPlayerOrganization(string orgId, string displayName, string hqCountryId, ResourceConfig resourceConfig) {
			var state = new PlayerOrganizationState();
			state.Set(true, orgId, displayName, hqCountryId);
			state.Resources.Set(true, orgId, BuildResourceEntries(resourceConfig, 480));
			return state;
		}

		public static ActiveTasksState BuildActiveTasks(bool includeTutorial = false, string highlightTargetId = "") {
			var state = new ActiveTasksState();
			state.Set(new List<ActiveTaskEntryState> {
				new ActiveTaskEntryState(
					"task_strengthen_army",
					"gallery.sample.task_name",
					"gallery.sample.task_desc",
					new List<ActiveTaskRewardState> {
						new ActiveTaskRewardState(ResourceDefinitions.Gold, 25),
						new ActiveTaskRewardState("recruits", 10),
					},
					isTutorial: includeTutorial,
					highlightTargetId: highlightTargetId),
			});
			return state;
		}

		public static WarIconsState BuildWarIcons(string attackerCountryId, string defenderCountryId) {
			var state = new WarIconsState();
			state.Set(new List<WarIconEntryState> {
				new WarIconEntryState("sample_war", 24.0, attackerCountryId, defenderCountryId),
			});
			return state;
		}

		public static GameLogState BuildGameLog(string orgId, string countryId, string targetCountryId) {
			var state = new GameLogState();
			state.Set(new List<GameLogEntry> {
				new GameLogEntry(1, GameLogEntryKind.Control, orgId, countryId, "", "", Array.Empty<string>(), 5, 45, isOrgRole: false),
				new GameLogEntry(2, GameLogEntryKind.Relation, orgId, countryId, "", "", Array.Empty<string>(), 0, 0, isOrgRole: false, targetCountryId: targetCountryId, relationKind: RelationKind.Rival),
				new GameLogEntry(3, GameLogEntryKind.War, orgId, countryId, "", "", Array.Empty<string>(), 0, 0, isOrgRole: false, targetCountryId: targetCountryId),
			});
			return state;
		}

		/// <summary>
		/// Two hand-built characters (one per available role, up to two) for CharactersView /
		/// OrgCharactersView gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "Hand/deck
		/// and animation blocks" batch) - every role skill gets a sample value so the stat chips render.
		/// </summary>
		public static CharacterStateEntry BuildCharacterEntry(CharacterConfig config, CharacterRoleDefinition role, string characterId, int opinion) {
			var skills = new List<SkillEntry>();
			foreach (string skillId in role.SkillIds) {
				skills.Add(new SkillEntry(skillId, 50));
			}
			var opinionAnimatable = new AnimatableInt();
			opinionAnimatable.SetActual(opinion);
			return new CharacterStateEntry(
				characterId, role.RoleId, new[] { "gallery.sample.character_name" }, skills, opinionAnimatable);
		}

		public static List<CharacterStateEntry> BuildSampleCountryCharacters(CharacterConfig config) {
			var entries = new List<CharacterStateEntry>();
			if (config == null) {
				return entries;
			}
			int opinion = 15;
			int count = 0;
			foreach (CharacterRoleDefinition role in config.Roles) {
				entries.Add(BuildCharacterEntry(config, role, $"gallery_character_{count}", opinion));
				opinion -= 25;
				count++;
				if (count >= 2) {
					break;
				}
			}
			return entries;
		}

		public static List<OrgCharacterSlotEntry> BuildSampleOrgCharacterSlots(CharacterConfig config) {
			var slots = new List<OrgCharacterSlotEntry>();
			if (config == null) {
				return slots;
			}
			int slotIndex = 0;
			foreach (CharacterRoleDefinition role in config.Roles) {
				if (slotIndex == 0) {
					CharacterStateEntry entry = BuildCharacterEntry(config, role, "gallery_org_character_0", 10);
					slots.Add(new OrgCharacterSlotEntry(role.RoleId, slotIndex, entry, true));
				} else {
					slots.Add(new OrgCharacterSlotEntry(role.RoleId, slotIndex, null, slotIndex == 1));
				}
				slotIndex++;
				if (slotIndex >= 3) {
					break;
				}
			}
			return slots;
		}

		/// <summary>
		/// Hand-built leaderboard (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the seven windows
		/// that already have a view" batch) - one entry per configured org/country, ranked by a
		/// descending score, feeding LeaderboardWindowView/GoalsWindowView/EndGameWindowView the
		/// same shape LeaderboardProjector would.
		/// </summary>
		public static LeaderboardState BuildLeaderboardState(
			ILocalization loc, OrgVisualConfig orgVisualConfig, CountryVisualConfig countryVisualConfig, CountryConfig countryConfig = null) {
			var orgs = new List<LeaderboardEntryState>();
			if (orgVisualConfig != null) {
				int place = 1;
				double score = 4200;
				foreach (OrgVisualEntry entry in orgVisualConfig.Entries) {
					string name = loc?.Get($"organization_name.{entry.orgId}") ?? entry.orgId;
					orgs.Add(new LeaderboardEntryState(place, entry.orgId, name, score));
					place++;
					score -= 850;
				}
			}
			var countries = new List<LeaderboardEntryState>();
			if (countryVisualConfig != null) {
				int place = 1;
				double score = 3100;
				int count = 0;
				foreach (CountryVisualEntry entry in countryVisualConfig.Entries) {
					if (!HudConfigLoader.IsCountryAvailable(countryConfig, entry.countryId)) {
						continue;
					}
					countries.Add(new LeaderboardEntryState(place, entry.countryId, entry.countryId, score));
					place++;
					score -= 240;
					count++;
					if (count >= 8) {
						break;
					}
				}
			}
			var state = new LeaderboardState();
			state.Set(orgs, countries);
			return state;
		}

		/// <summary>
		/// One goal row per WinConditionHintKind for every configured org, so GoalsWindowView's
		/// progress panel has something to show for whichever org the gallery block's list row is
		/// clicked (phase 7 "seven windows" batch).
		/// </summary>
		public static GoalsState BuildGoalsState(OrgVisualConfig orgVisualConfig) {
			var orgs = new List<GoalsOrgEntryState>();
			if (orgVisualConfig != null) {
				foreach (OrgVisualEntry entry in orgVisualConfig.Entries) {
					var goals = new List<GoalProgressEntryState> {
						new GoalProgressEntryState(WinConditionHintKind.TotalControl, 0.5, 32, 50, 154),
						new GoalProgressEntryState(WinConditionHintKind.FullControlCountries, 10, 4, 10, 154),
						new GoalProgressEntryState(WinConditionHintKind.ScoreGoal, 5000, 3200, 5000, 0),
						new GoalProgressEntryState(WinConditionHintKind.LastOrgStanding, 0, 0, 0, 0),
					};
					orgs.Add(new GoalsOrgEntryState(entry.orgId, goals));
				}
			}
			var state = new GoalsState();
			state.Set(orgs);
			return state;
		}

		/// <summary>
		/// A sample war's progress state for WarProgressWindowView (phase 7 "seven windows" batch).
		/// variant selects which side is ahead and whether any battles are in flight, so the
		/// gallery's state dropdown exercises the battles-list ListView with real rows as well as
		/// its empty state.
		/// </summary>
		public static SelectedWarState BuildSelectedWarState(string attackerCountryId, string defenderCountryId, int variant) {
			bool defenderLeading = variant == 1;
			bool noBattles = variant == 2;
			double progress = defenderLeading ? -38 : 42;
			var history = new List<WarProgressHistoryEntryState> {
				new WarProgressHistoryEntryState("war_progress_decay", -2.5, DateTime.UtcNow),
				new WarProgressHistoryEntryState("war_progress_battle_battle_1", defenderLeading ? -14 : 14, DateTime.UtcNow),
			};
			WarSideStatsState attacker = BuildWarSideStats(attackerCountryId, defenderLeading ? 3400 : 4200);
			WarSideStatsState defender = BuildWarSideStats(defenderCountryId, defenderLeading ? 4200 : 3400);
			var battles = noBattles ? new List<WarBattleRowState>() : new List<WarBattleRowState> {
				new WarBattleRowState("battle_1", "Afghanistan__Andkhvoy", false, "", WarParticipantKind.Attacker, 0, 0, 18, 820, 640),
				new WarBattleRowState("battle_2", "Germany__Berlin", true, attackerCountryId, WarParticipantKind.Attacker, 140, 360, 0, 0, 0),
			};
			var state = new SelectedWarState();
			state.Set(true, "gallery_war", progress, history, attacker, defender, battles);
			return state;
		}

		/// <summary>
		/// A finished war's result snapshot for WarResultWindowView (phase 7 "seven windows" batch),
		/// populating gold recipients, control deltas and a province transfer so every non-ListView
		/// section of the window has real rows to preview too.
		/// </summary>
		public static WarResultSnapshotState BuildWarResultSnapshot(string attackerCountryId, string defenderCountryId, bool attackerWon) {
			string winnerCountryId = attackerWon ? attackerCountryId : defenderCountryId;
			string loserCountryId = attackerWon ? defenderCountryId : attackerCountryId;
			var history = new List<WarProgressHistoryEntryState> {
				new WarProgressHistoryEntryState("war_progress_decay", -2.5, DateTime.UtcNow),
				new WarProgressHistoryEntryState("war_progress_battle_battle_1", attackerWon ? 14 : -14, DateTime.UtcNow),
			};
			WarSideStatsState attacker = BuildWarSideStats(attackerCountryId, attackerWon ? 4200 : 3400);
			WarSideStatsState defender = BuildWarSideStats(defenderCountryId, attackerWon ? 3400 : 4200);
			var battles = new List<WarBattleRowState> {
				new WarBattleRowState("battle_1", "Germany__Berlin", true, winnerCountryId, WarParticipantKind.Attacker, 140, 360, 0, 0, 0),
			};
			var goldRecipients = new List<WarGoldRecipientState> {
				new WarGoldRecipientState(OwnerType.Org, "player_org", 320),
				new WarGoldRecipientState(OwnerType.Org, "rival_org", 180),
			};
			var controlDeltas = new List<WarControlDeltaState> {
				new WarControlDeltaState(loserCountryId, "player_org", 15, 62),
				new WarControlDeltaState(loserCountryId, "rival_org", -8, 10),
			};
			var transfers = new List<WarProvinceTransferState> {
				new WarProvinceTransferState("Germany__Berlin", loserCountryId, winnerCountryId),
			};
			return new WarResultSnapshotState(
				"gallery_war", attackerCountryId, defenderCountryId, winnerCountryId, loserCountryId,
				attackerWon ? 62 : -62, false, 1500, goldRecipients, controlDeltas, transfers,
				history, attacker, defender, battles);
		}

		static WarSideStatsState BuildWarSideStats(string countryId, double durability) {
			return new WarSideStatsState(
				countryId, recruitsAvailable: 4200, troopsInBattles: 1800, casualties: 320, damage: 65, durability: durability,
				damageBase: 50, damageRulerBonus: 8, damageAdvisorBonus: 7,
				durabilityBase: 45, durabilityRulerBonus: 8, durabilityAdvisorBonus: 5);
		}

		/// <summary>
		/// Comparison-column config entries for EndGameWindowView (phase 7 "seven windows" batch) -
		/// stands in for GameSettings.EndGameComparisons, which the gallery has no GameSettings asset
		/// to load.
		/// </summary>
		public static List<EndGameComparisonEntry> BuildEndGameComparisons() {
			return new List<EndGameComparisonEntry> {
				new EndGameComparisonEntry { ComparisonElementId = "historical_average", Score = 3800 },
				new EndGameComparisonEntry { ComparisonElementId = "top_ai", Score = 5200 },
			};
		}
	}
}
