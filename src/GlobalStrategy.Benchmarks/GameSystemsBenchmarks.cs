using System;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;

namespace GlobalStrategy.Benchmarks;

[MemoryDiagnoser]
public class GameSystemsBenchmarks {
	[Params(100, 1_000)]
	public int CountryCount { get; set; }

	World _world = null!;
	DateTime _previousMonth;
	DateTime _currentMonth;

	[GlobalSetup]
	public void Setup() {
		_previousMonth = new DateTime(1880, 1, 31);
		_currentMonth = new DateTime(1880, 2, 1);
		_world = new World();

		for (int i = 0; i < CountryCount; i++) {
			string countryId = $"country_{i}";
			string firstOrgId = $"org_{i}_a";
			string secondOrgId = $"org_{i}_b";

			CreateResource(_world, countryId, OwnerType.Country, "gold", 1_000);
			CreateResource(_world, firstOrgId, OwnerType.Org, "gold", 100);
			CreateResource(_world, secondOrgId, OwnerType.Org, "gold", 100);
			CreateResourceEffect(_world, countryId, OwnerType.Country, "gold", 25);
			CreateControlEffect(_world, firstOrgId, countryId, 30);
			CreateControlEffect(_world, secondOrgId, countryId, 20);
		}
	}

	[Benchmark]
	public void ApplyMonthlyResourceEffects() {
		ResourceSystem.Update(_world, _previousMonth, _currentMonth);
	}

	[Benchmark]
	public void ApplyMonthlyControlIncome() {
		ControlSystem.Update(_world, _previousMonth, _currentMonth);
	}

	static void CreateResource(World world, string ownerId, OwnerType ownerType, string resourceId, double value) {
		int entity = world.Create();
		world.Add(entity, new ResourceOwner(ownerId, ownerType));
		world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
	}

	static void CreateResourceEffect(World world, string ownerId, OwnerType ownerType, string resourceId, double value) {
		int entity = world.Create();
		world.Add(entity, new ResourceOwner(ownerId, ownerType));
		world.Add(entity, new ResourceLink(resourceId));
		world.Add(entity, new ResourceEffect {
			EffectId = $"monthly_{ownerId}_{resourceId}",
			Value = value,
			PayType = PayType.Monthly,
			ClampToZero = false
		});
	}

	static void CreateControlEffect(World world, string orgId, string countryId, int value) {
		int entity = world.Create();
		world.Add(entity, new ControlEffect {
			OrgId = orgId,
			CountryId = countryId,
			Value = value,
			EffectId = $"control_{orgId}_{countryId}"
		});
	}
}
