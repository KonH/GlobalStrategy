using System.Linq;
using GS.Game.WebClient.Terminal;
using GS.Game.WebClient.Terminal.Suggestions;
using GS.Game.WebClient.Tests.TestSupport;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Classification tests per the spec's Tab scenarios: empty input => all commands, partial
	// command prefix => filtered + single-match detection, complete command + trailing space
	// => argument names, "key=" => value suggestions from the right provider, garbage input
	// => empty result, never a throw. Prefix filtering is case-insensitive throughout.
	public class SuggestionEngineTests {
		readonly CommandRegistry _registry = new();
		readonly SuggestionEngine _engine;

		public SuggestionEngineTests() {
			var configSource = new TestGameConfigSource();
			var localization = new FakeLocalization();
			_engine = new SuggestionEngine(_registry, new SuggestionValueResolver(configSource, localization));
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
			// Regression: after an inline command-name completion, the input holds the exact
			// registry casing with no trailing space (Complete() for CommandName never appends
			// one). A second Tab press must still advance to argument names instead of no-op'ing
			// forever on a name that has nothing left to complete.
			var result = _engine.GetSuggestions("DebugChangeGold");

			Assert.Equal(SuggestionKind.ArgumentName, result.Kind);
			var names = result.Items.Select(i => i.Value).ToList();
			Assert.Contains("OrgId", names);
			Assert.Contains("Amount", names);
		}

		[Fact]
		public void ExactCompleteCommandNameWithoutTrailingSpace_InsertsSeparatorBeforeArgumentName() {
			// Regression: the synthesized "as if a trailing space were there" classification above
			// must also synthesize the actual separating space when completing - otherwise the
			// argument name gets glued directly onto the command name with no space between them.
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
			var configSource = new TestGameConfigSource();
			var firstOrgId = configSource.Organization.Load().Organizations[0].OrganizationId;
			string partial = firstOrgId.Substring(0, System.Math.Min(3, firstOrgId.Length));

			var result = _engine.GetSuggestions($"DebugImproveOpinion OrgId={partial}");

			Assert.Equal(SuggestionKind.ArgumentValue, result.Kind);
			Assert.All(result.Items, i => Assert.StartsWith(partial, i.Value, System.StringComparison.OrdinalIgnoreCase));
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
}
