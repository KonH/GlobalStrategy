using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using GS.Game.Commands;
using GS.Main;

namespace GS.Game.Benchmarks {
	[MemoryDiagnoser]
	public class ListVisualStateSetBenchmarks {
		CountryControlState _control = null!;
		CountryCharactersState _countryCharacters = null!;
		OrgCharactersState _orgCharacters = null!;
		OrgMapState _orgMap = null!;
		OrgActionsState _orgActions = null!;
		CountryActionsState _countryActions = null!;
		LeaderboardState _leaderboard = null!;
		GameLogState _gameLog = null!;
		CountryResourcesState _countryResources = null!;
		VisualEffectCollection _effects = null!;

		List<OrgControlEntry> _controlBaseline = null!;
		List<OrgControlEntry> _controlAlt = null!;
		List<CharacterStateEntry> _countryCharactersBaseline = null!;
		List<CharacterStateEntry> _countryCharactersAlt = null!;
		List<OrgCharacterSlotEntry> _orgCharactersBaseline = null!;
		List<OrgCharacterSlotEntry> _orgCharactersAlt = null!;
		List<OrgCountryEntry> _orgMapBaseline = null!;
		List<OrgCountryEntry> _orgMapAlt = null!;
		List<ActionCardEntry> _orgHandBaseline = null!;
		List<ActionCardEntry> _orgHandAlt = null!;
		List<ActionCardEntry> _orgDeckBaseline = null!;
		List<ActionCardEntry> _countryHandBaseline = null!;
		List<ActionCardEntry> _countryHandAlt = null!;
		List<ActionCardEntry> _countryDeckBaseline = null!;
		List<LeaderboardEntryState> _leaderboardOrgsBaseline = null!;
		List<LeaderboardEntryState> _leaderboardOrgsAlt = null!;
		List<LeaderboardEntryState> _leaderboardCountriesBaseline = null!;
		List<GameLogEntry> _gameLogBaseline = null!;
		List<GameLogEntry> _gameLogAlt = null!;
		List<ResourceStateEntry> _resourcesBaseline = null!;
		List<ResourceStateEntry> _resourcesAlt = null!;
		IReadOnlyList<ControlIncomeEntry> _controlIncomesBaseline = null!;
		List<VisualResourceChangeEffect> _effectsBaseline = null!;
		List<VisualResourceChangeEffect> _effectsAlt = null!;

		int _orgActionsHandSize;
		int _countryActionsHandSize;
		DateTime _countryActionsTime;
		string _countryId = null!;

		bool _controlToggle;
		bool _countryCharactersToggle;
		bool _orgCharactersToggle;
		bool _orgMapToggle;
		bool _orgActionsToggle;
		bool _countryActionsToggle;
		bool _leaderboardToggle;
		bool _gameLogToggle;
		bool _resourcesToggle;
		bool _effectsToggle;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			var logic = fixture.Logic;
			logic.Commands.Push(new SelectCountryCommand(fixture.FirstCountryId));
			logic.Update(0f);

			var visualState = logic.VisualState;
			_countryId = fixture.FirstCountryId;

			_control = visualState.SelectedCountry.Control;
			_controlBaseline = new List<OrgControlEntry>(_control.OrgEntries);
			_controlAlt = new List<OrgControlEntry>(_controlBaseline) {
				new OrgControlEntry("bench_org", "Bench Org", 1, 1, 0, 0d)
			};

			_countryCharacters = visualState.SelectedCountry.Characters;
			_countryCharactersBaseline = new List<CharacterStateEntry>(_countryCharacters.Characters);
			_countryCharactersAlt = new List<CharacterStateEntry>(_countryCharactersBaseline) {
				new CharacterStateEntry("bench_char", "bench_role", new[] { "bench" }, Array.Empty<SkillEntry>(), new AnimatableInt())
			};

			_orgCharacters = visualState.PlayerOrganization.Characters;
			_orgCharactersBaseline = new List<OrgCharacterSlotEntry>(_orgCharacters.Slots);
			_orgCharactersAlt = new List<OrgCharacterSlotEntry>(_orgCharactersBaseline) {
				new OrgCharacterSlotEntry("bench_role", 99, null, true)
			};

