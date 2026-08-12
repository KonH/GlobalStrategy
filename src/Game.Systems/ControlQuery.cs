using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ControlQuery {
		public static int GetOrgControlInCountry(IReadOnlyWorld world, string orgId, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].CountryId == countryId && controls[i].OrgId == orgId) {
						total += controls[i].Value;
					}
				}
			}
			return total;
		}

		public static int GetTotalControlInCountry(IReadOnlyWorld world, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].CountryId == countryId) {
						total += controls[i].Value;
					}
				}
			}
			return total;
		}

		public static string? GetHighestControlOtherOrg(IReadOnlyWorld world, string orgId, string countryId) {
			var totals = GetOtherOrgControlTotals(world, orgId, countryId);
			string? result = null;
			int highestTotal = 0;
			foreach (var pair in totals) {
				if (pair.Value > highestTotal
					|| (pair.Value == highestTotal && pair.Value > 0 && (result == null || string.CompareOrdinal(pair.Key, result) < 0))) {
					result = pair.Key;
					highestTotal = pair.Value;
				}
			}
			return result;
		}

		public static void ReduceOrgControlInCountry(World world, string orgId, string countryId, int amount) {
			if (amount <= 0) {
				return;
			}

			var effects = new List<(int Entity, string EffectId, int Value)>();
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].OrgId == orgId && controls[i].CountryId == countryId) {
						effects.Add((arch.Entities[i], controls[i].EffectId, controls[i].Value));
					}
				}
			}

			effects.Sort((a, b) => {
				int effectIdComparison = string.CompareOrdinal(a.EffectId, b.EffectId);
				return effectIdComparison != 0 ? effectIdComparison : a.Entity.CompareTo(b.Entity);
			});

			int remaining = amount;
			foreach (var effect in effects) {
				if (remaining <= 0) {
					break;
				}
				int reduction = Math.Min(remaining, effect.Value);
				int newValue = effect.Value - reduction;
				remaining -= reduction;
				if (newValue <= 0) {
					world.Destroy(effect.Entity);
				} else {
					ref ControlEffect control = ref world.Get<ControlEffect>(effect.Entity);
					control.Value = newValue;
				}
			}
		}

		public static void DestroyAllControlInCountry(World world, string countryId) {
			var toDestroy = new List<int>();
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].CountryId == countryId) {
						toDestroy.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int entity in toDestroy) {
				world.Destroy(entity);
			}
		}

		static Dictionary<string, int> GetOtherOrgControlTotals(IReadOnlyWorld world, string orgId, string countryId) {
			var totals = new Dictionary<string, int>(StringComparer.Ordinal);
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (controls[i].CountryId != countryId || controls[i].OrgId == orgId) {
						continue;
					}
					totals.TryGetValue(controls[i].OrgId, out int total);
					totals[controls[i].OrgId] = total + controls[i].Value;
				}
			}
			return totals;
		}
	}
}
