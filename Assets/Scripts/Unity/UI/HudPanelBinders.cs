using System;
using System.ComponentModel;
using GS.Main;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Phase 7 (Docs/Specs/26_08_28_16_ui-refactoring) HUD panel binders. Each binder owns exactly
	/// the subscribe/refresh pair(s) for one HUD panel's slice(s) of VisualState, replacing the
	/// subscribe lines that used to be centralised in HUDDocument.OnEnable/OnDisable. HUDDocument
	/// still owns the shared multi-view refresh methods (RefreshCountryViews, RefreshProvinceInfoView)
	/// and the OnEnable-time initial refresh sequence - only the PropertyChanged wiring moved here.
	/// </summary>
	interface IHudPanelBinder {
		void Subscribe();
		void Unsubscribe();
	}

	sealed class SelectedCountryBinder : IHudPanelBinder {
		readonly SelectedCountryState _state;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public SelectedCountryBinder(SelectedCountryState state, Action refreshCountryViews) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => _refreshCountryViews();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class PlayerOrganizationBinder : IHudPanelBinder {
		readonly PlayerOrganizationState _state;
		readonly Action _closeOrgPanelIfDestroyed;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public PlayerOrganizationBinder(PlayerOrganizationState state, Action closeOrgPanelIfDestroyed, Action refreshCountryViews) {
			_state = state;
			_closeOrgPanelIfDestroyed = closeOrgPanelIfDestroyed;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => {
				if (_state.IsDestroyed) {
					_closeOrgPanelIfDestroyed();
				}
				_refreshCountryViews();
			};
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class TimeBinder : IHudPanelBinder {
		readonly TimeState _state;
		readonly TimeView _view;
		readonly PropertyChangedEventHandler _handler;

		public TimeBinder(TimeState state, TimeView view) {
			_state = state;
			_view = view;
			_handler = (s, e) => _view.Refresh(_state);
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class LocaleBinder : IHudPanelBinder {
		readonly LocaleState _state;
		readonly Action _onLocaleChanged;
		readonly PropertyChangedEventHandler _handler;

		public LocaleBinder(LocaleState state, Action onLocaleChanged) {
			_state = state;
			_onLocaleChanged = onLocaleChanged;
			_handler = (s, e) => _onLocaleChanged();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	/// <summary>Owns all three "resources" panels' subscribe/refresh pairs: the player org's own
	/// resources, the selected country's resources, and the org-lens dominant org's resources.</summary>
	sealed class ResourcesBinder : IHudPanelBinder {
		readonly VisualState _state;
		readonly PlayerOrgView _playerOrgView;
		readonly CountryInfoView _countryInfo;
		readonly OrgLensCountryView _orgLensCountryView;
		readonly PropertyChangedEventHandler _playerHandler;
		readonly PropertyChangedEventHandler _selectedHandler;
		readonly PropertyChangedEventHandler _orgLensHandler;

		public ResourcesBinder(VisualState state, PlayerOrgView playerOrgView, CountryInfoView countryInfo, OrgLensCountryView orgLensCountryView) {
			_state = state;
			_playerOrgView = playerOrgView;
			_countryInfo = countryInfo;
			_orgLensCountryView = orgLensCountryView;
			_playerHandler = (s, e) => {
				_playerOrgView?.Refresh(_state.PlayerOrganization, _state.PlayerOrganization.Resources);
				_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
			};
			_selectedHandler = (s, e) => {
				_countryInfo?.Refresh(_state.SelectedCountry, _state.SelectedCountry.Resources, _state.SelectedCountry.Control, _state.SelectedCountry.Characters, _state.SelectedCountry.CountryActions, _state.PlayerOrganization.Resources);
			};
			_orgLensHandler = (s, e) => {
				if (_state.MapLens.Lens != GS.Game.Common.MapLens.Org) {
					return;
				}
				_orgLensCountryView?.Refresh(_state.SelectedCountry, _state.OrgMap, _state.SelectedCountry.Control, _state.OrgLensOrganizationResources);
			};
		}

		public void Subscribe() {
			_state.PlayerOrganization.Resources.PropertyChanged += _playerHandler;
			_state.SelectedCountry.Resources.PropertyChanged += _selectedHandler;
			_state.OrgLensOrganizationResources.PropertyChanged += _orgLensHandler;
		}

		public void Unsubscribe() {
			_state.PlayerOrganization.Resources.PropertyChanged -= _playerHandler;
			_state.SelectedCountry.Resources.PropertyChanged -= _selectedHandler;
			_state.OrgLensOrganizationResources.PropertyChanged -= _orgLensHandler;
		}
	}

	/// <summary>Owns the selected country's control total and its per-frame used-control tick.</summary>
	sealed class ControlBinder : IHudPanelBinder {
		readonly CountryControlState _control;
		readonly CountryInfoView _countryInfo;
		readonly Func<bool> _isCardPlaying;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _controlHandler;
		readonly PropertyChangedEventHandler _tickHandler;

		public ControlBinder(CountryControlState control, CountryInfoView countryInfo, Func<bool> isCardPlaying, Action refreshCountryViews) {
			_control = control;
			_countryInfo = countryInfo;
			_isCardPlaying = isCardPlaying;
			_refreshCountryViews = refreshCountryViews;
			_controlHandler = (s, e) => {
				if (_isCardPlaying()) { return; }
				_refreshCountryViews();
			};
			_tickHandler = (s, e) => _countryInfo?.RefreshUsedControl();
		}

		public void Subscribe() {
			_control.PropertyChanged += _controlHandler;
			_control.UsedControl.PropertyChanged += _tickHandler;
		}

		public void Unsubscribe() {
			_control.PropertyChanged -= _controlHandler;
			_control.UsedControl.PropertyChanged -= _tickHandler;
		}
	}

	sealed class CharactersBinder : IHudPanelBinder {
		readonly CountryCharactersState _state;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public CharactersBinder(CountryCharactersState state, Action refreshCountryViews) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => _refreshCountryViews();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class CountryActionsBinder : IHudPanelBinder {
		readonly CountryActionsState _state;
		readonly Action _refreshCountryViews;
		readonly Action _restorePendingOfferIfIdle;
		readonly PropertyChangedEventHandler _handler;

		public CountryActionsBinder(CountryActionsState state, Action refreshCountryViews, Action restorePendingOfferIfIdle) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_restorePendingOfferIfIdle = restorePendingOfferIfIdle;
			_handler = (s, e) => {
				_refreshCountryViews();
				_restorePendingOfferIfIdle();
			};
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class RelationsBinder : IHudPanelBinder {
		readonly CountryRelationsState _state;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public RelationsBinder(CountryRelationsState state, Action refreshCountryViews) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => _refreshCountryViews();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class WarsBinder : IHudPanelBinder {
		readonly CountryWarsState _state;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public WarsBinder(CountryWarsState state, Action refreshCountryViews) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => _refreshCountryViews();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class MapLensBinder : IHudPanelBinder {
		readonly MapLensState _state;
		readonly LensSwitcherView _lensSwitcher;
		readonly Action _refreshCountryViews;
		readonly Action _refreshProvinceInfoView;
		readonly PropertyChangedEventHandler _handler;

		public MapLensBinder(MapLensState state, LensSwitcherView lensSwitcher, Action refreshCountryViews, Action refreshProvinceInfoView) {
			_state = state;
			_lensSwitcher = lensSwitcher;
			_refreshCountryViews = refreshCountryViews;
			_refreshProvinceInfoView = refreshProvinceInfoView;
			_handler = (s, e) => {
				_lensSwitcher?.Refresh(_state.Lens);
				_refreshCountryViews();
				_refreshProvinceInfoView();
			};
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class OrgMapBinder : IHudPanelBinder {
		readonly OrgMapState _state;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public OrgMapBinder(OrgMapState state, Action refreshCountryViews) {
			_state = state;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => _refreshCountryViews();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	/// <summary>Owns the selected-province panel's identity change and its own resources change.</summary>
	sealed class SelectedProvinceBinder : IHudPanelBinder {
		readonly SelectedProvinceState _state;
		readonly Action _refreshProvinceInfoView;
		readonly PropertyChangedEventHandler _selectedHandler;
		readonly PropertyChangedEventHandler _resourcesHandler;

		public SelectedProvinceBinder(SelectedProvinceState state, Action refreshProvinceInfoView) {
			_state = state;
			_refreshProvinceInfoView = refreshProvinceInfoView;
			_selectedHandler = (s, e) => _refreshProvinceInfoView();
			_resourcesHandler = (s, e) => _refreshProvinceInfoView();
		}

		public void Subscribe() {
			_state.PropertyChanged += _selectedHandler;
			_state.Resources.PropertyChanged += _resourcesHandler;
		}

		public void Unsubscribe() {
			_state.PropertyChanged -= _selectedHandler;
			_state.Resources.PropertyChanged -= _resourcesHandler;
		}
	}

	sealed class ProvinceOwnershipBinder : IHudPanelBinder {
		readonly ProvinceOwnershipState _state;
		readonly Action _refreshProvinceInfoView;
		readonly PropertyChangedEventHandler _handler;

		public ProvinceOwnershipBinder(ProvinceOwnershipState state, Action refreshProvinceInfoView) {
			_state = state;
			_refreshProvinceInfoView = refreshProvinceInfoView;
			_handler = (s, e) => _refreshProvinceInfoView();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class ProvinceOccupationBinder : IHudPanelBinder {
		readonly ProvinceOccupationState _state;
		readonly Action _refreshProvinceInfoView;
		readonly PropertyChangedEventHandler _handler;

		public ProvinceOccupationBinder(ProvinceOccupationState state, Action refreshProvinceInfoView) {
			_state = state;
			_refreshProvinceInfoView = refreshProvinceInfoView;
			_handler = (s, e) => _refreshProvinceInfoView();
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class GameLogBinder : IHudPanelBinder {
		readonly GameLogState _state;
		readonly ActionLogView _actionLog;
		readonly Action _notifyNewLogEntries;
		readonly PropertyChangedEventHandler _handler;

		public GameLogBinder(GameLogState state, ActionLogView actionLog, Action notifyNewLogEntries) {
			_state = state;
			_actionLog = actionLog;
			_notifyNewLogEntries = notifyNewLogEntries;
			_handler = (s, e) => {
				_actionLog?.Refresh(_state);
				_notifyNewLogEntries();
			};
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	sealed class WarIconsBinder : IHudPanelBinder {
		readonly WarIconsState _state;
		readonly WarIconsView _view;
		readonly PropertyChangedEventHandler _handler;

		public WarIconsBinder(WarIconsState state, WarIconsView view) {
			_state = state;
			_view = view;
			_handler = (s, e) => _view?.Refresh(_state);
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	/// <summary>Owns the player-tasks accordion and the tutorial-highlight arrow - both are driven
	/// by the same ActiveTasksState change.</summary>
	sealed class ActiveTasksBinder : IHudPanelBinder {
		readonly ActiveTasksState _state;
		readonly PlayerTasksView _tasksView;
		readonly TutorialHighlightView _highlightView;
		readonly PropertyChangedEventHandler _handler;

		public ActiveTasksBinder(ActiveTasksState state, PlayerTasksView tasksView, TutorialHighlightView highlightView) {
			_state = state;
			_tasksView = tasksView;
			_highlightView = highlightView;
			_handler = (s, e) => {
				_tasksView?.Refresh(_state);
				_highlightView?.Refresh(_state);
			};
		}

		public void Subscribe() { _state.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.PropertyChanged -= _handler; }
	}

	/// <summary>Owns the transient gold-barrier reaction to the player org's last-frame resource
	/// effects. Needs the full VisualState (player org validity/resources) plus a root element to
	/// schedule the delayed force-refresh - see HUDDocument's original HandleLastFrameEffectsChanged
	/// for why the delayed refresh exists.</summary>
	sealed class LastFrameEffectsBinder : IHudPanelBinder {
		readonly VisualState _state;
		readonly Func<bool> _isCardPlaying;
		readonly VisualElement _root;
		readonly Action _refreshCountryViews;
		readonly PropertyChangedEventHandler _handler;

		public LastFrameEffectsBinder(VisualState state, Func<bool> isCardPlaying, VisualElement root, Action refreshCountryViews) {
			_state = state;
			_isCardPlaying = isCardPlaying;
			_root = root;
			_refreshCountryViews = refreshCountryViews;
			_handler = (s, e) => Handle();
		}

		void Handle() {
			if (_state == null || _state.LastFrameEffects.Effects.Count == 0) { return; }
			// See HUDDocument's own comment history for why this barrier exists: CardPlayAnimator
			// already owns a gold barrier for an in-flight card play, so adding a second one here
			// on top of it would stack both offsets on the same AnimatableDouble.
			if (_isCardPlaying()) { return; }
			if (!_state.PlayerOrganization.IsValid) { return; }

			string playerOrgId = _state.PlayerOrganization.OrgId;
			bool createdBarrier = false;
			foreach (var effect in _state.LastFrameEffects.Effects) {
				if (effect.OwnerId != playerOrgId) { continue; }
				if (effect.ResourceId != GS.Game.Configs.ResourceDefinitions.Gold) { continue; }
				AnimatableDouble goldAnimatable = null;
				foreach (var res in _state.PlayerOrganization.Resources.Resources) {
					if (res.ResourceId == GS.Game.Configs.ResourceDefinitions.Gold) { goldAnimatable = res.Value; break; }
				}
				if (goldAnimatable == null) { continue; }
				var barrier = goldAnimatable.Hold(-effect.Amount);
				barrier.Release(3.0f);
				createdBarrier = true;
			}

			// Unlike CardPlayAnimator's barriers (awaited, then force-refreshed via
			// OnCardPlayComplete), this barrier just decays on its own via AnimatableDouble.Tick.
			// Force a refresh explicitly once it has had time to fully decay.
			if (createdBarrier && _root != null) {
				_root.schedule.Execute(_refreshCountryViews).StartingIn(3100);
			}
		}

		public void Subscribe() { _state.LastFrameEffects.PropertyChanged += _handler; }
		public void Unsubscribe() { _state.LastFrameEffects.PropertyChanged -= _handler; }
	}
}