			_orgMap = visualState.OrgMap;
			_orgMapBaseline = new List<OrgCountryEntry>(_orgMap.Entries);
			_orgMapAlt = new List<OrgCountryEntry>(_orgMapBaseline) {
				new OrgCountryEntry("bench_country", "bench_org", 0f)
			};

			_orgActions = visualState.PlayerOrganization.Actions;
			_orgActionsHandSize = _orgActions.HandSize;
			_orgHandBaseline = new List<ActionCardEntry>(_orgActions.Hand);
			_orgHandAlt = new List<ActionCardEntry>(_orgHandBaseline) {
				new ActionCardEntry("bench_action", 99, true)
			};
			_orgDeckBaseline = new List<ActionCardEntry>(_orgActions.Deck);

			_countryActions = visualState.SelectedCountry.CountryActions;
			_countryActionsHandSize = _countryActions.HandSize;
			_countryActionsTime = _countryActions.CurrentTime;
			_countryHandBaseline = new List<ActionCardEntry>(_countryActions.Hand);
			_countryHandAlt = new List<ActionCardEntry>(_countryHandBaseline) {
				new ActionCardEntry("bench_action", 99, true)
			};
			_countryDeckBaseline = new List<ActionCardEntry>(_countryActions.Deck);

			_leaderboard = visualState.Leaderboard;
			_leaderboardOrgsBaseline = new List<LeaderboardEntryState>(_leaderboard.Organizations);
			_leaderboardOrgsAlt = new List<LeaderboardEntryState>(_leaderboardOrgsBaseline) {
				new LeaderboardEntryState(99, "bench_org", "Bench Org", 0d)
			};
			_leaderboardCountriesBaseline = new List<LeaderboardEntryState>(_leaderboard.Countries);

			_gameLog = visualState.GameLog;
			_gameLogBaseline = new List<GameLogEntry>(_gameLog.Entries);
			_gameLogAlt = new List<GameLogEntry>(_gameLogBaseline) {
				new GameLogEntry(0, GameLogEntryKind.Control, "bench_org", "bench_country", "", "", Array.Empty<string>(), 0d, 0d, false)
			};

			_countryResources = visualState.SelectedCountry.Resources;
			_resourcesBaseline = new List<ResourceStateEntry>(_countryResources.Resources);
			_resourcesAlt = new List<ResourceStateEntry>(_resourcesBaseline) {
				new ResourceStateEntry("bench_resource", new AnimatableDouble(), Array.Empty<EffectStateEntry>())
			};
			_controlIncomesBaseline = _countryResources.ControlIncomes;

			_effects = visualState.LastFrameEffects;
			_effectsBaseline = new List<VisualResourceChangeEffect>(_effects.Effects);
			_effectsAlt = new List<VisualResourceChangeEffect>(_effectsBaseline) {
				new VisualResourceChangeEffect("bench_effect", "bench_resource", "bench_owner", 0d)
			};

			_control.Set(_control.UsedControl.Actual, _controlBaseline);
			_countryCharacters.Set(_countryCharactersBaseline);
			_orgCharacters.Set(_orgCharactersBaseline);
			_orgMap.Set(_orgMapBaseline);
			_orgActions.Set(_orgHandBaseline, _orgDeckBaseline, _orgActionsHandSize);
			_countryActions.Set(_countryHandBaseline, _countryDeckBaseline, _countryActionsHandSize, _countryActionsTime);
			_leaderboard.Set(_leaderboardOrgsBaseline, _leaderboardCountriesBaseline);
			_gameLog.Set(_gameLogBaseline);
			_countryResources.Set(true, _countryId, _resourcesBaseline, _controlIncomesBaseline);
			_effects.Set(_effectsBaseline);
		}

		[Benchmark]
		public void CountryControlState_NoOp() =>
			_control.Set(_control.UsedControl.Actual, new List<OrgControlEntry>(_controlBaseline));

		[Benchmark]
		public void CountryControlState_Update() {
			_controlToggle = !_controlToggle;
			_control.Set(_control.UsedControl.Actual, _controlToggle ? _controlAlt : _controlBaseline);
		}

