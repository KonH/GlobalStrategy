using System.Collections.Generic;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class EndGameComparisonProjectorTests {
		static EndGameComparisonEntry Entry(string id, double score) {
			return new EndGameComparisonEntry { ComparisonElementId = id, Score = score };
		}

		[Fact]
		void player_is_inserted_among_configured_entries_and_sorted_descending_by_score() {
			var entries = new List<EndGameComparisonEntry> {
				Entry("alpha", 10.0),
				Entry("beta", 30.0),
				Entry("gamma", 20.0)
			};

			var rows = EndGameComparisonProjector.Build(entries, "playerOrg", "Player", 25.0);

			Assert.Equal(4, rows.Count);
			Assert.Equal(new[] { "beta", "playerOrg", "gamma", "alpha" }, rows.ConvertAll(r => r.ComparisonElementId));
			Assert.Equal(new[] { 30.0, 25.0, 20.0, 10.0 }, rows.ConvertAll(r => r.Score));
		}

		[Fact]
		void tie_break_is_deterministic_by_comparison_element_id_then_player_flag() {
			var entries = new List<EndGameComparisonEntry> {
				Entry("zulu", 15.0),
				Entry("alpha", 15.0)
			};

			var rows = EndGameComparisonProjector.Build(entries, "mid", "Player", 15.0);

			Assert.Equal(new[] { "alpha", "mid", "zulu" }, rows.ConvertAll(r => r.ComparisonElementId));

			var rowsAgain = EndGameComparisonProjector.Build(entries, "mid", "Player", 15.0);
			Assert.Equal(rows.ConvertAll(r => r.ComparisonElementId), rowsAgain.ConvertAll(r => r.ComparisonElementId));
		}

		[Fact]
		void null_or_empty_entries_yield_a_single_player_only_row() {
			var rowsFromNull = EndGameComparisonProjector.Build(null!, "playerOrg", "Player", 42.0);
			Assert.Single(rowsFromNull);
			Assert.True(rowsFromNull[0].IsPlayer);
			Assert.Equal(1, rowsFromNull[0].Place);
			Assert.Equal("playerOrg", rowsFromNull[0].ComparisonElementId);
			Assert.Equal("Player", rowsFromNull[0].DisplayName);
			Assert.Equal(42.0, rowsFromNull[0].Score);

			var rowsFromEmpty = EndGameComparisonProjector.Build(new List<EndGameComparisonEntry>(), "playerOrg", "Player", 42.0);
			Assert.Single(rowsFromEmpty);
			Assert.True(rowsFromEmpty[0].IsPlayer);
		}

		[Fact]
		void scores_and_ids_pass_through_unchanged() {
			var entries = new List<EndGameComparisonEntry> {
				Entry("configured", 7.5)
			};

			var rows = EndGameComparisonProjector.Build(entries, "player", "Player Display", 99.25);

			var configuredRow = rows.Find(r => !r.IsPlayer);
			Assert.NotNull(configuredRow);
			Assert.Equal("configured", configuredRow!.ComparisonElementId);
			Assert.Equal("configured", configuredRow.DisplayName);
			Assert.Equal(7.5, configuredRow.Score);
			Assert.False(configuredRow.IsPlayer);

			var playerRow = rows.Find(r => r.IsPlayer);
			Assert.NotNull(playerRow);
			Assert.Equal("player", playerRow!.ComparisonElementId);
			Assert.Equal("Player Display", playerRow.DisplayName);
			Assert.Equal(99.25, playerRow.Score);
		}

		[Fact]
		void places_are_one_based_and_consecutive() {
			var entries = new List<EndGameComparisonEntry> {
				Entry("a", 1.0),
				Entry("b", 2.0),
				Entry("c", 3.0),
				Entry("d", 4.0)
			};

			var rows = EndGameComparisonProjector.Build(entries, "player", "Player", 2.5);

			for (int i = 0; i < rows.Count; i++) {
				Assert.Equal(i + 1, rows[i].Place);
			}
		}
	}
}
