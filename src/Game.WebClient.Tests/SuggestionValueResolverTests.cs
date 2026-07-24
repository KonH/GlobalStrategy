using System.Linq;
using GS.Game.Commands;
using GS.Game.WebClient.Terminal;
using GS.Game.WebClient.Terminal.Suggestions;
using GS.Game.WebClient.Tests.TestSupport;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Covers attribute-type-to-provider resolution for all eight ParamSuggestionAttribute
	// types (via real production ICommand parameters discovered through CommandRegistry),
	// labeled Value/Label composition against the real Assets/Configs/*.json, enum
	// auto-suggestion, and the unannotated-non-enum => empty-list fallback.
	public class SuggestionValueResolverTests {
		readonly CommandRegistry _registry = new();
		readonly TestGameConfigSource _configSource = new();
		readonly FakeLocalization _localization = new();
		readonly SuggestionValueResolver _resolver;

		public SuggestionValueResolverTests() {
			_resolver = new SuggestionValueResolver(_configSource, _localization);
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
			var countries = _configSource.Country.Load().Countries;

			Assert.Equal(countries.Count, items.Count);
			var first = countries[0];
			var match = items.Single(i => i.Value == first.CountryId);
			Assert.Equal($"[country_name.{first.CountryId}]", match.Label);
		}

		[Fact]
		public void OrgId_ResolvesAllOrganizationsLabeledByLocaleKey() {
			var parameter = FindParameter("DebugImproveOpinion", "OrgId");
			Assert.IsType<OrgIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			var organizations = _configSource.Organization.Load().Organizations;

			Assert.Equal(organizations.Count, items.Count);
			var first = organizations[0];
			var match = items.Single(i => i.Value == first.OrganizationId);
			Assert.Equal($"[organization_name.{first.OrganizationId}]", match.Label);
		}

		[Fact]
		public void ProvinceId_ResolvesAllProvincesLabeledByLocaleKey() {
			var parameter = FindParameter("DebugChangeProvinceOwner", "ProvinceId");
			Assert.IsType<ProvinceIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			var provinces = _configSource.Province.Load().Provinces;

			Assert.Equal(provinces.Count, items.Count);
			var first = provinces[0];
			var match = items.Single(i => i.Value == first.ProvinceId);
			Assert.Equal($"[province_name.{first.ProvinceId}]", match.Label);
		}

		[Fact]
		public void ActionId_ResolvesAllActionsLabeledByNameKey() {
			var parameter = FindParameter("PlayCardAction", "ActionId");
			Assert.IsType<ActionIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			var actions = _configSource.Action.Load().Actions;

			Assert.Equal(actions.Count, items.Count);
			var first = actions[0];
			var match = items.Single(i => i.Value == first.ActionId);
			Assert.Equal($"[{first.NameKey}]", match.Label);
		}

		[Fact]
		public void RoleId_ResolvesAllRolesLabeledByNameKey() {
			var parameter = FindParameter("DebugCycleCharacter", "RoleId");
			Assert.IsType<RoleIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			var roles = _configSource.Character.Load().Roles;

			Assert.Equal(roles.Count, items.Count);
			var first = roles[0];
			var match = items.Single(i => i.Value == first.RoleId);
			Assert.Equal($"[{first.NameKey}]", match.Label);
		}

		[Fact]
		public void CharacterOwnerId_ResolvesUnionOfCountriesAndOrganizations() {
			var parameter = FindParameter("DebugCycleCharacter", "OwnerId");
			Assert.IsType<CharacterOwnerIdAttribute>(parameter.Suggestion);

			var items = _resolver.Resolve(parameter);
			int countryCount = _configSource.Country.Load().Countries.Count;
			int orgCount = _configSource.Organization.Load().Organizations.Count;

			Assert.Equal(countryCount + orgCount, items.Count);
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
}
