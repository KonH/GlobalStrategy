#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Unity.UI {
	public class PlayerTasksView {
		readonly VisualElement _root;
		readonly VisualElement _list;
		readonly ILocalization _loc;
		readonly ResourceConfig _resourceConfig;
		string? _expandedTaskId;
		ActiveTasksState? _lastState;
		// Snapshot of task ids as of the previous Refresh call. ActiveTasksState.Tasks is
		// mutated in place by VisualState.Set — _lastState and the incoming `state` are the
		// same object reference by the time Refresh runs, so diffing state.Tasks against
		// _lastState.Tasks would always compare a list against itself. This standalone
		// snapshot is the only way to tell which task ids are actually new.
		List<string> _lastTaskIds = new();

		public PlayerTasksView(VisualElement root, ILocalization loc, ResourceConfig resourceConfig) {
			_root = root;
			_list = root.Q("tasks-list") ?? root;
			_loc = loc;
			_resourceConfig = resourceConfig;
		}

		public void Refresh(ActiveTasksState state) {
			List<string> previousTaskIds = _lastTaskIds;
			List<string> currentTaskIds = BuildTaskIds(state);
			_lastTaskIds = currentTaskIds;
			_lastState = state;
			_list.Clear();
			if (state == null || state.Tasks.Count == 0) {
				_expandedTaskId = null;
				_root.style.display = DisplayStyle.None;
				return;
			}

			_expandedTaskId = TaskAccordionInteraction.SelectInitialExpandedTask(
				previousTaskIds,
				currentTaskIds,
				_expandedTaskId);

			_root.style.display = DisplayStyle.Flex;
			foreach (var task in state.Tasks) {
				_list.Add(BuildTaskItem(task));
			}
		}

		static List<string> BuildTaskIds(ActiveTasksState? state) {
			var ids = new List<string>();
			if (state == null) {
				return ids;
			}
			foreach (var task in state.Tasks) {
				ids.Add(task.TaskId);
			}
			return ids;
		}

		VisualElement BuildTaskItem(ActiveTaskEntryState task) {
			var item = new VisualElement();
			item.AddToClassList("task-item");

			var header = new VisualElement();
			header.AddToClassList("task-header");
			header.focusable = true;
			string taskId = task.TaskId;
			header.OnClick(() => {
				_expandedTaskId = TaskAccordionInteraction.ApplyHeaderClick(_expandedTaskId, taskId);
				if (_lastState != null) {
					Refresh(_lastState);
				}
			});

			var nameLabel = new Label(Localize(task.NameKey));
			nameLabel.AddToClassList("gs-label");
			nameLabel.AddToClassList("task-header-label");
			nameLabel.pickingMode = PickingMode.Ignore;
			header.Add(nameLabel);
			item.Add(header);

			if (_expandedTaskId == task.TaskId) {
				var body = new VisualElement();
				body.AddToClassList("task-body");

				var desc = new Label(Localize(task.DescKey));
				desc.AddToClassList("gs-label");
				desc.AddToClassList("task-description");
				desc.pickingMode = PickingMode.Ignore;
				body.Add(desc);

				foreach (var reward in task.Rewards) {
					var rewardRow = new VisualElement();
					rewardRow.AddToClassList("task-reward-row");
					string resourceNameKey = _resourceConfig.FindResource(reward.ResourceId)?.NameKey ?? reward.ResourceId;
					string amountText = reward.Amount.ToString("F1", CultureInfo.InvariantCulture);
					var rewardLabel = new Label($"{Localize(resourceNameKey)}: {amountText}");
					rewardLabel.AddToClassList("gs-label");
					rewardLabel.AddToClassList("task-reward-label");
					rewardLabel.pickingMode = PickingMode.Ignore;
					rewardRow.Add(rewardLabel);
					body.Add(rewardRow);
				}

				item.Add(body);
			}

			return item;
		}

		string Localize(string key) {
			if (string.IsNullOrEmpty(key)) { return ""; }
			string text = _loc.Get(key);
			return string.IsNullOrEmpty(text) ? key : text;
		}
	}
}
