using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using GS.Configs.IO;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Main;

namespace GS.Game.Benchmarks {
	[MemoryDiagnoser]
	public class ScalarVisualStateSetBenchmarks {
		SelectedCountryState _selectedCountry = null!;
		SelectedOrganizationState _selectedOrganization = null!;
		SelectedProvinceState _selectedProvince = null!;
		PlayerOrganizationState _playerOrganization = null!;
		TimeState _time = null!;
		LocaleState _locale = null!;
		MapLensState _mapLens = null!;

		string _countryId = null!;
		string _altCountryId = null!;
		string _orgId = null!;
		string _altOrgId = null!;
		string _provinceId = null!;
		string _altProvinceId = null!;
		DateTime _time0;
		DateTime _time1;

		bool _countryToggle;
		bool _orgToggle;
		bool _provinceToggle;
		bool _playerOrgToggle;
		bool _timeToggle;
		bool _localeToggle;
		bool _mapLensToggle;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			var visualState = fixture.Logic.VisualState;

			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(GameWorldFixture.ConfigDir, "organizations.json")).Load();

			_countryId = fixture.FirstCountryId;
			_altCountryId = fixture.Logic.ProvinceConfig.Provinces.Count > 1 ?
				fixture.Logic.ProvinceConfig.Provinces[1].CountryId : fixture.FirstCountryId;
			_orgId = orgConfig.Organizations[0].OrganizationId;
			_altOrgId = orgConfig.Organizations.Count > 1 ? orgConfig.Organizations[1].OrganizationId : _orgId;
			_provinceId = fixture.FirstProvinceId;
			_altProvinceId = fixture.Logic.ProvinceConfig.Provinces.Count > 1 ?
				fixture.Logic.ProvinceConfig.Provinces[1].ProvinceId : fixture.FirstProvinceId;
			_time0 = new DateTime(2000, 1, 1);
			_time1 = new DateTime(2000, 1, 2);

			_selectedCountry = visualState.SelectedCountry;
			_selectedCountry.Set(true, _countryId);

			_selectedOrganization = visualState.SelectedOrganization;
			_selectedOrganization.Set(true, _orgId, "Display", 100d);

			_selectedProvince = visualState.SelectedProvince;
			_selectedProvince.Set(true, _provinceId);

			_playerOrganization = visualState.PlayerOrganization;
			_playerOrganization.Set(true, _orgId, "Display", _countryId);

			_time = visualState.Time;
			_time.Set(_time0, false, 0);

			_locale = visualState.Locale;
			_locale.Set("en");

			_mapLens = visualState.MapLens;
			_mapLens.Set(MapLens.Political);
		}

		[Benchmark]
		public void SelectedCountryState_NoOp() => _selectedCountry.Set(true, _countryId);

		[Benchmark]
		public void SelectedCountryState_Update() {
			_countryToggle = !_countryToggle;
			_selectedCountry.Set(true, _countryToggle ? _altCountryId : _countryId);
		}

		[Benchmark]
		public void SelectedOrganizationState_NoOp() => _selectedOrganization.Set(true, _orgId, "Display", 100d);

		[Benchmark]
		public void SelectedOrganizationState_Update() {
			_orgToggle = !_orgToggle;
			_selectedOrganization.Set(true, _orgToggle ? _altOrgId : _orgId, "Display", 100d);
		}

		[Benchmark]
		public void SelectedProvinceState_NoOp() => _selectedProvince.Set(true, _provinceId);

		[Benchmark]
		public void SelectedProvinceState_Update() {
			_provinceToggle = !_provinceToggle;
			_selectedProvince.Set(true, _provinceToggle ? _altProvinceId : _provinceId);
		}

		[Benchmark]
		public void PlayerOrganizationState_NoOp() => _playerOrganization.Set(true, _orgId, "Display", _countryId);

		[Benchmark]
		public void PlayerOrganizationState_Update() {
			_playerOrgToggle = !_playerOrgToggle;
			_playerOrganization.Set(true, _orgId, "Display", _playerOrgToggle ? _altCountryId : _countryId);
		}

		[Benchmark]
		public void TimeState_NoOp() => _time.Set(_time0, false, 0);

		[Benchmark]
		public void TimeState_Update() {
			_timeToggle = !_timeToggle;
			_time.Set(_timeToggle ? _time1 : _time0, false, 0);
		}

		[Benchmark]
		public void LocaleState_NoOp() => _locale.Set("en");

		[Benchmark]
		public void LocaleState_Update() {
			_localeToggle = !_localeToggle;
			_locale.Set(_localeToggle ? "ru" : "en");
		}

		[Benchmark]
		public void MapLensState_NoOp() => _mapLens.Set(MapLens.Political);

		[Benchmark]
		public void MapLensState_Update() {
			_mapLensToggle = !_mapLensToggle;
			_mapLens.Set(_mapLensToggle ? MapLens.Geographic : MapLens.Political);
		}
	}
}
