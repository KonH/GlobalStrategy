using System;
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
		readonly ScrollView _list;
		readonly Label _empty;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly OrgVisualConfig _orgVisualConfig;
		Tab _selectedTab = Tab.Organizations;
		LeaderboardState _lastState;

		public LeaderboardWindowView(VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) {
			_root = root;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_orgVisualConfig = orgVisualConfig;
			_tabOrganizations = root.Q<Button>("tab-organizations");
			_tabCountries = root.Q<Button>("tab-countries");
			_list = root.Q<ScrollView>("leaderboard-list");
			_empty = root.Q<Label>("leaderboard-empty");

			_tabOrganizations?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _tabOrganizations.ContainsPoint(e.localPosition)) {
					SetTab(Tab.Organizations, true);
				}
			});
			_tabCountries?.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _tabCountries.ContainsPoint(e.localPosition)) {
					SetTab(Tab.Countries, true);
				}
			});
		}

		public void ResetToDefaultTab() {
			SetTab(Tab.Organizations, true);
		}

		public void Refresh(LeaderboardState state) {
			if (_list == null || state == null) {
				return;
			}

			_lastState = state;
			Vector2 scrollOffset = _list.scrollOffset;
			_list.Clear();
			IReadOnlyList<LeaderboardEntryState> entries = _selectedTab == Tab.Organizations ? state.Organizations : state.Countries;
			foreach (var entry in entries) {
				_list.Add(CreateRow(entry));
			}
			if (_empty != null) {
				_empty.style.display = entries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
			UpdateTabClasses();
			_list.schedule.Execute(() => _list.scrollOffset = scrollOffset);
		}

		void SetTab(Tab tab, bool resetScroll) {
			_selectedTab = tab;
			UpdateTabClasses();
			if (resetScroll && _list != null) {
				_list.scrollOffset = Vector2.zero;
			}
			if (_lastState != null) {
				Refresh(_lastState);
			}
		}

		void UpdateTabClasses() {
			_tabOrganizations?.EnableInClassList("leaderboard-tab--active", _selectedTab == Tab.Organizations);
			_tabCountries?.EnableInClassList("leaderboard-tab--active", _selectedTab == Tab.Countries);
		}

		VisualElement CreateRow(LeaderboardEntryState entry) {
			var row = new VisualElement();
			row.AddToClassList("leaderboard-row");

			var place = new Label(entry.Place.ToString(CultureInfo.InvariantCulture));
			place.AddToClassList("leaderboard-row-place");
			row.Add(place);

			var flag = new VisualElement();
			flag.AddToClassList("leaderboard-row-flag");
			Sprite sprite = _selectedTab == Tab.Organizations
				? _orgVisualConfig?.Find(entry.EntityId)?.flag
				: _countryVisualConfig?.Find(entry.EntityId)?.flag;
			if (sprite != null) {
				flag.style.backgroundImage = new StyleBackground(sprite);
				flag.style.display = DisplayStyle.Flex;
			} else {
				flag.style.display = DisplayStyle.None;
			}
			row.Add(flag);

			var name = new Label(GetDisplayName(entry));
			name.AddToClassList("leaderboard-row-name");
			row.Add(name);

			var score = new Label(FormatScore(entry.Score));
			score.AddToClassList("leaderboard-row-score");
			row.Add(score);

			return row;
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

		static readonly NumberFormatInfo s_scoreFormat = new NumberFormatInfo {
			NumberGroupSeparator = " ",
			NumberGroupSizes = new[] { 3 }
		};

		static string FormatScore(double value) {
			return Math.Round(value, MidpointRounding.AwayFromZero).ToString("#,0", s_scoreFormat);
		}
	}
}
