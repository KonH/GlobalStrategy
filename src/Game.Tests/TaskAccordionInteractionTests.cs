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
	}
}
