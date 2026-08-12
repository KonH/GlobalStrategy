using System;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class TaskAccordionInteractionTests {
		[Fact]
		void expands_when_nothing_expanded() {
			Assert.Equal("task_a", TaskAccordionInteraction.ApplyHeaderClick(null, "task_a"));
		}

		[Fact]
		void any_header_only_collapses_when_expanded() {
			Assert.Null(TaskAccordionInteraction.ApplyHeaderClick("task_a", "task_a"));
			Assert.Null(TaskAccordionInteraction.ApplyHeaderClick("task_a", "task_b"));
		}

		[Fact]
		void select_initial_expanded_prefers_newly_appeared_tutorial() {
			var previous = Array.Empty<(string, bool)>();
			var current = new[] { ("t0", true), ("gameplay", false) };
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTutorial(previous, current, null));
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTutorial(previous, current, "gameplay"));
		}

		[Fact]
		void select_initial_expanded_clears_when_expanded_missing() {
			var previous = new[] { ("t0", true) };
			var current = new[] { ("gameplay", false) };
			Assert.Null(TaskAccordionInteraction.SelectInitialExpandedTutorial(previous, current, "t0"));
		}

		[Fact]
		void select_initial_expanded_keeps_existing_when_still_present() {
			var previous = new[] { ("t0", true) };
			var current = new[] { ("t0", true) };
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTutorial(previous, current, "t0"));
		}
	}
}
