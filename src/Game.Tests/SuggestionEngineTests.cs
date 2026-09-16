using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GS.Configs.IO;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class SuggestionValueResolverTests {
		readonly CommandRegistry _registry = new();
		readonly ISuggestionSource _source;
		readonly SuggestionValueResolver _resolver;

		public SuggestionValueResolverTests() {
			_source = FileSuggestionCatalog.Load();
			_resolver = new SuggestionValueResolver(_source, new BracketSuggestionLabels());
		}

		CommandParameter FindParameter(string commandName, string parameterName) {
			var descriptor = _registry.Find(commandName);
			Assert.NotNull(descriptor);
			return descriptor!.Parameters.Single(p => p.Name == parameterName);
		}

		[Fact]
		public void CountryId_ResolvesAllCountriesLabeledByLocaleKey() {
			var parameter = FindParameter("DebugImproveOpinion", "CountryId");
			Assert.IsType<CountryIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.CountryIds.Count, items.Count);
			string first = _source.CountryIds[0];
			var match = items.Single(i => i.Value == first);
			Assert.Equal($"[country_name.{first}]", match.Label);
		}

		[Fact]
		public void OrgId_ResolvesAllOrganizationsLabeledByLocaleKey() {
			var parameter = FindParameter("DebugImproveOpinion", "OrgId");
			Assert.IsType<OrgIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.OrgIds.Count, items.Count);
			string first = _source.OrgIds[0];
			var match = items.Single(i => i.Value == first);
			Assert.Equal($"[organization_name.{first}]", match.Label);
		}

		[Fact]
		public void ProvinceId_ResolvesAllProvincesLabeledByLocaleKey() {
			var parameter = FindParameter("DebugChangeProvinceOwner", "ProvinceId");
			Assert.IsType<ProvinceIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.ProvinceIds.Count, items.Count);
			string first = _source.ProvinceIds[0];
			var match = items.Single(i => i.Value == first);
			Assert.Equal($"[province_name.{first}]", match.Label);
		}

		[Fact]
		public void ActionId_ResolvesAllActionsLabeledByNameKey() {
			var parameter = FindParameter("PlayCardAction", "ActionId");
			Assert.IsType<ActionIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.ActionIds.Count, items.Count);
			string first = _source.ActionIds[0];
			var match = items.Single(i => i.Value == first);
			Assert.Equal($"[{_source.ActionNameKey(first)}]", match.Label);
		}

		[Fact]
		public void RoleId_ResolvesAllRolesLabeledByNameKey() {
			var parameter = FindParameter("DebugCycleCharacter", "RoleId");
			Assert.IsType<RoleIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.RoleIds.Count, items.Count);
			string first = _source.RoleIds[0];
			var match = items.Single(i => i.Value == first);
			Assert.Equal($"[{_source.RoleNameKey(first)}]", match.Label);
		}

		[Fact]
		public void CharacterOwnerId_ResolvesUnionOfCountriesAndOrganizations() {
			var parameter = FindParameter("DebugCycleCharacter", "OwnerId");
			Assert.IsType<CharacterOwnerIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			Assert.Equal(_source.CountryIds.Count + _source.OrgIds.Count, items.Count);
		}

		[Fact]
		public void LocaleId_ResolvesFixedEnRuSet() {
			var parameter = FindParameter("ChangeLocale", "Locale");
			Assert.IsType<LocaleIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);

			Assert.Equal(2, items.Count);
			Assert.Contains(items, i => i.Value == "en" && i.Label == "en");
			Assert.Contains(items, i => i.Value == "ru" && i.Label == "ru");
		}

		[Fact]
		public void OneOf_ResolvesExactLiteralValues() {
			var parameter = FindParameter("ChangeAutoSaveInterval", "Interval");
			Assert.IsType<OneOfAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);

			Assert.Equal(new[] { "daily", "monthly", "yearly" }, items.Select(i => i.Value));
			Assert.Equal(new[] { "daily", "monthly", "yearly" }, items.Select(i => i.Label));
		}

		[Fact]
		public void UnannotatedEnumParameter_AutoSuggestsEnumNames() {
			var parameter = FindParameter("ChangeLens", "Lens");
			Assert.Null(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);

			Assert.Equal(new[] { "Political", "Geographic", "Org", "Province" }, items.Select(i => i.Value));
		}

		[Fact]
		public void UnannotatedNonEnumParameter_ResolvesEmpty() {
			var parameter = FindParameter("DebugChangeGold", "Amount");
			Assert.Null(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);

			Assert.Empty(items);
		}
	}

	public class SuggestionEngineTests {
		readonly CommandRegistry _registry = new();
		readonly SuggestionEngine _engine;

		public SuggestionEngineTests() {
			_engine = new SuggestionEngine(_registry, new SuggestionValueResolver(FileSuggestionCatalog.Load(), new BracketSuggestionLabels()));
		}

		[Fact]
		public void EmptyInput_SuggestsAllCommands() {
			var result = _engine.GetSuggestions("");

			Assert.Equal(SuggestionKind.CommandName, result.Kind);
			Assert.Equal(_registry.Commands.Count, result.Items.Count);
		}

		[Fact]
		public void PartialCommandPrefix_FiltersMatchingCommands() {
			var result = _engine.GetSuggestions("DebugChange");

			Assert.Equal(SuggestionKind.CommandName, result.Kind);
			Assert.Contains(result.Items, i => i.Value == "DebugChangeGold");
			Assert.Contains(result.Items, i => i.Value == "DebugChangeProvinceOwner");
			Assert.DoesNotContain(result.Items, i => i.Value == "Pause");
		}

		[Fact]
		public void PartialCommandPrefix_IsCaseInsensitive() {
			var result = _engine.GetSuggestions("pause");

			Assert.True(result.IsSingleMatch);
			Assert.Equal("Pause", result.Items[0].Value);
		}

		[Fact]
		public void PartialCommandPrefix_SingleRemainingMatch_CompletesInline() {
			var result = _engine.GetSuggestions("DebugChangeGo");

			Assert.True(result.IsSingleMatch);
			Assert.Equal("DebugChangeGold", result.Complete("DebugChangeGo", result.Items[0].Value));
		}

		[Fact]
		public void ExactCompleteCommandNameWithoutTrailingSpace_AdvancesToArgumentNames() {
			var result = _engine.GetSuggestions("DebugChangeGold");

			Assert.Equal(SuggestionKind.ArgumentName, result.Kind);
			var names = result.Items.Select(i => i.Value).ToList();
			Assert.Contains("OrgId", names);
			Assert.Contains("Amount", names);
		}

		[Fact]
		public void ExactCompleteCommandNameWithoutTrailingSpace_InsertsSeparatorBeforeArgumentName() {
			var result = _engine.GetSuggestions("DebugChangeGold");
			var chosen = result.Items.First(i => i.Value == "OrgId");

			string completed = result.Complete("DebugChangeGold", chosen.Value);

			Assert.Equal("DebugChangeGold OrgId=", completed);
		}

		[Fact]
		public void CompleteCommandNameWithTrailingSpace_SuggestsArgumentNames() {
			var result = _engine.GetSuggestions("DebugChangeGold ");

			Assert.Equal(SuggestionKind.ArgumentName, result.Kind);
			var names = result.Items.Select(i => i.Value).ToList();
			Assert.Contains("OrgId", names);
			Assert.Contains("Amount", names);
		}

		[Fact]
		public void CompleteCommandNamePlusPartialArgument_FiltersArgumentNames() {
			var result = _engine.GetSuggestions("DebugChangeGold Am");

			Assert.Equal(SuggestionKind.ArgumentName, result.Kind);
			Assert.True(result.IsSingleMatch);
			Assert.Equal("Amount", result.Items[0].Value);
			Assert.Equal("DebugChangeGold Amount=", result.Complete("DebugChangeGold Am", result.Items[0].Value));
		}

		[Fact]
		public void ArgumentEquals_SuggestsLabeledValues() {
			var result = _engine.GetSuggestions("DebugImproveOpinion OrgId=");

			Assert.Equal(SuggestionKind.ArgumentValue, result.Kind);
			Assert.NotEmpty(result.Items);
			Assert.All(result.Items, i => Assert.StartsWith("[organization_name.", i.Label));
		}

		[Fact]
		public void ArgumentEqualsWithPartialValue_FiltersByValuePrefix() {
			var source = FileSuggestionCatalog.Load();
			var firstOrgId = source.OrgIds[0];
			string partial = firstOrgId.Substring(0, Math.Min(3, firstOrgId.Length));

			var result = _engine.GetSuggestions($"DebugImproveOpinion OrgId={partial}");

			Assert.Equal(SuggestionKind.ArgumentValue, result.Kind);
			Assert.All(result.Items, i => Assert.StartsWith(partial, i.Value, StringComparison.OrdinalIgnoreCase));
			Assert.Contains(result.Items, i => i.Value == firstOrgId);
		}

		[Fact]
		public void ArgumentValue_PickingOneInsertsUnderlyingId() {
			var result = _engine.GetSuggestions("DebugImproveOpinion OrgId=");
			var chosen = result.Items[0];

			string completed = result.Complete("DebugImproveOpinion OrgId=", chosen.Value);

			Assert.Equal($"DebugImproveOpinion OrgId={chosen.Value}", completed);
		}

		[Fact]
		public void EnumArgument_SuggestsEnumNamesWithNoAttribute() {
			var result = _engine.GetSuggestions("ChangeLens Lens=");

			Assert.Equal(SuggestionKind.ArgumentValue, result.Kind);
			Assert.Equal(new[] { "Political", "Geographic", "Org", "Province" }, result.Items.Select(i => i.Value));
		}

		[Fact]
		public void UnknownCommand_ReturnsNoneWithoutThrowing() {
			var result = _engine.GetSuggestions("TotallyNotACommand key=value");

			Assert.Equal(SuggestionKind.None, result.Kind);
			Assert.Empty(result.Items);
		}

		[Fact]
		public void UnknownArgumentName_ReturnsNoneWithoutThrowing() {
			var result = _engine.GetSuggestions("DebugChangeGold notAField=1");

			Assert.Equal(SuggestionKind.None, result.Kind);
			Assert.Empty(result.Items);
		}

		[Fact]
		public void ZeroArgCommandWithTrailingSpace_SuggestsNoArguments() {
			var result = _engine.GetSuggestions("Pause ");

			Assert.Equal(SuggestionKind.ArgumentName, result.Kind);
			Assert.Empty(result.Items);
		}
	}

	sealed class BracketSuggestionLabels : ISuggestionLabels {
		public string Country(string id, string displayName) => $"[country_name.{id}]";
		public string Org(string id, string displayName) => $"[organization_name.{id}]";
		public string Province(string id, string displayName) => $"[province_name.{id}]";
		public string Action(string id, string nameKey) => $"[{nameKey}]";
		public string Role(string id, string nameKey) => $"[{nameKey}]";
		public string Raw(string id) => id;
	}

	static class FileSuggestionCatalog {
		public static ISuggestionSource Load() {
			CountryConfig countries = LoadConfig<CountryConfig>("countries.json");
			OrganizationConfig orgs = LoadConfig<OrganizationConfig>("organizations.json");
			ProvinceConfig provinces = LoadConfig<ProvinceConfig>("provinces.json");
			ActionConfig actions = LoadConfig<ActionConfig>("actions.json");
			CharacterConfig characters = LoadConfig<CharacterConfig>("characters.json");
			ResourceConfig resources = LoadConfig<ResourceConfig>("resources.json");
			TasksConfig tasks = LoadConfig<TasksConfig>("tasks.json");

			var resourceIds = new List<string>(resources.Resources.Select(r => r.ResourceId));
			var characterIds = new List<string>();
			foreach (var pool in characters.CountryPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						characterIds.Add(entry.CharacterId);
					}
				}
			}
			foreach (var pool in characters.OrgPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						characterIds.Add(entry.CharacterId);
					}
				}
			}

			return new ConfigCatalogSuggestionSource(
				countries.Countries.Select(c => c.CountryId).ToList(),
				orgs.Organizations.Select(o => o.OrganizationId).ToList(),
				provinces.Provinces.Select(p => p.ProvinceId).ToList(),
				actions.Actions.Select(a => a.ActionId).ToList(),
				characters.Roles.Select(r => r.RoleId).ToList(),
				resourceIds,
				characterIds,
				Array.Empty<string>(),
				Array.Empty<string>(),
				Array.Empty<string>(),
				tasks.Tasks.Select(t => t.TaskId).ToList(),
				Array.Empty<string>(),
				actionNameKeys: actions.Actions.ToDictionary(a => a.ActionId, a => a.NameKey),
				roleNameKeys: characters.Roles.ToDictionary(r => r.RoleId, r => r.NameKey));
		}

		static T LoadConfig<T>(string fileName) where T : class, new() {
			return new FileConfig<T>(FindRepoRootConfigPath(fileName)).Load();
		}

		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) {
					return candidate;
				}
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}
	}
}
