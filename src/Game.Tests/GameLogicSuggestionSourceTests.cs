using System.Linq;
using ECS;
using GS.Game.Commands.Text.Suggestions;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class GameLogicSuggestionSourceTests {
		[Fact]
		void catalogs_include_resource_definitions_live_control_id_and_collector_ids() {
			var logic = new GameLogic(MultiOrgTestSupport.BuildContext());
			logic.Update(0f);

			int extra = logic.World.Create();
			logic.World.Add(extra, new Resource { ResourceId = $"control_{MultiOrgTestSupport.HqA}", Value = 4 });

			var source = new GameLogicSuggestionSource(logic);

			Assert.Contains(ResourceDefinitions.Gold, source.ResourceIds);
			Assert.Contains(ResourceDefinitions.OrgScore, source.ResourceIds);
			Assert.Contains(source.ResourceIds, id => id.StartsWith("control_"));
			Assert.Contains(OrgScoreCollector.Id, source.CollectorIds);
			Assert.Contains(PopulationGrowthCollector.Id, source.CollectorIds);
			Assert.Contains(MultiOrgTestSupport.OrgA, source.OrgIds);
		}
	}
}
