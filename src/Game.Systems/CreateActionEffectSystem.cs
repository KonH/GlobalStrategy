using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CreateActionEffectSystem {
		public static void Update(World world, ActionConfig actionConfig, EffectConfig effectConfig, DateTime currentTime) {
			int[] required = { TypeId<GameAction>.Value, TypeId<ActionSucceeded>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
			var toProcess = new List<(int entity, string actionId, string orgId)>();

			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					toProcess.Add((arch.Entities[i], actions[i].ActionId, orgs[i].OrgId));
				}
			}

			int[] countryRequired = { TypeId<CountryContext>.Value };
			var entityCountry = new Dictionary<int, string>();
			foreach (var arch in world.GetMatchingArchetypes(countryRequired, null)) {
				CountryContext[] ctxs = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					entityCountry[arch.Entities[i]] = ctxs[i].CountryId;
				}
			}

			foreach (var (entity, actionId, orgId) in toProcess) {
				entityCountry.TryGetValue(entity, out string countryId);
				var def = actionConfig.Find(actionId);
				if (def == null) { continue; }

				foreach (var effectId in def.EffectIds) {
					var effectDef = effectConfig.Find(effectId);
					if (effectDef is DiscoverCountryEffectParams) {
						int e = world.Create();
						world.Add(e, new DiscoverCountryEffect { EffectId = effectId, OrgId = orgId });
					} else if (effectDef is ControlChangeEffectParams controlParams && controlParams.Amount > 0 && !string.IsNullOrEmpty(countryId)) {
						int usedTotal = ControlQuery.GetTotalControlInCountry(world, countryId);
						if (usedTotal < 100) {
							int toAdd = Math.Min(controlParams.Amount, 100 - usedTotal);
							int ie = world.Create();
							world.Add(ie, new ControlEffect {
								OrgId = orgId,
								CountryId = countryId,
								Value = toAdd,
								EffectId = $"country_action_{orgId}_{actionId}_{currentTime.Ticks}"
							});
							int rc = world.Create();
							world.Add(rc, new ResourceChange {
								EffectId = $"control_{orgId}_{countryId}_{currentTime.Ticks}",
								ResourceId = $"control_{countryId}",
								OwnerId = orgId,
								Amount = toAdd
							});
							// Game Log event — computed after ControlEffect above so the sum already
							// includes this action's own contribution. See
							// Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
							int orgTotal = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
							int ge = world.Create();
							world.Add(ge, new ControlEffectApplied {
								OrgId = orgId,
								CountryId = countryId,
								Delta = toAdd,
								Total = orgTotal
							});
						}
					} else if (effectDef is OpinionModifierEffectParams opinionParams && !string.IsNullOrEmpty(countryId)) {
						string targetCharId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, def.TargetRole);
						if (string.IsNullOrEmpty(targetCharId)) { continue; }

						string opinionResourceId = $"opinion_{orgId}";
						int rc = world.Create();
						world.Add(rc, new ResourceChange {
							EffectId = $"opinion_{orgId}_{targetCharId}_{currentTime.Ticks}",
							ResourceId = opinionResourceId,
							OwnerId = targetCharId,
							Amount = opinionParams.InitialValue
						});
						double opinionTotal = EnsureOpinionResource(world, targetCharId, opinionResourceId, opinionParams.InitialValue);
						// Game Log event — see Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
						int opinionGe = world.Create();
						world.Add(opinionGe, new OpinionEffectApplied {
							OrgId = orgId,
							CharacterId = targetCharId,
							Delta = opinionParams.InitialValue,
							Total = opinionTotal
						});
						int decayEffectEntity = world.Create();
						world.Add(decayEffectEntity, new ResourceOwner(targetCharId));
						world.Add(decayEffectEntity, new ResourceLink(opinionResourceId));
						world.Add(decayEffectEntity, new ResourceEffect {
							EffectId = $"opinion_decay_{orgId}_{targetCharId}_{currentTime.Ticks}",
							Value = -(double)opinionParams.DecayPerMonth,
							PayType = PayType.Monthly,
							MaxTotal = opinionParams.InitialValue,
							ClampToZero = true
						});
					} else if (effectDef is SetCountryRelationEffectParams relationParams && !string.IsNullOrEmpty(countryId)) {
						int e = world.Create();
						world.Add(e, new SetCountryRelationEffect { EffectId = effectId, OrgId = orgId, CountryId = countryId, Kind = relationParams.Kind });
					} else if (effectDef is ClearCountryRelationEffectParams && !string.IsNullOrEmpty(countryId) && world.Has<RelationCardTarget>(entity)) {
						string targetCountryId = world.Get<RelationCardTarget>(entity).TargetCountryId;
						int e = world.Create();
						world.Add(e, new ClearCountryRelationEffect { EffectId = effectId, OrgId = orgId, CountryId = countryId, TargetCountryId = targetCountryId });
					} else if (effectDef is DeclareWarEffectParams && !string.IsNullOrEmpty(countryId) && world.Has<RelationCardTarget>(entity)) {
						string targetCountryId = world.Get<RelationCardTarget>(entity).TargetCountryId;
						if (Wars.DeclareWar(world, countryId, targetCountryId, currentTime)) {
							int e = world.Create();
							world.Add(e, new WarDeclaredApplied {
								OrgId = orgId,
								CountryId = countryId,
								DefenderCountryId = targetCountryId
							});
						}
					} else if (effectDef is EnemyControlDrainEffectParams drainParams && drainParams.Amount > 0 && !string.IsNullOrEmpty(countryId)) {
						string? targetOrgId = ControlQuery.GetHighestControlOtherOrg(world, orgId, countryId);
						if (targetOrgId != null) {
							int targetControlBefore = ControlQuery.GetOrgControlInCountry(world, targetOrgId, countryId);
							int actualDrain = Math.Min(drainParams.Amount, targetControlBefore);
							ControlQuery.ReduceOrgControlInCountry(world, targetOrgId, countryId, actualDrain);
							if (actualDrain > 0) {
								int rc = world.Create();
								world.Add(rc, new ResourceChange {
									EffectId = $"control_{targetOrgId}_{countryId}_{currentTime.Ticks}",
									ResourceId = $"control_{countryId}",
									OwnerId = targetOrgId,
									Amount = -actualDrain
								});
								int ge = world.Create();
								world.Add(ge, new ControlEffectApplied {
									OrgId = targetOrgId,
									CountryId = countryId,
									Delta = -actualDrain,
									Total = targetControlBefore - actualDrain
								});
							}
						}
					}
				}
			}
		}

		static double EnsureOpinionResource(World world, string charId, string resourceId, int initialValue) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == charId && resources[i].ResourceId == resourceId) {
						resources[i].Value += initialValue;
						return resources[i].Value;
					}
				}
			}
			int re = world.Create();
			world.Add(re, new ResourceOwner(charId, OwnerType.Character));
			world.Add(re, new Resource { ResourceId = resourceId, Value = initialValue });
			return initialValue;
		}
	}
}
