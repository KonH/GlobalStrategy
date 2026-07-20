using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Configs.IO;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;

namespace GS.Game.Benchmarks {
	[MemoryDiagnoser]
	public class VisualStateConverterBenchmarks {
		VisualStateConverter _converter = null!;
		World _world = null!;
		int _gameTimeEntity;
		int _localeEntity;
		int _orgEntity;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			var logic = fixture.Logic;
			logic.Commands.Push(new SelectCountryCommand(fixture.FirstCountryId));
			logic.Update(0f);

			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(GameWorldFixture.ConfigDir, "organizations.json")).Load();
			var hqCountryByOrgId = new Dictionary<string, string>();
			foreach (var entry in orgConfig.Organizations) {
				hqCountryByOrgId[entry.OrganizationId] = entry.HqCountryId;
			}

			_world = logic.World;
			_gameTimeEntity = fixture.GameTimeEntity;
			_localeEntity = BenchmarkEntityLookup.FindEntityWith<Locale>(_world);
			_orgEntity = BenchmarkEntityLookup.FindEntityWith<Organization>(_world);
			_converter = new VisualStateConverter(
				logic.VisualState,
				logic.ActionConfig,
				hqCountryByOrgId,
				countryConfig: logic.CountryConfig);
			_converter.Update(0f, _world, _gameTimeEntity, _localeEntity, _orgEntity);
		}

		[Benchmark]
		public void Update() {
			_converter.Update(0f, _world, _gameTimeEntity, _localeEntity, _orgEntity);
		}
	}
}
