using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Unity.Map;

namespace GS.Unity.UI {
	public class LeaderboardWindowView {
		enum Tab {
			Organizations,
			Countries
		}

		readonly VisualElement _root;
		readonly Button _tabOrganizations;
		readonly Button _tabCountries;
		readonly ListView _list;
		readonly Label _empty;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		Tab _selectedTab = Tab.Organizations;
		LeaderboardState _lastState;
		IReadOnlyList<LeaderboardEntryState> _currentEntries = Array.Empty<LeaderboardEntryState>();

		public LeaderboardWindowView(VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_tabOrganizations = root.Q<Button>("tab-organizations");
			_tabCountries = root.Q<Button>("tab-countries");
			_list = root.Q<ListView>("leaderboard-list");
			_empty = root.Q<Label>("leaderboard-empty");

			if (_list != null) {
				_list.makeItem = () => {
					RankRowBuilder.Elements elements = RankRowBuilder.Build();
					elements.Row.userData = elements;
					return elements.Row;
				};
				_list.bindItem = (element, index) => BindRow(element, _currentEntries[index]);
			}

			if (_tabOrganizations != null) {
				_tabOrganizations.OnClick(() => SetTab(Tab.Organizations, true));
			}
			if (_tabCountries != null) {
				_tabCountries.OnClick(() => SetTab(Tab.Countries, true));
			}
		}

		public void ResetToDefaultTab() {
			SetTab(Tab.Organizations, true);
		}

		public void Refresh(LeaderboardState state) {
			if (_list == null || state == null) {
				return;
			}

			_lastState = state;
			_currentEntries = _selectedTab == Tab.Organizations ? state.Organizations : state.Countries;
			_list.itemsSource = (IList)_currentEntries;
			_list.Rebuild();
			if (_empty != null) {
				_empty.style.display = _currentEntries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
			UpdateTabClasses();
		}

		void SetTab(Tab tab, bool resetScroll) {
			_selectedTab = tab;
			UpdateTabClasses();
			if (_lastState != null) {
				Refresh(_lastState);
			}
			if (resetScroll && _list != null && _currentEntries.Count > 0) {
				_list.ScrollToItem(0);
			}
		}

		void UpdateTabClasses() {
			_tabOrganizations?.EnableInClassList("leaderboard-tab--active", _selectedTab == Tab.Organizations);
			_tabCountries?.EnableInClassList("leaderboard-tab--active", _selectedTab == Tab.Countries);
		}

		void BindRow(VisualElement element, LeaderboardEntryState entry) {
			Sprite sprite = _selectedTab == Tab.Organizations
				? _orgVisualConfig?.Find(entry.EntityId)?.flag
				: _countryVisualConfig?.Find(entry.EntityId)?.flag;
			var elements = (RankRowBuilder.Elements)element.userData;
			RankRowBuilder.Bind(elements, entry.Place, sprite, GetDisplayName(entry), ScoreFormat.Format(entry.Score));
		}

		string GetDisplayName(LeaderboardEntryState entry) {
			if (_selectedTab == Tab.Countries) {
				string key = $"country_name.{entry.EntityId}";
				string localized = _loc?.Get(key) ?? "";
				if (!string.IsNullOrEmpty(localized) && localized != key) {
					return localized;
				}
			}
			return entry.DisplayName;
		}
	}
}
