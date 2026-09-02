using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public sealed class GoalsLeafDescriptor {
		public WinConditionHintKind Kind { get; }
		public double ConfigValue { get; }
		public TotalControlCondition? TotalControl { get; }
		public FullControlCondition? FullControl { get; }
		public ScoreGoalCondition? ScoreGoal { get; }

		GoalsLeafDescriptor(
			WinConditionHintKind kind,
			double configValue,
			TotalControlCondition? totalControl,
			FullControlCondition? fullControl,
			ScoreGoalCondition? scoreGoal) {
			Kind = kind;
			ConfigValue = configValue;
			TotalControl = totalControl;
			FullControl = fullControl;
			ScoreGoal = scoreGoal;
		}

		public static GoalsLeafDescriptor ForTotalControl(double value) {
			return new GoalsLeafDescriptor(WinConditionHintKind.TotalControl, value, new TotalControlCondition(value), null, null);
		}

		public static GoalsLeafDescriptor ForFullControl(double value) {
			return new GoalsLeafDescriptor(WinConditionHintKind.FullControlCountries, value, null, new FullControlCondition(value), null);
		}

		public static GoalsLeafDescriptor ForScoreGoal(double value) {
			return new GoalsLeafDescriptor(WinConditionHintKind.ScoreGoal, value, null, null, new ScoreGoalCondition(value));
		}

		public static GoalsLeafDescriptor ForLastOrgStanding() {
			return new GoalsLeafDescriptor(WinConditionHintKind.LastOrgStanding, 0, null, null, null);
		}
	}

	public static class GoalsProjector {
		public static List<GoalsLeafDescriptor> FlattenLeaves(CompletionConditionConfig? condition) {
			var leaves = new List<GoalsLeafDescriptor>();
			Flatten(condition, leaves);
			return leaves;
		}

		// Pull entry point for callers without a VisualStateConverter instance (the converter used
		// to flatten leaves once in its constructor and cache them in a field; a pull caller
		// re-flattens on each call instead - cheap relative to the 250ms refresh cadence, and
		// keeps the config->leaves rule itself in one place).
		public static List<GoalsOrgEntryState> Project(
			IReadOnlyWorld world,
			CompletionConditionConfig? completionCondition,
			int maxControlPool,
			ResourceQuery resources) {
			return Build(world, FlattenLeaves(completionCondition), maxControlPool, resources);
		}

		public static List<GoalsOrgEntryState> Build(
			IReadOnlyWorld world,
			IReadOnlyList<GoalsLeafDescriptor> leaves,
			int maxControlPool,
			ResourceQuery resources) {
			var organizations = new List<GoalsOrgEntryState>();
			if (leaves.Count == 0) {
				return organizations;
			}

			var availableCountryIds = GameCompletionSystem.GetAvailableCountryIds(world);
			var availableOrgIds = GameCompletionSystem.GetAvailableOrgIds(world);
			int availableCountryCount = availableCountryIds.Count;
			int totalOrgCount = CountOrganizations(world);
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (world.Has<IsOrgDestroyed>(arch.Entities[i])) {
						continue;
					}
					string orgId = orgs[i].OrganizationId;
					var context = new CompletionConditionContext(world, orgId, availableCountryIds, maxControlPool, resources);
					Dictionary<string, int>? controlByCountry = null;
					var goals = new List<GoalProgressEntryState>(leaves.Count);
					foreach (GoalsLeafDescriptor leaf in leaves) {
						double current;
						double target;
						switch (leaf.Kind) {
							case WinConditionHintKind.TotalControl:
								controlByCountry ??= OrgMetrics.GetControlByCountry(world, orgId, availableCountryIds);
								current = availableCountryCount == 0
									? 0
									: TotalControlCondition.GetCurrentFromControl(controlByCountry);
								target = leaf.TotalControl!.GetTarget(context);
								break;
							case WinConditionHintKind.FullControlCountries:
								controlByCountry ??= OrgMetrics.GetControlByCountry(world, orgId, availableCountryIds);
								current = availableCountryCount == 0
									? 0
									: FullControlCondition.GetCurrentFromControl(controlByCountry, maxControlPool);
								target = leaf.FullControl!.GetTarget(context);
								break;
							case WinConditionHintKind.ScoreGoal:
								current = leaf.ScoreGoal!.GetCurrent(context);
								target = leaf.ScoreGoal.GetTarget(context);
								break;
							case WinConditionHintKind.LastOrgStanding:
								target = System.Math.Max(0, totalOrgCount - 1);
								current = System.Math.Clamp(
									totalOrgCount - availableOrgIds.Count, 0, (int)target);
								break;
							default:
								continue;
						}
						goals.Add(new GoalProgressEntryState(
							leaf.Kind, leaf.ConfigValue, current, target, availableCountryCount));
					}
					organizations.Add(new GoalsOrgEntryState(orgId, goals));
				}
			}
			return organizations;
		}

		static int CountOrganizations(IReadOnlyWorld world) {
			int total = 0;
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				total += archetype.Count;
			}
			return total;
		}

		static void Flatten(CompletionConditionConfig? condition, List<GoalsLeafDescriptor> leaves) {
			if (condition == null) {
				return;
			}
			if (!CompletionConditionTypeParser.TryParse(condition.Type, out var type)) {
				return;
			}

			if (type == CompletionConditionType.Any) {
				foreach (var member in condition.Members ?? new List<CompletionConditionConfig>()) {
					Flatten(member, leaves);
				}
				return;
			}

			switch (type) {
				case CompletionConditionType.TotalControl:
					leaves.Add(GoalsLeafDescriptor.ForTotalControl(condition.Value));
					break;
				case CompletionConditionType.FullControlCountries:
					leaves.Add(GoalsLeafDescriptor.ForFullControl(condition.Value));
					break;
				case CompletionConditionType.ScoreGoal:
					leaves.Add(GoalsLeafDescriptor.ForScoreGoal(condition.Value));
					break;
				case CompletionConditionType.LastOrgStanding:
					leaves.Add(GoalsLeafDescriptor.ForLastOrgStanding());
					break;
			}
		}
	}
}
