using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotControlledTests {
		static int FindOrgEntity(World world, string orgId) {
			int[] req = { TypeId<Organization>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrganizationId == orgId) { return arch.Entities[i]; }
				}
			}
			return -1;
		}

		[Fact]
		void every_org_except_the_initial_one_is_marked_bot_controlled() {
			var participants = new List<string> {
				MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.OrgC
			};
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: participants,
				initialOrganizationId: MultiOrgTestSupport.OrgA);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			int orgAEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgA);
			int orgBEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgB);
			int orgCEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgC);

			Assert.NotEqual(-1, orgAEntity);
			Assert.NotEqual(-1, orgBEntity);
			Assert.NotEqual(-1, orgCEntity);

			Assert.False(world.Has<BotControlled>(orgAEntity));
			Assert.True(world.Has<BotControlled>(orgBEntity));
			Assert.True(world.Has<BotControlled>(orgCEntity));
		}

		[Fact]
		void the_players_own_org_never_receives_bot_controlled() {
			var participants = new List<string> {
				MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.OrgC
			};
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: participants,
				initialOrganizationId: MultiOrgTestSupport.OrgB);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			int orgAEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgA);
			int orgBEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgB);
			int orgCEntity = FindOrgEntity(world, MultiOrgTestSupport.OrgC);

			Assert.True(world.Has<BotControlled>(orgAEntity));
			Assert.False(world.Has<BotControlled>(orgBEntity));
			Assert.True(world.Has<BotControlled>(orgCEntity));
		}

		[Fact]
		void bot_controlled_markers_are_stable_across_a_save_load_round_trip() {
			var participants = new List<string> {
				MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.OrgC
			};
			var storage = new InMemoryStorage();
			var serializer = new PassthroughSerializer();
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: participants,
				initialOrganizationId: MultiOrgTestSupport.OrgA,
				storage: storage,
				serializer: serializer);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var beforeMarked = new HashSet<string>();
			foreach (var orgId in participants) {
				int e = FindOrgEntity(logic.World, orgId);
				if (logic.World.Has<BotControlled>(e)) { beforeMarked.Add(orgId); }
			}

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loadedLogic = new GameLogic(ctx);
			loadedLogic.LoadState(saveName);
			loadedLogic.Update(0f);

			var afterMarked = new HashSet<string>();
			foreach (var orgId in participants) {
				int e = FindOrgEntity(loadedLogic.World, orgId);
				if (loadedLogic.World.Has<BotControlled>(e)) { afterMarked.Add(orgId); }
			}

			Assert.Equal(beforeMarked, afterMarked);

			int orgCountAfterLoad = 0;
			int[] req = { TypeId<Organization>.Value };
			foreach (var arch in loadedLogic.World.GetMatchingArchetypes(req, null)) {
				orgCountAfterLoad += arch.Count;
			}
			Assert.Equal(participants.Count, orgCountAfterLoad);
		}

		sealed class InMemoryStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new Dictionary<string, string>();
			public void Write(string path, string content) => _files[path] = content;
			public string Read(string path) => _files[path];
			public bool Exists(string path) => _files.ContainsKey(path);
			public void Delete(string path) => _files.Remove(path);
			public IReadOnlyList<string> List(string dir) {
				var result = new List<string>();
				string prefix = dir + "/";
				foreach (var key in _files.Keys) {
					if (key.StartsWith(prefix)) {
						result.Add(key.Substring(prefix.Length));
					}
				}
				return result;
			}
		}

		sealed class PassthroughSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _store = new Dictionary<string, WorldSnapshot>();
			public string LastSaveName { get; private set; } = "";

			public string Serialize(WorldSnapshot snapshot) {
				LastSaveName = snapshot.Header.SaveName;
				_store[snapshot.Header.SaveName] = snapshot;
				return snapshot.Header.SaveName;
			}

			public WorldSnapshot Deserialize(string json) => _store[json];
		}
	}
}