		[Benchmark]
		public void CountryCharactersState_NoOp() =>
			_countryCharacters.Set(new List<CharacterStateEntry>(_countryCharactersBaseline));

		[Benchmark]
		public void CountryCharactersState_Update() {
			_countryCharactersToggle = !_countryCharactersToggle;
			_countryCharacters.Set(_countryCharactersToggle ? _countryCharactersAlt : _countryCharactersBaseline);
		}

		[Benchmark]
		public void OrgCharactersState_NoOp() =>
			_orgCharacters.Set(new List<OrgCharacterSlotEntry>(_orgCharactersBaseline));

		[Benchmark]
		public void OrgCharactersState_Update() {
			_orgCharactersToggle = !_orgCharactersToggle;
			_orgCharacters.Set(_orgCharactersToggle ? _orgCharactersAlt : _orgCharactersBaseline);
		}

		[Benchmark]
		public void OrgMapState_NoOp() =>
			_orgMap.Set(new List<OrgCountryEntry>(_orgMapBaseline));

		[Benchmark]
		public void OrgMapState_Update() {
			_orgMapToggle = !_orgMapToggle;
			_orgMap.Set(_orgMapToggle ? _orgMapAlt : _orgMapBaseline);
		}

		[Benchmark]
		public void OrgActionsState_NoOp() =>
			_orgActions.Set(new List<ActionCardEntry>(_orgHandBaseline), new List<ActionCardEntry>(_orgDeckBaseline), _orgActionsHandSize);

		[Benchmark]
		public void OrgActionsState_Update() {
			_orgActionsToggle = !_orgActionsToggle;
			_orgActions.Set(_orgActionsToggle ? _orgHandAlt : _orgHandBaseline, _orgDeckBaseline, _orgActionsHandSize);
		}

		[Benchmark]
		public void CountryActionsState_NoOp() =>
			_countryActions.Set(new List<ActionCardEntry>(_countryHandBaseline), new List<ActionCardEntry>(_countryDeckBaseline), _countryActionsHandSize, _countryActionsTime);

		[Benchmark]
		public void CountryActionsState_Update() {
			_countryActionsToggle = !_countryActionsToggle;
			_countryActions.Set(_countryActionsToggle ? _countryHandAlt : _countryHandBaseline, _countryDeckBaseline, _countryActionsHandSize, _countryActionsTime);
		}

		[Benchmark]
		public void LeaderboardState_NoOp() =>
			_leaderboard.Set(new List<LeaderboardEntryState>(_leaderboardOrgsBaseline), new List<LeaderboardEntryState>(_leaderboardCountriesBaseline));

		[Benchmark]
		public void LeaderboardState_Update() {
			_leaderboardToggle = !_leaderboardToggle;
			_leaderboard.Set(_leaderboardToggle ? _leaderboardOrgsAlt : _leaderboardOrgsBaseline, _leaderboardCountriesBaseline);
		}

		[Benchmark]
		public void GameLogState_NoOp() =>
			_gameLog.Set(new List<GameLogEntry>(_gameLogBaseline));

		[Benchmark]
		public void GameLogState_Update() {
			_gameLogToggle = !_gameLogToggle;
			_gameLog.Set(_gameLogToggle ? _gameLogAlt : _gameLogBaseline);
		}

		[Benchmark]
		public void CountryResourcesState_NoOp() =>
			_countryResources.Set(true, _countryId, new List<ResourceStateEntry>(_resourcesBaseline), _controlIncomesBaseline);

		[Benchmark]
		public void CountryResourcesState_Update() {
			_resourcesToggle = !_resourcesToggle;
			_countryResources.Set(true, _countryId, _resourcesToggle ? _resourcesAlt : _resourcesBaseline, _controlIncomesBaseline);
		}

		[Benchmark]
		public void VisualEffectCollection_NoOp() =>
			_effects.Set(new List<VisualResourceChangeEffect>(_effectsBaseline));

		[Benchmark]
		public void VisualEffectCollection_Update() {
			_effectsToggle = !_effectsToggle;
			_effects.Set(_effectsToggle ? _effectsAlt : _effectsBaseline);
		}
	}
}
