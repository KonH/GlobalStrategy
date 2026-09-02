using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Shared scaffolding for a gallery block: an instance dropdown, a state dropdown, dropdown-width
	/// fitting, and persistence of both selections through the GalleryBlockState the host passes to
	/// Build. Concrete blocks only supply their instance ids, state names and a Render callback.
	/// </summary>
	public abstract class GalleryBlockBase : IGalleryBlock {
		public abstract string Id { get; }
		public abstract string Title { get; }

		protected abstract IReadOnlyList<string> InstanceChoices { get; }
		protected abstract IReadOnlyList<string> StateChoices { get; }

		protected virtual string InstanceLabel => "Instance";
		protected virtual string StateLabel => "State";

		/// <summary>
		/// True for a block previewing a whole window/panel/menu/overlay - authored in its real
		/// UXML as position:Absolute, width:100%/height:100% against a full-screen root - so instead
		/// of the small inline ".gallery-stage" atoms/rows/cards use, it gets only a "Preview" button
		/// here; clicking it hands the block off to GalleryDocument's full-screen focus mode (see
		/// EnterFocusModeRequested below), which renders it as a genuine top-level full-panel root.
		/// </summary>
		protected virtual bool IsFullSurface => false;

		protected abstract void Render(VisualElement stage, string instanceId, int stateIndex);

		/// <summary>
		/// Set by GalleryDocument.BuildFoldoutFor before Build runs, so a full-surface block's
		/// Preview button can hand control back to the host without GalleryBlockBase holding a
		/// hard reference to the concrete GalleryDocument type.
		/// </summary>
		public Action<GalleryBlockBase, GalleryBlockState> EnterFocusModeRequested { get; set; }

		public void Build(VisualElement content, GalleryBlockState state) {
			var controls = new VisualElement();
			controls.AddToClassList("gallery-controls");
			controls.AddToClassList("gs-panel");
			content.Add(controls);

			var instanceDropdown = new DropdownField(InstanceLabel);
			instanceDropdown.AddToClassList("gallery-dropdown");
			var stateDropdown = new DropdownField(StateLabel);
			stateDropdown.AddToClassList("gallery-dropdown");
			controls.Add(instanceDropdown);
			controls.Add(stateDropdown);

			VisualElement stage = null;
			if (IsFullSurface) {
				var previewButton = new Button { text = "Preview" };
				previewButton.AddToClassList("gs-btn");
				previewButton.AddToClassList("gs-btn--small");
				previewButton.AddToClassList("gallery-preview-button");
				previewButton.OnClick(() => EnterFocusModeRequested?.Invoke(this, state));
				controls.Add(previewButton);
			} else {
				stage = new VisualElement();
				stage.AddToClassList("gallery-stage");
				content.Add(stage);
			}

			IReadOnlyList<string> instanceChoices = InstanceChoices ?? Array.Empty<string>();
			IReadOnlyList<string> stateChoices = StateChoices ?? Array.Empty<string>();

			instanceDropdown.choices = new List<string>(instanceChoices);
			int selectedInstance = IndexOf(instanceChoices, state.Selection1);
			instanceDropdown.index = selectedInstance >= 0 ? selectedInstance : (instanceChoices.Count > 0 ? 0 : -1);

			stateDropdown.choices = new List<string>(stateChoices);
			stateDropdown.index = stateChoices.Count > 0 ? Mathf.Clamp(state.Selection2, 0, stateChoices.Count - 1) : -1;

			GalleryDocument.FitDropdownToWidestChoice(instanceDropdown);
			GalleryDocument.FitDropdownToWidestChoice(stateDropdown);

			// For a full-surface block there is no inline stage to update any more - a dropdown
			// change just persists the new selection into `state` so the next Preview click (or the
			// focus bar's own re-render) uses it. For an atom/row/card block this still re-renders
			// the inline stage live, exactly as before.
			void RenderCurrent() {
				if (stage == null) {
					return;
				}
				stage.Clear();
				if (instanceDropdown.index < 0) {
					return;
				}
				Render(stage, instanceDropdown.value, Mathf.Max(stateDropdown.index, 0));
			}

			// Registered after indices are assigned above, so restoring a selection does not
			// re-enter these handlers.
			instanceDropdown.RegisterValueChangedCallback(_ => {
				state.Selection1 = instanceDropdown.value ?? "";
				RenderCurrent();
			});
			stateDropdown.RegisterValueChangedCallback(_ => {
				state.Selection2 = stateDropdown.index;
				RenderCurrent();
			});

			RenderCurrent();
		}

		/// <summary>
		/// Renders this block's current persisted selection into an external container - used by
		/// GalleryDocument's full-screen focus mode, which owns a single shared content container
		/// outside any block's own Foldout. Falls back to the first instance choice if nothing is
		/// selected yet (e.g. Preview clicked before touching the dropdowns).
		/// </summary>
		public void RenderInto(VisualElement container, GalleryBlockState state) {
			if (container == null || state == null) {
				return;
			}
			container.Clear();
			IReadOnlyList<string> instanceChoices = InstanceChoices ?? Array.Empty<string>();
			string instanceId = state.Selection1;
			if (string.IsNullOrEmpty(instanceId) || IndexOf(instanceChoices, instanceId) < 0) {
				instanceId = instanceChoices.Count > 0 ? instanceChoices[0] : null;
			}
			if (string.IsNullOrEmpty(instanceId)) {
				return;
			}
			Render(container, instanceId, Mathf.Max(state.Selection2, 0));
		}

		static int IndexOf(IReadOnlyList<string> list, string value) {
			for (int i = 0; i < list.Count; i++) {
				if (list[i] == value) {
					return i;
				}
			}
			return -1;
		}
	}
}
