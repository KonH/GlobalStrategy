using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// TaskCard component - the accordion item PlayerTasksView builds by hand: a header row that
	/// toggles an expandable body with a description and a reward-per-line list. Reads the existing
	/// tokenized .task-item family (Assets/UI/HUD/PlayerTasks/PlayerTasks.uss). PlayerTasksView
	/// switches to calling this in phase 7.
	/// </summary>
	public static class TaskCardBuilder {
		public struct Elements {
			public VisualElement Item;
			public VisualElement Header;
			public Label NameLabel;
		}

		public static Elements Build() {
			var item = new VisualElement();
			item.AddToClassList("task-item");

			var header = new VisualElement();
			header.AddToClassList("task-header");
			header.focusable = true;

			var nameLabel = new Label();
			nameLabel.AddToClassList("gs-label");
			nameLabel.AddToClassList("task-header-label");
			nameLabel.pickingMode = PickingMode.Ignore;
			header.Add(nameLabel);
			item.Add(header);

			return new Elements { Item = item, Header = header, NameLabel = nameLabel };
		}

		public static VisualElement AddBody(Elements elements, string descriptionText) {
			var body = new VisualElement();
			body.AddToClassList("task-body");

			var desc = new Label(descriptionText);
			desc.AddToClassList("gs-label");
			desc.AddToClassList("task-description");
			desc.pickingMode = PickingMode.Ignore;
			body.Add(desc);

			elements.Item.Add(body);
			return body;
		}

		public static void AddRewardRow(VisualElement body, string text) {
			var rewardRow = new VisualElement();
			rewardRow.AddToClassList("task-reward-row");

			var label = new Label(text);
			label.AddToClassList("gs-label");
			label.AddToClassList("task-reward-label");
			label.pickingMode = PickingMode.Ignore;
			rewardRow.Add(label);

			body.Add(rewardRow);
		}
	}
}
