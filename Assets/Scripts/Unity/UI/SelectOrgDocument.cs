using GS.Main;
using GS.Unity.Common;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(PanelRenderer))]
	public class SelectOrgDocument : MonoBehaviour {
		SelectOrgLogic _logic;
		SceneLoader _sceneLoader;
		ILocalization _localization;
		OrgVisualConfig _orgVisualConfig;
		PanelRenderer _doc;
		SelectOrgView _view;
		UIPointerState _pointerState;

		[Inject]
		void Construct(SelectOrgLogic logic, SceneLoader sceneLoader, ILocalization localization, OrgVisualConfig orgVisualConfig, UIPointerState pointerState) {
			_logic = logic;
			_sceneLoader = sceneLoader;
			_localization = localization;
			_orgVisualConfig = orgVisualConfig;
			_pointerState = pointerState;
		}

		void Awake() {
			_doc = GetComponent<PanelRenderer>();
			_doc.RegisterUIReloadCallback(OnUIReload);
		}

		void OnDestroy() {
			if (_doc != null) {
				_doc.UnregisterUIReloadCallback(OnUIReload);
			}
		}

		void OnUIReload(PanelRenderer _, VisualElement rootElement) {
			Bind(rootElement);
		}

		void Bind(VisualElement root) {
			if (root == null || _logic == null) {
				return;
			}
			_pointerState.RuntimePanel = root.panel;
			_view = new SelectOrgView(root, _localization);
			_view.BtnBack.OnClick(() => _sceneLoader.LoadMainMenu());
			_view.BtnStart.OnClick(OnStartGame);
			_view.BtnStart.SetEnabled(false);
			RefreshTexts();
			RefreshUI();
		}

		void Start() {
			if (_view == null) {
				Bind(PanelRendererRoot.Get(_doc));
			}
			_logic.VisualState.SelectedOrganization.PropertyChanged += (_, _) => RefreshUI();
			RefreshUI();
		}

		void RefreshTexts() {
			_view?.RefreshTexts();
			_view?.RefreshGoalHint(_logic.VisualState.WinConditionHint);
		}

		void Update() {
			_logic.Update();
		}

		void RefreshUI() {
			if (_view == null) {
				return;
			}
			var state = _logic.VisualState.SelectedOrganization;
			int baseControl = state.IsValid ? _logic.GetBaseControl(state.OrgId) : 0;
			double income = state.IsValid ? _logic.ComputeBaseControlIncome(state.OrgId) : 0;
			_view.Refresh(state, _orgVisualConfig, baseControl, income);
		}

		void OnStartGame() {
			var orgState = _logic.VisualState.SelectedOrganization;
			if (orgState.IsValid) {
				_sceneLoader.LoadGame(organizationId: orgState.OrgId);
			}
		}
	}
}
