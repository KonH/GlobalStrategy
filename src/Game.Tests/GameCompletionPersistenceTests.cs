using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class GameCompletionPersistenceTests {
		static readonly IReadOnlyList<string> Participants = new[] {
			MultiOrgTestSupport.OrgA,
			MultiOrgTestSupport.OrgB
		};

		sealed class MemoryStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new Dictionary<string, string>();
			public void Write(string path, string content) => _files[path] = content;
			public string Read(string path) => _files[path];
			public bool Exists(string path) => _files.ContainsKey(path);
			public void Delete(string path) => _files.Remove(path);
			public IReadOnlyList<string> List(string dir) => Array.Empty<string>();
		}

		sealed class SnapshotSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _snapshots = new Dictionary<string, WorldSnapshot>();
			public string LastSaveName { get; private set; } = "";

			public string Serialize(WorldSnapshot snapshot) {
				LastSaveName = snapshot.Header.SaveName;
				_snapshots[LastSaveName] = snapshot;
				return LastSaveName;
			}

			public WorldSnapshot Deserialize(string json) => _snapshots[json];
			public WorldSnapshot Get(string saveName) => _snapshots[saveName];
		}

		static GameLogic BuildLogic(MemoryStorage storage, SnapshotSerializer serializer,
			IReadOnlyList<string>? participants = null) {
			return new GameLogic(MultiOrgTestSupport.BuildContext(
				participants ?? Participants,
				rngSeed: 73,
				storage: storage,
				serializer: serializer));
		}

		static string Save(GameLogic logic, SnapshotSerializer serializer) {
			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			return serializer.LastSaveName;
		}

		static void GiveTotalControl(GameLogic logic, string organizationId) {
			foreach (string countryId in new[] {
				MultiOrgTestSupport.HqA,
				MultiOrgTestSupport.HqB,
				MultiOrgTestSupport.ExtraCountry1,
				MultiOrgTestSupport.ExtraCountry2
			}) {
				logic.Commands.Push(new ChangeControlCommand {
					OrgId = organizationId,
					CountryId = countryId,
					Delta = logic.MaxControlPool
				});
			}
		}

		static Dictionary<string, OrganizationGameOutcome> GetOutcomes(World world) {
			var result = new Dictionary<string, OrganizationGameOutcome>(StringComparer.Ordinal);
			int[] required = { TypeId<Organization>.Value, TypeId<OrganizationGameOutcome>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				OrganizationGameOutcome[] outcomes = archetype.GetColumn<OrganizationGameOutcome>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add(organizations[i].OrganizationId, outcomes[i]);
				}
			}
			return result;
		}

		static double GetGold(World world) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					if (owners[i].OwnerId == MultiOrgTestSupport.OrgA && resources[i].ResourceId == "gold") {
						return resources[i].Value;
					}
				}
			}
			throw new InvalidOperationException("Illuminati gold resource was not found.");
		}

		static int FindOrganization(World world, string organizationId) {
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				for (int i = 0; i < archetype.Count; i++) {
					if (organizations[i].OrganizationId == organizationId) {
						return archetype.Entities[i];
					}
				}
			}
			throw new InvalidOperationException($"Organization '{organizationId}' was not found.");
		}

		[Fact]
		void in_progress_round_trip_preserves_completion_and_participant_order() {
			var storage = new MemoryStorage();
			var serializer = new SnapshotSerializer();
			GameLogic source = BuildLogic(storage, serializer);
			source.Update(0f);
			string saveName = Save(source, serializer);

			GameLogic loaded = BuildLogic(storage, serializer);
			loaded.LoadState(saveName);

			Assert.False(loaded.IsCompleted);
			Assert.Equal(GameResult.InProgress, loaded.VisualState.GameCompletion.Result);
			Dictionary<string, OrganizationGameOutcome> outcomes = GetOutcomes(loaded.World);
			Assert.Equal(0, outcomes[MultiOrgTestSupport.OrgA].ParticipationOrder);
			Assert.Equal(1, outcomes[MultiOrgTestSupport.OrgB].ParticipationOrder);
			Assert.All(outcomes.Values, outcome => Assert.Equal(OrganizationGameResult.InProgress, outcome.Result));
		}

		[Fact]
		void terminal_round_trip_immediately_projects_winner_and_freezes_later_updates() {
			var storage = new MemoryStorage();
			var serializer = new SnapshotSerializer();
			GameLogic source = BuildLogic(storage, serializer);
			source.Update(0f);
			GiveTotalControl(source, MultiOrgTestSupport.OrgB);
			source.Update(0f);
			string saveName = Save(source, serializer);

			GameLogic loaded = BuildLogic(storage, serializer);
			loaded.LoadState(saveName);
			double gold = GetGold(loaded.World);
			DateTime time = loaded.VisualState.Time.CurrentTime;

			Assert.True(loaded.IsCompleted);
			Assert.Equal(MultiOrgTestSupport.OrgB, loaded.VisualState.GameCompletion.WinnerOrganizationId);
			Assert.Equal(GameResult.Lose, loaded.VisualState.GameCompletion.Result);
			Dictionary<string, OrganizationGameOutcome> outcomes = GetOutcomes(loaded.World);
			Assert.Equal(OrganizationGameResult.Loser, outcomes[MultiOrgTestSupport.OrgA].Result);
			Assert.Equal(OrganizationGameResult.Winner, outcomes[MultiOrgTestSupport.OrgB].Result);

			loaded.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 500.0 });
			loaded.Update(2400f);

			Assert.Equal(gold, GetGold(loaded.World));
			Assert.Equal(time, loaded.VisualState.Time.CurrentTime);
			Assert.Equal(outcomes, GetOutcomes(loaded.World));
		}

		[Fact]
		void legacy_snapshot_reconstructs_completion_and_order_across_organization_archetypes() {
			var storage = new MemoryStorage();
			var serializer = new SnapshotSerializer();
			GameLogic source = BuildLogic(storage, serializer);
			source.Update(0f);
			source.World.Add(FindOrganization(source.World, MultiOrgTestSupport.OrgB), new IsSelected());
			string saveName = Save(source, serializer);
			WorldSnapshot snapshot = serializer.Get(saveName);
			foreach (EntitySnapshot entity in snapshot.Entities) {
				entity.Components.Remove(typeof(GameCompletion).FullName!);
				entity.Components.Remove(typeof(OrganizationGameOutcome).FullName!);
			}

			GameLogic loaded = BuildLogic(storage, serializer, new[] {
				MultiOrgTestSupport.OrgB,
				MultiOrgTestSupport.OrgA
			});
			loaded.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 250.0 });
			loaded.LoadState(saveName);
			double gold = GetGold(loaded.World);
			loaded.Update(0f);

			Assert.False(loaded.IsCompleted);
			Assert.Equal(GameResult.InProgress, loaded.VisualState.GameCompletion.Result);
			Dictionary<string, OrganizationGameOutcome> outcomes = GetOutcomes(loaded.World);
			Assert.Equal(0, outcomes[MultiOrgTestSupport.OrgB].ParticipationOrder);
			Assert.Equal(1, outcomes[MultiOrgTestSupport.OrgA].ParticipationOrder);
			Assert.All(outcomes.Values, outcome => Assert.Equal(OrganizationGameResult.InProgress, outcome.Result));
			Assert.Equal(gold, GetGold(loaded.World));
		}
	}
}
