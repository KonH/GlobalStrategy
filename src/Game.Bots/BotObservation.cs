using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Bots {
	public sealed class BotObservation : IBotObservation {
		public string OrgId { get; }
		public DateTime CurrentDate { get; }
		public double Gold { get; }
		public int OrgHandSize { get; }
		public int TotalControl { get; }
		public IReadOnlyList<BotCardView> OrgHand { get; }
		public IReadOnlyList<string> DiscoveredCountryIds { get; }
		public IReadOnlyList<BotCharacterSlotView> CharacterSlots { get; }
		public IReadOnlyList<BotCountryView> Countries { get; }

		readonly Dictionary<string, BotCountryView> _countryById;

		BotObservation(
			string orgId,
			DateTime currentDate,
			double gold,
			int orgHandSize,
			int totalControl,
			IReadOnlyList<BotCardView> orgHand,
			IReadOnlyList<string> discoveredCountryIds,
			IReadOnlyList<BotCharacterSlotView> characterSlots,
			IReadOnlyList<BotCountryView> countries) {
			OrgId = orgId;
			CurrentDate = currentDate;
			Gold = gold;
			OrgHandSize = orgHandSize;
			TotalControl = totalControl;
			OrgHand = orgHand;
			DiscoveredCountryIds = discoveredCountryIds;
			CharacterSlots = characterSlots;
			Countries = countries;
			_countryById = new Dictionary<string, BotCountryView>();
			foreach (var c in countries) {
				_countryById[c.CountryId] = c;
			}
		}

		public BotCountryView? GetCountry(string countryId) {
			return _countryById.TryGetValue(countryId, out var view) ? view : null;
		}

		public static BotObservation Build(IReadOnlyWorld world, ActionConfig actionConfig, string orgId) {
			DateTime currentDate = default;
			int[] timeReq = { TypeId<GameTime>.Value };
			foreach (var arch in world.GetMatchingArchetypes(timeReq, null)) {
				if (arch.Count > 0) {
					currentDate = arch.GetColumn<GameTime>()[0].CurrentTime;
					break;
				}
			}

			double gold = OrgMetrics.GetGold(world, orgId);
			int totalControl = OrgMetrics.GetTotalControl(world, orgId);
			Dictionary<string, int> myControlByCountry = OrgMetrics.GetControlByCountry(world, orgId);

			var discovered = new SortedSet<string>(StringComparer.Ordinal);
			int[] discReq = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in world.GetMatchingArchetypes(discReq, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == orgId) { discovered.Add(dcs[i].CountryId); }
				}
			}

			var controlByCountry = new Dictionary<string, Dictionary<string, int>>();
			int[] controlReq = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(controlReq, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					string countryId = effects[i].CountryId;
					if (!discovered.Contains(countryId)) { continue; }
					if (!controlByCountry.TryGetValue(countryId, out var byOrg)) {
						byOrg = new Dictionary<string, int>();
						controlByCountry[countryId] = byOrg;
					}
					byOrg.TryGetValue(effects[i].OrgId, out int existing);
					byOrg[effects[i].OrgId] = existing + effects[i].Value;
				}
			}

			var orgHandCards = new List<BotCardView>();
			var countryHandCards = new Dictionary<string, List<BotCardView>>();

			int[] cardReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(cardReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgContexts = arch.GetColumn<OrgContext>();
				CardInHand[] inHands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgContexts[i].OrgId != orgId) { continue; }
					int entity = arch.Entities[i];
					string actionId = actions[i].ActionId;
					int slotIndex = inHands[i].SlotIndex;
					string? countryId = null;
					if (world.TryGet<CountryContext>(entity, out var cc)) {
						countryId = cc.CountryId;
					}

					var def = actionConfig.Find(actionId);
					var costs = new List<BotCostView>();
					double goldCost = 0;
					if (def != null) {
						foreach (var cost in def.Cost) {
							costs.Add(new BotCostView { ResourceId = cost.ResourceId, Amount = cost.Amount });
							if (cost.ResourceId == "gold") { goldCost += cost.Amount; }
						}
					}

					bool isPlayable = ActionPlayability.Evaluate(world, actionConfig, actionId, orgId, countryId);

					if (countryId == null) {
						orgHandCards.Add(new BotCardView {
							ActionId = actionId,
							SlotIndex = slotIndex,
							CountryId = "",
							Cost = costs,
							GoldCost = goldCost,
							IsPlayable = isPlayable
						});
					} else if (discovered.Contains(countryId)) {
						if (!countryHandCards.TryGetValue(countryId, out var list)) {
							list = new List<BotCardView>();
							countryHandCards[countryId] = list;
						}
						list.Add(new BotCardView {
							ActionId = actionId,
							SlotIndex = slotIndex,
							CountryId = countryId,
							Cost = costs,
							GoldCost = goldCost,
							IsPlayable = isPlayable
						});
					}
				}
			}
			orgHandCards.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

			int orgHandSize = 0;
			int[] deckReq = { TypeId<CardDeck>.Value, TypeId<CardHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && string.IsNullOrEmpty(decks[i].CountryId)) {
						orgHandSize = hands[i].HandSize;
						break;
					}
				}
			}

			var slots = new List<BotCharacterSlotView>();
			int[] slotReq = { TypeId<CharacterSlot>.Value };
			foreach (var arch in world.GetMatchingArchetypes(slotReq, null)) {
				CharacterSlot[] charSlots = arch.GetColumn<CharacterSlot>();
				for (int i = 0; i < arch.Count; i++) {
					if (charSlots[i].OwnerId != orgId) { continue; }
					slots.Add(new BotCharacterSlotView {
						RoleId = charSlots[i].RoleId,
						SlotIndex = charSlots[i].SlotIndex,
						IsAvailable = charSlots[i].IsAvailable,
						CharacterId = charSlots[i].CharacterId
					});
				}
			}
			slots.Sort((a, b) => {
				int cmp = string.CompareOrdinal(a.RoleId, b.RoleId);
				return cmp != 0 ? cmp : a.SlotIndex.CompareTo(b.SlotIndex);
			});

			string opinionResourceId = $"opinion_{orgId}";
			var charactersByCountry = new Dictionary<string, List<BotCountryCharacterView>>();
			int[] charReq = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(charReq, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					string countryId = chars[i].CountryId;
					if (string.IsNullOrEmpty(countryId) || !discovered.Contains(countryId)) { continue; }

					double opinion = 0.0;
					int resourceEntity = ActionPlayability.FindResourceEntity(world, chars[i].CharacterId, opinionResourceId);
					if (resourceEntity >= 0) {
						opinion = world.Get<Resource>(resourceEntity).Value;
					}

					if (!charactersByCountry.TryGetValue(countryId, out var list)) {
						list = new List<BotCountryCharacterView>();
						charactersByCountry[countryId] = list;
					}
					list.Add(new BotCountryCharacterView {
						CharacterId = chars[i].CharacterId,
						RoleId = chars[i].RoleId,
						OpinionOfMyOrg = opinion
					});
				}
			}
			foreach (var list in charactersByCountry.Values) {
				list.Sort((a, b) => string.CompareOrdinal(a.CharacterId, b.CharacterId));
			}

			var countries = new List<BotCountryView>();
			foreach (string countryId in discovered) {
				int myControl = myControlByCountry.TryGetValue(countryId, out int mc) ? mc : 0;

				var shares = new List<BotControlShare>();
				int total = 0;
				if (controlByCountry.TryGetValue(countryId, out var byOrg)) {
					foreach (var kv in byOrg) {
						shares.Add(new BotControlShare { OrgId = kv.Key, Control = kv.Value });
						total += kv.Value;
					}
				}
				shares.Sort((a, b) => string.CompareOrdinal(a.OrgId, b.OrgId));

				var hand = countryHandCards.TryGetValue(countryId, out var handList) ? handList : new List<BotCardView>();
				hand.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

				var characters = charactersByCountry.TryGetValue(countryId, out var charList) ? charList : new List<BotCountryCharacterView>();

				countries.Add(new BotCountryView {
					CountryId = countryId,
					MyControl = myControl,
					TotalControl = total,
					ControlByOrg = shares,
					Hand = hand,
					Characters = characters
				});
			}

			return new BotObservation(
				orgId,
				currentDate,
				gold,
				orgHandSize,
				totalControl,
				orgHandCards,
				new List<string>(discovered),
				slots,
				countries);
		}
	}
}
