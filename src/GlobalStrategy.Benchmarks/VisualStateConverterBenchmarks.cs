using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;

namespace GlobalStrategy.Benchmarks;

[MemoryDiagnoser]
public class VisualStateConverterBenchmarks {
	VisualStateConverter _converter = null!;
	World _world = null!;
	int _gameTimeEntity;
	int _localeEntity;
	int _orgEntity;

	[GlobalSetup]
	public void Setup() {
		var logic = BenchmarkGameFactory.CreateGameLogic();
		logic.Update(0f);
		logic.Commands.Push(new SelectCountryCommand(BenchmarkGameFactory.HqCountryId));
		logic.Update(0f);

		_world = logic.World;
		_gameTimeEntity = FindEntityWith<GameTime>(_world);
		_localeEntity = FindEntityWith<Locale>(_world);
		_orgEntity = FindEntityWith<Organization>(_world);
		_converter = new VisualStateConverter(
			logic.VisualState,
			logic.ActionConfig,
			new Dictionary<string, string> {
				[BenchmarkGameFactory.PlayerOrgId] = BenchmarkGameFactory.HqCountryId
			},
			countryConfig: logic.CountryConfig);
		_converter.Update(0f, _world, _gameTimeEntity, _localeEntity, _orgEntity);
	}

	[Benchmark]
	public void Update() {
		_converter.Update(0f, _world, _gameTimeEntity, _localeEntity, _orgEntity);
	}

	static int FindEntityWith<T>(World world) {
		int[] required = { TypeId<T>.Value };
		foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
			if (archetype.Count > 0) {
				return archetype.Entities[0];
			}
		}

		throw new InvalidOperationException($"No entity has component {typeof(T).Name}.");
	}
}
