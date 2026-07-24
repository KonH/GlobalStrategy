using System.Collections.Generic;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class WinConditionHintProjectorTests {
		static CompletionConditionConfig Leaf(string type, double value) {
			return new CompletionConditionConfig { Type = type, Value = value };
		}

		static CompletionConditionConfig Any(params CompletionConditionConfig[] members) {
			return new CompletionConditionConfig { Type = "any", Members = new List<CompletionConditionConfig>(members) };
		}

		[Fact]
		void single_total_control_leaf_yields_one_available_row() {
			var (isAvailable, isAlternativeGroup, rows) = WinConditionHintProjector.Build(Leaf("total_control", 0.75), 12);

			Assert.True(isAvailable);
			Assert.False(isAlternativeGroup);
			Assert.Single(rows);
			Assert.Equal(WinConditionHintKind.TotalControl, rows[0].Kind);
			Assert.Equal(0.75, rows[0].Value);
			Assert.Equal(12, rows[0].AvailableCountryCount);
		}

		[Fact]
		void single_full_control_countries_leaf_carries_the_supplied_available_country_count() {
			var (isAvailable, isAlternativeGroup, rows) = WinConditionHintProjector.Build(Leaf("full_control_countries", 5), 20);

			Assert.True(isAvailable);
			Assert.False(isAlternativeGroup);
			Assert.Single(rows);
			Assert.Equal(WinConditionHintKind.FullControlCountries, rows[0].Kind);
			Assert.Equal(5, rows[0].Value);
			Assert.Equal(20, rows[0].AvailableCountryCount);
		}

		[Fact]
		void nested_any_groups_flatten_to_configuration_order_regardless_of_depth() {
			var condition = Any(
				Leaf("total_control", 0.9),
				Any(
					Leaf("full_control_countries", 3),
					Leaf("total_control", 0.5)
				)
			);

			var (isAvailable, isAlternativeGroup, rows) = WinConditionHintProjector.Build(condition, 10);

			Assert.True(isAvailable);
			Assert.True(isAlternativeGroup);
			Assert.Equal(3, rows.Count);
			Assert.Equal(WinConditionHintKind.TotalControl, rows[0].Kind);
			Assert.Equal(0.9, rows[0].Value);
			Assert.Equal(WinConditionHintKind.FullControlCountries, rows[1].Kind);
			Assert.Equal(3, rows[1].Value);
			Assert.Equal(WinConditionHintKind.TotalControl, rows[2].Kind);
			Assert.Equal(0.5, rows[2].Value);
		}

		[Fact]
		void unsupported_leaf_types_are_skipped_without_failing_the_projection() {
			var condition = Any(
				Leaf("total_control", 0.8),
				Leaf("unsupported_type", 1)
			);

			var (isAvailable, isAlternativeGroup, rows) = WinConditionHintProjector.Build(condition, 10);

			Assert.True(isAvailable);
			Assert.False(isAlternativeGroup);
			Assert.Single(rows);
			Assert.Equal(WinConditionHintKind.TotalControl, rows[0].Kind);
		}

		[Fact]
		void null_condition_empty_any_and_all_unsupported_leaves_yield_unavailable_with_no_rows() {
			var (nullIsAvailable, nullIsAlternativeGroup, nullRows) = WinConditionHintProjector.Build(null, 10);
			Assert.False(nullIsAvailable);
			Assert.False(nullIsAlternativeGroup);
			Assert.Empty(nullRows);

			var (emptyIsAvailable, emptyIsAlternativeGroup, emptyRows) = WinConditionHintProjector.Build(Any(), 10);
			Assert.False(emptyIsAvailable);
			Assert.False(emptyIsAlternativeGroup);
			Assert.Empty(emptyRows);

			var (unsupportedIsAvailable, unsupportedIsAlternativeGroup, unsupportedRows) = WinConditionHintProjector.Build(
				Any(Leaf("unsupported_a", 1), Leaf("unsupported_b", 2)), 10);
			Assert.False(unsupportedIsAvailable);
			Assert.False(unsupportedIsAlternativeGroup);
			Assert.Empty(unsupportedRows);
		}

		[Fact]
		void any_condition_with_null_members_yields_unavailable_with_no_rows_instead_of_throwing() {
			var condition = new CompletionConditionConfig { Type = "any", Members = null! };

			var (isAvailable, isAlternativeGroup, rows) = WinConditionHintProjector.Build(condition, 10);

			Assert.False(isAvailable);
			Assert.False(isAlternativeGroup);
			Assert.Empty(rows);
		}

		[Fact]
		void is_alternative_group_is_true_only_for_two_or_more_rows() {
			var (_, singleIsAlternativeGroup, _) = WinConditionHintProjector.Build(Leaf("total_control", 0.75), 10);
			Assert.False(singleIsAlternativeGroup);

			var (_, multiIsAlternativeGroup, _) = WinConditionHintProjector.Build(
				Any(Leaf("total_control", 0.75), Leaf("full_control_countries", 5)), 10);
			Assert.True(multiIsAlternativeGroup);
		}
	}
}
