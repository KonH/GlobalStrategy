using System;
using System.Collections.Generic;
using ECS.Viewer;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Game.WebClient.Ecs;
using GS.Game.WebClient.Services;
using GS.Main;
using Xunit;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Tests {
	public class HostModeTests {
		[Fact]
		void is_remote_detects_query_flag() {
			Assert.True(HostMode.IsRemote("http://localhost:1234/?host=remote"));
			Assert.True(HostMode.IsRemote("http://localhost:1234/game?x=1&host=remote"));
			Assert.False(HostMode.IsRemote("http://localhost:1234/game"));
			Assert.False(HostMode.IsRemote("http://localhost:1234/?host=local"));
		}

		[Fact]
		void remote_does_not_redirect_when_logic_is_null() {
			Assert.False(HostMode.ShouldRedirectToMenu("http://localhost/?host=remote", hasInProcessLogic: false));
			Assert.True(HostMode.ShouldRedirectToMenu("http://localhost/", hasInProcessLogic: false));
			Assert.False(HostMode.ShouldRedirectToMenu("http://localhost/", hasInProcessLogic: true));
		}
	}

	public class AppNavigationTests {
		[Fact]
		void leading_slash_targets_resolve_against_host_root_without_helper() {
			var resolved = new Uri(new Uri("https://konh.github.io/GlobalStrategy/"), "/org-select");
			Assert.Equal("https://konh.github.io/org-select", resolved.AbsoluteUri);
		}

		[Fact]
		void base_relative_targets_keep_github_pages_prefix() {
			Assert.Equal("org-select", AppNavigation.ToBaseRelativeTarget("/org-select"));
			Assert.Equal("game", AppNavigation.ToBaseRelativeTarget("/game"));
			Assert.Equal("game?host=remote", AppNavigation.ToBaseRelativeTarget("/game?host=remote"));
			Assert.Equal("", AppNavigation.ToBaseRelativeTarget("/"));
			Assert.Equal("", AppNavigation.ToBaseRelativeTarget(""));

			var resolved = new Uri(
				new Uri("https://konh.github.io/GlobalStrategy/"),
				AppNavigation.ToBaseRelativeTarget("/org-select"));
			Assert.Equal("https://konh.github.io/GlobalStrategy/org-select", resolved.AbsoluteUri);
		}
	}

	public class EcsSnapshotFilterTests {
		static ECS.Viewer.EntitySnapshot Entity(int id, params ECS.Viewer.ComponentSnapshot[] components) {
			return new ECS.Viewer.EntitySnapshot { Id = id, Components = new List<ECS.Viewer.ComponentSnapshot>(components) };
		}

		static ECS.Viewer.ComponentSnapshot Comp(string type, params ECS.Viewer.FieldSnapshot[] fields) {
			return new ECS.Viewer.ComponentSnapshot { TypeName = type, Fields = new List<ECS.Viewer.FieldSnapshot>(fields) };
		}

		[Fact]
		void chips_require_and_exclude_match_app_js() {
			var snapshot = new EcsWorldSnapshot {
				Entities = {
					Entity(1, Comp("Hp"), Comp("Name")),
					Entity(2, Comp("Hp")),
					Entity(12, Comp("Name"))
				}
			};
			var chips = new Dictionary<string, ComponentFilterMode> {
				["Hp"] = ComponentFilterMode.Require,
				["Name"] = ComponentFilterMode.Exclude
			};
			var visible = EcsSnapshotFilter.Apply(snapshot, chips, "", Array.Empty<FieldFilterRow>());
			Assert.Single(visible);
			Assert.Equal(2, visible[0].Id);
		}

		[Fact]
		void entity_id_substring_matches_decimal() {
			var snapshot = new EcsWorldSnapshot {
				Entities = { Entity(12), Entity(120), Entity(512), Entity(7) }
			};
			var visible = EcsSnapshotFilter.Apply(snapshot, new Dictionary<string, ComponentFilterMode>(), "12", Array.Empty<FieldFilterRow>());
			Assert.Equal(new[] { 12, 120, 512 }, System.Linq.Enumerable.Select(visible, e => e.Id));
		}

		[Fact]
		void field_filters_and_incomplete_and_invalid_regex() {
			var snapshot = new EcsWorldSnapshot {
				Entities = {
					Entity(1, Comp("Hp", new ECS.Viewer.FieldSnapshot { Name = "Value", Kind = FieldSnapshotKind.Number, Value = 10 })),
					Entity(2, Comp("Hp", new ECS.Viewer.FieldSnapshot { Name = "Value", Kind = FieldSnapshotKind.Number, Value = 50 })),
					Entity(3, Comp("Name", new ECS.Viewer.FieldSnapshot { Name = "Value", Kind = FieldSnapshotKind.String, Value = "alpha" }))
				}
			};
			var incomplete = new FieldFilterRow { ComponentType = "Hp" };
			var number = new FieldFilterRow {
				ComponentType = "Hp",
				FieldName = "Value",
				Kind = FieldSnapshotKind.Number,
				NumberMode = NumberFilterMode.MinMax,
				Min = "20",
				Max = "60"
			};
			var visible = EcsSnapshotFilter.Apply(snapshot, new Dictionary<string, ComponentFilterMode>(), "", new[] { incomplete, number });
			Assert.Single(visible);
			Assert.Equal(2, visible[0].Id);

			var regexRow = new FieldFilterRow {
				ComponentType = "Name",
				FieldName = "Value",
				Kind = FieldSnapshotKind.String,
				StringMode = StringFilterMode.Regex,
				Value = "("
			};
			var withBadRegex = EcsSnapshotFilter.Apply(snapshot, new Dictionary<string, ComponentFilterMode>(), "", new[] { regexRow });
			Assert.Equal(3, withBadRegex.Count);
			Assert.NotNull(regexRow.RegexError);
		}

		[Fact]
		void empty_bool_field_filter_is_incomplete_and_ignored() {
			var snapshot = new EcsWorldSnapshot {
				Entities = {
					Entity(1, Comp("Flag", new ECS.Viewer.FieldSnapshot { Name = "On", Kind = FieldSnapshotKind.Bool, Value = true })),
					Entity(2, Comp("Flag", new ECS.Viewer.FieldSnapshot { Name = "On", Kind = FieldSnapshotKind.Bool, Value = false }))
				}
			};
			var emptyBool = new FieldFilterRow {
				ComponentType = "Flag",
				FieldName = "On",
				Kind = FieldSnapshotKind.Bool,
				Value = ""
			};
			var visible = EcsSnapshotFilter.Apply(snapshot, new Dictionary<string, ComponentFilterMode>(), "", new[] { emptyBool });
			Assert.Equal(2, visible.Count);
		}
	}

	public class EcsInspectorCatalogTests {
		[Fact]
		void type_names_and_fields_come_from_snapshot_components() {
			var snapshot = new EcsWorldSnapshot {
				Entities = {
					new ECS.Viewer.EntitySnapshot {
						Id = 1,
						Components = {
							new ECS.Viewer.ComponentSnapshot {
								TypeName = "Hp",
								Fields = { new ECS.Viewer.FieldSnapshot { Name = "Value", Kind = FieldSnapshotKind.Number, Value = 3 } }
							},
							new ECS.Viewer.ComponentSnapshot { TypeName = "Name" }
						}
					}
				}
			};

			Assert.Equal(new[] { "Hp", "Name" }, EcsInspectorCatalog.TypeNames(snapshot));
			IReadOnlyList<ECS.Viewer.FieldSnapshot> fields = EcsInspectorCatalog.FieldsFor(snapshot, "Hp");
			Assert.Single(fields);
			Assert.Equal("Value", fields[0].Name);
			Assert.Equal("Hp, Name", EcsInspectorCatalog.ComponentTags(snapshot.Entities[0]));
		}

		[Fact]
		void ordered_type_names_put_active_filters_first_in_click_order() {
			var names = new[] { "Alpha", "Hp", "Name", "Zed" };
			var chips = new Dictionary<string, ComponentFilterMode> {
				["Name"] = ComponentFilterMode.Exclude,
				["Hp"] = ComponentFilterMode.Require
			};
			var order = new[] { "Hp", "Name" };
			Assert.Equal(new[] { "Hp", "Name", "Alpha", "Zed" }, EcsInspectorCatalog.OrderedTypeNames(names, chips, order));
		}
	}

	public class EcsWorldSnapshotJsonTests {
		[Fact]
		void parse_accepts_pascal_or_camel_entities() {
			EcsWorldSnapshot? pascal = EcsWorldSnapshotJson.Parse(
				"{\"Entities\":[{\"Id\":7,\"Components\":[{\"TypeName\":\"Hp\",\"Fields\":[]}]}]}");
			Assert.NotNull(pascal);
			Assert.Single(pascal!.Entities);
			Assert.Equal(7, pascal.Entities[0].Id);
			Assert.Equal("Hp", pascal.Entities[0].Components[0].TypeName);

			EcsWorldSnapshot? camel = EcsWorldSnapshotJson.Parse(
				"{\"entities\":[{\"id\":8,\"components\":[{\"typeName\":\"Gold\",\"fields\":[]}]}]}");
			Assert.NotNull(camel);
			Assert.Single(camel!.Entities);
			Assert.Equal(8, camel.Entities[0].Id);
			Assert.Equal("Gold", camel.Entities[0].Components[0].TypeName);
		}

		[Fact]
		void parse_rejects_html() {
			Assert.Null(EcsWorldSnapshotJson.Parse("<html>index</html>"));
		}
	}

	public class GameClientFakeTests {
		sealed class FakeClient : IGameClient {
			public bool ShowPauseMenu { get; set; }
			public bool HasSession { get; set; } = true;
			public bool IsFrozen { get; set; }
			public TimeHud? Time { get; set; } = new TimeHud { IsPaused = false, MultiplierIndex = 0, CurrentTime = DateTime.MinValue };
			public IReadOnlyList<GameLogEntry>? LogEntries { get; set; }
			public GameCompletionState? Completion { get; set; }
			public EcsWorldSnapshot? Snapshot { get; set; }
			public event Action? Changed;

			void RaiseChanged() {
				Changed?.Invoke();
			}
			public int PlayPauseCalls { get; private set; }
			public int FreezeCalls { get; private set; }
			public string? LastCommand { get; private set; }
			public string? LastSuggest { get; private set; }

			public System.Threading.Tasks.Task RefreshHudAsync() => System.Threading.Tasks.Task.CompletedTask;
			public System.Threading.Tasks.Task RefreshSnapshotAsync() => System.Threading.Tasks.Task.CompletedTask;
			public System.Threading.Tasks.Task TogglePlayPauseAsync() {
				PlayPauseCalls++;
				return System.Threading.Tasks.Task.CompletedTask;
			}
			public System.Threading.Tasks.Task SetSpeedAsync(int index) => System.Threading.Tasks.Task.CompletedTask;
			public System.Threading.Tasks.Task SetFrozenAsync(bool frozen) {
				FreezeCalls++;
				IsFrozen = frozen;
				RaiseChanged();
				return System.Threading.Tasks.Task.CompletedTask;
			}
			public System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(string line) {
				LastCommand = line;
				return System.Threading.Tasks.Task.FromResult(ExecutionResult.Ok("ok"));
			}
			public System.Threading.Tasks.Task<SuggestionResult> SuggestAsync(string input) {
				LastSuggest = input;
				return System.Threading.Tasks.Task.FromResult(SuggestionResult.None);
			}
			public System.Threading.Tasks.Task PatchFieldAsync(int entityId, string typeName, string fieldName, string rawValue) => System.Threading.Tasks.Task.CompletedTask;
			public IReadOnlyList<string> DomainIds(string? domainIdKind, string? ownerType) => Array.Empty<string>();
		}

		[Fact]
		async System.Threading.Tasks.Task pause_and_freeze_are_distinct_on_fake_client() {
			var client = new FakeClient { ShowPauseMenu = true };
			Assert.True(client.ShowPauseMenu);
			await client.TogglePlayPauseAsync();
			await client.SetFrozenAsync(true);
			Assert.Equal(1, client.PlayPauseCalls);
			Assert.Equal(1, client.FreezeCalls);
			Assert.True(client.IsFrozen);
			await client.ExecuteAsync("Pause");
			await client.SuggestAsync("Pau");
			Assert.Equal("Pause", client.LastCommand);
			Assert.Equal("Pau", client.LastSuggest);
		}
	}
}
