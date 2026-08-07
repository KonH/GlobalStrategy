using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	/// <summary>
	/// Cached resource lookup/mutation service. Prefer Set/Update/Remove over direct
	/// <see cref="Resource"/> component access so the cache stays coherent.
	/// </summary>
	public sealed class ResourceQuery {
		readonly Dictionary<(string OwnerId, string ResourceId), double> _values =
			new Dictionary<(string OwnerId, string ResourceId), double>();
		readonly Dictionary<(string OwnerId, string ResourceId), int> _entities =
			new Dictionary<(string OwnerId, string ResourceId), int>();

		public Action<string>? OnCacheMissWarning { get; set; }

		public double GetValue(IReadOnlyWorld world, string ownerId, string resourceId) {
			return TryGetValue(world, ownerId, resourceId, out double value) ? value : 0;
		}

		public bool TryGetValue(IReadOnlyWorld world, string ownerId, string resourceId, out double value) {
			var key = (ownerId, resourceId);
			if (_values.TryGetValue(key, out value)) {
				return true;
			}
			if (!TryReadWorld(world, ownerId, resourceId, out value, out int entity)) {
				value = 0;
				return false;
			}
			WarnCacheMiss(ownerId, resourceId);
			PutCache(key, value, entity);
			return true;
		}

		public int FindEntity(IReadOnlyWorld world, string ownerId, string resourceId) {
			var key = (ownerId, resourceId);
			if (_entities.TryGetValue(key, out int cached) && world.IsAlive(cached) && world.Has<Resource>(cached)) {
				return cached;
			}
			if (!TryReadWorld(world, ownerId, resourceId, out double value, out int entity)) {
				return -1;
			}
			WarnCacheMiss(ownerId, resourceId);
			PutCache(key, value, entity);
			return entity;
		}

		/// <summary>Create or overwrite a resource value and refresh the cache.</summary>
		public void Set(
			World world,
			string ownerId,
			string resourceId,
			double value,
			OwnerType ownerType = OwnerType.Org) {
			var key = (ownerId, resourceId);
			int entity = FindEntityNoMissWarn(world, ownerId, resourceId);
			if (entity < 0) {
				entity = world.Create();
				world.Add(entity, new ResourceOwner(ownerId, ownerType));
				world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
			} else {
				ref Resource resource = ref world.Get<Resource>(entity);
				resource.Value = value;
			}
			PutCache(key, value, entity);
		}

		/// <summary>Overwrite an existing resource. Returns false if missing.</summary>
		public bool TryUpdate(World world, string ownerId, string resourceId, double value, out double oldValue) {
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				oldValue = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			oldValue = resource.Value;
			resource.Value = value;
			PutCache((ownerId, resourceId), value, entity);
			return true;
		}

		public bool TrySetValue(
			World world, string ownerId, string resourceId, double value, out double oldValue) {
			return TryUpdate(world, ownerId, resourceId, value, out oldValue);
		}

		public bool TryApplyClampedDelta(
			World world, string ownerId, string resourceId, double delta,
			double minimum, double maximum, out double appliedDelta) {
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				appliedDelta = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			double oldValue = resource.Value;
			resource.Value = Math.Clamp(oldValue + delta, minimum, maximum);
			appliedDelta = resource.Value - oldValue;
			PutCache((ownerId, resourceId), resource.Value, entity);
			return true;
		}

		public bool TryApplyClampedDelta(
			World world, string ownerId, string resourceId, double delta,
			ResourceDefinition? definition, string effectId, DateTime timestamp,
			double defaultMinimum, double defaultMaximum, out double appliedDelta) {
			double minimum = definition?.MinValue ?? defaultMinimum;
			double maximum = definition?.MaxValue ?? defaultMaximum;
			int entity = FindEntity(world, ownerId, resourceId);
			if (entity < 0) {
				appliedDelta = 0;
				return false;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			double oldValue = resource.Value;
			resource.Value = Math.Clamp(oldValue + delta, minimum, maximum);
			appliedDelta = resource.Value - oldValue;
			PutCache((ownerId, resourceId), resource.Value, entity);
			if (appliedDelta != 0) {
				TryAppendHistory(world, entity, definition, effectId, appliedDelta, timestamp);
			}
			return true;
		}

		public void ApplyDelta(World world, string ownerId, string resourceId, double delta, OwnerType ownerType) {
			if (delta == 0) {
				return;
			}
			int entity = FindEntityNoMissWarn(world, ownerId, resourceId);
			if (entity < 0) {
				Set(world, ownerId, resourceId, delta, ownerType);
				return;
			}
			ref Resource resource = ref world.Get<Resource>(entity);
			resource.Value += delta;
			PutCache((ownerId, resourceId), resource.Value, entity);
		}

		/// <summary>Destroy a resource entity (e.g. transient war progress) and drop cache entries.</summary>
		public bool Remove(World world, string ownerId, string resourceId) {
			int entity = FindEntityNoMissWarn(world, ownerId, resourceId);
			var key = (ownerId, resourceId);
			_values.Remove(key);
			_entities.Remove(key);
			if (entity < 0) {
				return false;
			}
			world.Destroy(entity);
			return true;
		}

		/// <summary>Update cache after an in-place mutation that bypassed Set/Update (e.g. bulk apply).</summary>
		public void NotifyValue(string ownerId, string resourceId, double value, int entity = -1) {
			var key = (ownerId, resourceId);
			_values[key] = value;
			if (entity >= 0) {
				_entities[key] = entity;
			}
		}

		public void NotifyRemoved(string ownerId, string resourceId) {
			var key = (ownerId, resourceId);
			_values.Remove(key);
			_entities.Remove(key);
		}

		/// <summary>Rebuild the entire cache from world state (init/load).</summary>
		public void Rebuild(IReadOnlyWorld world) {
			_values.Clear();
			_entities.Clear();
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					PutCache((owners[i].OwnerId, resources[i].ResourceId), resources[i].Value, arch.Entities[i]);
				}
			}
		}

		public static void TryAppendHistory(
			World world, int resourceEntity, ResourceDefinition? definition,
			string effectId, double appliedDelta, DateTime timestamp) {
			if (appliedDelta == 0 || definition?.RecordHistory != true) {
				return;
			}
			if (!world.Has<ResourceHistory>(resourceEntity)) {
				world.Add(resourceEntity, new ResourceHistory {
					History = new List<ResourceChangeEntry>()
				});
			}
			ref ResourceHistory history = ref world.Get<ResourceHistory>(resourceEntity);
			if (history.History == null) {
				history.History = new List<ResourceChangeEntry>();
			}
			history.History.Add(new ResourceChangeEntry {
				EffectId = effectId,
				AppliedDelta = appliedDelta,
				Timestamp = timestamp
			});
		}

		int FindEntityNoMissWarn(IReadOnlyWorld world, string ownerId, string resourceId) {
			var key = (ownerId, resourceId);
			if (_entities.TryGetValue(key, out int cached) && world.IsAlive(cached) && world.Has<Resource>(cached)) {
				return cached;
			}
			if (!TryReadWorld(world, ownerId, resourceId, out double value, out int entity)) {
				return -1;
			}
			PutCache(key, value, entity);
			return entity;
		}

		void PutCache((string OwnerId, string ResourceId) key, double value, int entity) {
			_values[key] = value;
			_entities[key] = entity;
		}

		void WarnCacheMiss(string ownerId, string resourceId) {
			OnCacheMissWarning?.Invoke(
				$"ResourceQuery cache miss for '{ownerId}'/'{resourceId}'; falling back to world scan.");
		}

		static bool TryReadWorld(
			IReadOnlyWorld world, string ownerId, string resourceId, out double value, out int entity) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						value = resources[i].Value;
						entity = arch.Entities[i];
						return true;
					}
				}
			}
			value = 0;
			entity = -1;
			return false;
		}
	}
}
