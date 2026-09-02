using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the TaskCard component (PlayerTasksView's accordion item) collapsed and expanded.</summary>
	public class TaskCardGalleryBlock : GalleryBlockBase {
		static readonly List<string> _instances = new List<string> { "Task" };
		static readonly List<string> _states = new List<string> { "Collapsed", "Expanded" };

		readonly ILocalization _loc;

		public override string Id => "task-card";
		public override string Title => "Card: TaskCard";
		protected override IReadOnlyList<string> InstanceChoices => _instances;
		protected override IReadOnlyList<string> StateChoices => _states;

		public TaskCardGalleryBlock(ILocalization loc) {
			_loc = loc;
		}

		protected override void Render(VisualElement stage, string instanceId, int stateIndex) {
			TaskCardBuilder.Elements elements = TaskCardBuilder.Build();
			elements.NameLabel.text = "Strengthen the army";
			if (stateIndex == 1) {
				VisualElement body = TaskCardBuilder.AddBody(elements, "Recruit 10 more soldiers to strengthen your army.");
				TaskCardBuilder.AddRewardRow(body, $"{_loc.Get("resource.gold.name")}: 25.0");
				TaskCardBuilder.AddRewardRow(body, $"{_loc.Get("resource.recruits.name")}: 10.0");
			}
			stage.Add(elements.Item);
		}
	}
}
