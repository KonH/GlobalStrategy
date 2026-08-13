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
		void select_initial_expanded_prefers_newly_appeared_task() {
			var previous = Array.Empty<string>();
			var current = new[] { "t0", "gameplay" };
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTask(previous, current, null));
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTask(previous, current, "gameplay"));
		}

		[Fact]
		void select_initial_expanded_prefers_newly_appeared_non_tutorial_task() {
			var previous = new[] { "t0" };
			var current = new[] { "t0", "gameplay" };
			Assert.Equal("gameplay", TaskAccordionInteraction.SelectInitialExpandedTask(previous, current, "t0"));
		}

		[Fact]
		void select_initial_expanded_clears_when_expanded_missing() {
			var previous = new[] { "t0" };
			var current = new[] { "gameplay" };
			Assert.Null(TaskAccordionInteraction.SelectInitialExpandedTask(previous, current, "t0"));
		}

		[Fact]
		void select_initial_expanded_keeps_existing_when_still_present() {
			var previous = new[] { "t0" };
			var current = new[] { "t0" };
			Assert.Equal("t0", TaskAccordionInteraction.SelectInitialExpandedTask(previous, current, "t0"));
		}
	}
}
