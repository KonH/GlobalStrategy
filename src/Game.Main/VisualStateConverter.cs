using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Game.Configs;

namespace GS.Main {
	public class VisualStateConverter {
		readonly VisualState _state;
		readonly System.Collections.Generic.HashSet<string> _previousDiscoveredIds = new();
		readonly Dictionary<string, AnimatableInt> _characterOpinionAnimatables = new();
		readonly Dictionary<(string, string), AnimatableDouble> _resourceAnimatables = new();
		readonly IReadOnlyDictionary<string, string> _hqCountryByOrgId;
		readonly CountryConfig? _countryConfig;
		ActionConfig? _actionConfig;
		int _lastSeenProvinceOwnershipVersion = -1;
		int _lastSeenProvinceOccupationVersion = -1;

		readonly List<GameLogEntry> _gameLogEntries = new();
		long _nextGameLogSequenceId = 1;
		readonly bool _gameLogIncludePlayerActions;
		readonly int _gameLogMaxEntries;

		static readonly string[] s_roleOrder = { "ruler", "military_advisor", "diplomacy_advisor", "economic_advisor", "secret_advisor" };
		static readonly string[] s_orgRoleOrder = { "master", "agent" };

		public VisualStateConverter(VisualState state, ActionConfig? actionConfig = null,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			bool gameLogIncludePlayerActions = true, int gameLogMaxEntries = 12,
			CountryConfig? countryConfig = null) {
			_state = state;
			_actionConfig = actionConfig;
			_hqCountryByOrgId = hqCountryByOrgId ?? new Dictionary<string, string>();
			_countryConfig = countryConfig;
			_gameLogIncludePlayerActions = gameLogIncludePlayerActions;
			_gameLogMaxEntries = gameLogMaxEntries;
		}

		public void Update(float deltaTime, IReadOnlyWorld world, int gameTimeEntity, int localeEntity, int orgEntity) {
			UpdateLastFrameEffects(world);
			UpdateSelectedCountry(world);
			UpdateTime(world, gameTimeEntity);
			UpdateLocale(world, localeEntity);
			UpdatePlayerOrganization(world, orgEntity);
			UpdateGameCompletion(world, orgEntity);
			UpdateResources(world);
			UpdateSelectedControl(world);
			UpdateCharacters(world, orgEntity);
			UpdateOrgCharacters(world);
			UpdateOrgMap(world, orgEntity);
			UpdateDiscoveredCountries(world, orgEntity);
			UpdateOrgActions(world);
			UpdateCountryActions(world, gameTimeEntity);
			UpdateProvinceOwnership(world);
			UpdateProvinceOccupation(world);
			UpdateSelectedProvince(world);
			UpdateCountryScore(world);
			UpdateLeaderboards(world);
			UpdateGameLog(world, orgEntity);

			// Tick all animatables
			foreach (var animatable in _characterOpinionAnimatables.Values) {
				animatable.Tick(deltaTime);
			}
			foreach (var animatable in _resourceAnimatables.Values) {
				animatable.Tick(deltaTime);
			}
			_state.SelectedCountry.Control.UsedControl.Tick(deltaTime);
		}

		void UpdateLastFrameEffects(IReadOnlyWorld world) {
			var effects = new List<VisualResourceChangeEffect>();
			int[] req = { TypeId<ResourceChange>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceChange[] changes = arch.GetColumn<ResourceChange>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					effects.Add(new VisualResourceChangeEffect(
						changes[i].EffectId,
						changes[i].ResourceId,
						changes[i].OwnerId,
						changes[i].Amount));
				}
			}
			_state.LastFrameEffects.Set(effects);
		}

		void UpdateCharacters(IReadOnlyWorld world, int orgEntity) {
			if (!_state.SelectedCountry.IsValid) {
				_characterOpinionAnimatables.Clear();
				_state.SelectedCountry.Characters.Set(new List<CharacterStateEntry>());
				return;
			}
			string selectedCountryId = _state.SelectedCountry.CountryId;

			string playerOrgId = orgEntity >= 0 ? world.Get<Organization>(orgEntity).OrganizationId : "";

			var charData = new Dictionary<string, (string roleId, string[] namePartKeys)>();
			var charSkills = new Dictionary<string, List<SkillEntry>>();
			string opinionResourceId = $"opinion_{playerOrgId}";
			var charOpinionValues = new Dictionary<string, int>();

			int[] charRequired = { TypeId<Character>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(charRequired, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (chars[i].CountryId != selectedCountryId) {
						continue;
					}
					charData[chars[i].CharacterId] = (chars[i].RoleId, chars[i].NamePartKeys);
					charSkills[chars[i].CharacterId] = new List<SkillEntry>();
				}
			}

			int[] resRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(resRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerType == OwnerType.Character) {
						if (resources[i].ResourceId == opinionResourceId && charData.ContainsKey(owners[i].OwnerId)) {
							charOpinionValues[owners[i].OwnerId] = Math.Clamp((int)resources[i].Value, -100, 100);
						} else if (charSkills.TryGetValue(owners[i].OwnerId, out var skillList)) {
							skillList.Add(new SkillEntry(resources[i].ResourceId, (int)resources[i].Value));
						}
					}
				}
			}

			var entries = new List<CharacterStateEntry>();
			foreach (var (charId, (roleId, namePartKeys)) in charData) {
				charOpinionValues.TryGetValue(charId, out int effective);
				if (!_characterOpinionAnimatables.TryGetValue(charId, out var opinionAnimatable)) {
					opinionAnimatable = new AnimatableInt();
					_characterOpinionAnimatables[charId] = opinionAnimatable;
				}
				opinionAnimatable.SetActual(effective);
				entries.Add(new CharacterStateEntry(charId, roleId, namePartKeys, charSkills[charId], opinionAnimatable));
			}
			entries.Sort((a, b) => {
				int ai = Array.IndexOf(s_roleOrder, a.RoleId);
				int bi = Array.IndexOf(s_roleOrder, b.RoleId);
				if (ai < 0) { ai = int.MaxValue; }
				if (bi < 0) { bi = int.MaxValue; }
				return ai.CompareTo(bi);
			});

			var newCharIds = new System.Collections.Generic.HashSet<string>();
			foreach (var entry in entries) {
				newCharIds.Add(entry.CharacterId);
			}
			var keysToRemove = new System.Collections.Generic.List<string>();
			foreach (var key in _characterOpinionAnimatables.Keys) {
				if (!newCharIds.Contains(key)) { keysToRemove.Add(key); }
			}
			foreach (var key in keysToRemove) { _characterOpinionAnimatables.Remove(key); }

			_state.SelectedCountry.Characters.Set(entries);
		}

		void UpdateOrgCharacters(IReadOnlyWorld world) {
			if (!_state.PlayerOrganization.IsValid) {
				_state.PlayerOrganization.Characters.Set(new List<OrgCharacterSlotEntry>());
				return;
			}
			string orgId = _state.PlayerOrganization.OrgId;

			var charData = new Dictionary<string, (string roleId, string[] namePartKeys)>();
			var charSkills = new Dictionary<string, List<SkillEntry>>();

			int[] charRequired = { TypeId<Character>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(charRequired, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (chars[i].OrgId != orgId) {
						continue;
					}
					charData[chars[i].CharacterId] = (chars[i].RoleId, chars[i].NamePartKeys);
					charSkills[chars[i].CharacterId] = new List<SkillEntry>();
				}
			}

			int[] resRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(resRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerType == OwnerType.Character && charSkills.TryGetValue(owners[i].OwnerId, out var skillList)) {
						skillList.Add(new SkillEntry(resources[i].ResourceId, (int)resources[i].Value));
					}
				}
			}

			var slotEntries = new List<OrgCharacterSlotEntry>();
			int[] slotRequired = { TypeId<CharacterSlot>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(slotRequired, null)) {
				CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (slots[i].OwnerId != orgId) {
						continue;
					}
					CharacterStateEntry? charEntry = null;
					string cid = slots[i].CharacterId;
					if (!string.IsNullOrEmpty(cid) && charData.TryGetValue(cid, out var cd)) {
						charEntry = new CharacterStateEntry(cid, cd.roleId, cd.namePartKeys, charSkills[cid], new AnimatableInt());
					}
					slotEntries.Add(new OrgCharacterSlotEntry(
						slots[i].RoleId, slots[i].SlotIndex, charEntry, slots[i].IsAvailable));
				}
			}

			slotEntries.Sort((a, b) => {
				int ai = Array.IndexOf(s_orgRoleOrder, a.RoleId);
				int bi = Array.IndexOf(s_orgRoleOrder, b.RoleId);
				if (ai < 0) { ai = int.MaxValue; }
				if (bi < 0) { bi = int.MaxValue; }
				int rc = ai.CompareTo(bi);
				return rc != 0 ? rc : a.SlotIndex.CompareTo(b.SlotIndex);
			});

			_state.PlayerOrganization.Characters.Set(slotEntries);
		}

		void UpdateSelectedCountry(IReadOnlyWorld world) {
			int[] required = { TypeId<Country>.Value, TypeId<IsSelected>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) {
					continue;
				}
				Country[] countries = arch.GetColumn<Country>();
				_state.SelectedCountry.Set(true, countries[0].CountryId);
				return;
			}
			_state.SelectedCountry.Set(false, "");
		}

		void UpdateTime(IReadOnlyWorld world, int gameTimeEntity) {
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			_state.Time.Set(time.CurrentTime, time.IsPaused, time.MultiplierIndex);
		}

		void UpdateLocale(IReadOnlyWorld world, int localeEntity) {
			ref Locale locale = ref world.Get<Locale>(localeEntity);
			_state.Locale.Set(locale.Value);
		}

		void UpdatePlayerOrganization(IReadOnlyWorld world, int orgEntity) {
			if (orgEntity < 0) {
				_state.PlayerOrganization.Set(false, "", "", "");
				return;
			}
			ref Organization org = ref world.Get<Organization>(orgEntity);
			_hqCountryByOrgId.TryGetValue(org.OrganizationId, out var hqCountryId);
			_state.PlayerOrganization.Set(true, org.OrganizationId, org.DisplayName, hqCountryId ?? "");
		}

		void UpdateGameCompletion(IReadOnlyWorld world, int orgEntity) {
			int[] required = { TypeId<GameCompletion>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				if (archetype.Count == 0) {
					continue;
				}

				GameCompletion completion = archetype.GetColumn<GameCompletion>()[0];
				if (!completion.IsCompleted) {
					_state.GameCompletion.Set(false, completion.WinnerOrganizationId ?? "", GameResult.InProgress);
					return;
				}

				string playerOrganizationId = orgEntity >= 0
					? world.Get<Organization>(orgEntity).OrganizationId
					: "";
				GameResult result = playerOrganizationId == completion.WinnerOrganizationId
					? GameResult.Win
					: GameResult.Lose;
				_state.GameCompletion.Set(true, completion.WinnerOrganizationId ?? "", result);
				return;
			}

			_state.GameCompletion.Set(false, "", GameResult.InProgress);
		}

		void UpdateResources(IReadOnlyWorld world) {
			string playerOrgId = _state.PlayerOrganization.IsValid ? _state.PlayerOrganization.OrgId : "";
			string selectedCountryId = _state.SelectedCountry.IsValid ? _state.SelectedCountry.CountryId : "";

			List<ControlIncomeEntry>? orgControlIncomes = null;
			if (_state.PlayerOrganization.IsValid) {
				orgControlIncomes = BuildControlIncomesForOrg(world, playerOrgId);
			}

			_state.PlayerOrganization.Resources.Set(
				_state.PlayerOrganization.IsValid,
				playerOrgId,
				BuildResources(world, playerOrgId),
				orgControlIncomes);
			_state.SelectedCountry.Resources.Set(
				_state.SelectedCountry.IsValid,
				selectedCountryId,
				BuildResources(world, selectedCountryId));
		}

		List<ControlIncomeEntry> BuildControlIncomesForOrg(IReadOnlyWorld world, string orgId) {
			var result = new List<ControlIncomeEntry>();
			int[] required = { TypeId<ControlEffect>.Value };
			var byCountry = new Dictionary<string, int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId != orgId) {
						continue;
					}
					string cid = effects[i].CountryId;
					if (!byCountry.TryGetValue(cid, out int v)) {
						v = 0;
					}
					byCountry[cid] = v + effects[i].Value;
				}
			}
			foreach (var (countryId, control) in byCountry) {
				double baseIncome = ControlSystem.ComputeBaseMonthlyGold(world, countryId);
				double gain = Math.Round((control / 100.0) * baseIncome, 2);
				result.Add(new ControlIncomeEntry(countryId, gain));
			}
			return result;
		}

		void UpdateSelectedControl(IReadOnlyWorld world) {
			string selectedCountryId = _state.SelectedCountry.IsValid ? _state.SelectedCountry.CountryId : "";
			if (!_state.SelectedCountry.IsValid) {
				_state.SelectedCountry.Control.Set(0, new List<OrgControlEntry>());
				return;
			}

			// Gather org display names
			var orgNames = new Dictionary<string, string>();
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					orgNames[orgs[i].OrganizationId] = orgs[i].DisplayName;
				}
			}

			// Group control by org for the selected country (track base vs permanent)
			var byOrgBase = new Dictionary<string, int>();
			var byOrgPermanent = new Dictionary<string, int>();
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].CountryId != selectedCountryId) {
						continue;
					}
					string oid = effects[i].OrgId;
					bool isBase = effects[i].EffectId.StartsWith("base_");
					if (isBase) {
						if (!byOrgBase.TryGetValue(oid, out int v)) {
							v = 0;
						}
						byOrgBase[oid] = v + effects[i].Value;
					} else {
						if (!byOrgPermanent.TryGetValue(oid, out int v)) {
							v = 0;
						}
						byOrgPermanent[oid] = v + effects[i].Value;
					}
				}
			}

			double baseIncome = ControlSystem.ComputeBaseMonthlyGold(world, selectedCountryId);
			int usedTotal = 0;
			var entries = new List<OrgControlEntry>();
			var allOrgs = new System.Collections.Generic.HashSet<string>(byOrgBase.Keys);
			foreach (var k in byOrgPermanent.Keys) {
				allOrgs.Add(k);
			}
			foreach (string orgId in allOrgs) {
				int baseInfl = byOrgBase.TryGetValue(orgId, out int b) ? b : 0;
				int permInfl = byOrgPermanent.TryGetValue(orgId, out int p) ? p : 0;
				int control = baseInfl + permInfl;
				usedTotal += control;
				string displayName = orgNames.TryGetValue(orgId, out string? n) ? n : orgId;
				double gain = Math.Round((control / 100.0) * baseIncome, 2);
				entries.Add(new OrgControlEntry(orgId, displayName, control, baseInfl, permInfl, gain));
			}
			entries.Sort((a, b) => b.Control.CompareTo(a.Control));
			_state.SelectedCountry.Control.Set(usedTotal, entries);
		}

		List<ResourceStateEntry> BuildResources(IReadOnlyWorld world, string countryId) {
			var result = new List<ResourceStateEntry>();
			if (string.IsNullOrEmpty(countryId)) {
				return result;
			}
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId) {
						continue;
					}
					var key = (countryId, resources[i].ResourceId);
					if (!_resourceAnimatables.TryGetValue(key, out var animatable)) {
						animatable = new AnimatableDouble();
						_resourceAnimatables[key] = animatable;
					}
					animatable.SetActual(resources[i].Value);
					var effects = BuildEffects(world, countryId, resources[i].ResourceId);
					result.Add(new ResourceStateEntry(resources[i].ResourceId, animatable, effects));
				}
			}
			return result;
		}

		void UpdateOrgMap(IReadOnlyWorld world, int orgEntity) {
			var byCountryOrg = new Dictionary<string, Dictionary<string, int>>();
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string cid = effects[i].CountryId;
					string oid = effects[i].OrgId;
					if (!byCountryOrg.TryGetValue(cid, out var orgMap)) {
						orgMap = new Dictionary<string, int>();
						byCountryOrg[cid] = orgMap;
					}
					if (!orgMap.TryGetValue(oid, out int v)) {
						v = 0;
					}
					orgMap[oid] = v + effects[i].Value;
				}
			}
			var entries = new List<OrgCountryEntry>();
			foreach (var (countryId, orgControls) in byCountryOrg) {
				string topOrgId = "";
				int topControl = 0;
				foreach (var (oid, inf) in orgControls) {
					if (inf > topControl) {
						topControl = inf;
						topOrgId = oid;
					}
				}
				if (topControl > 0) {
					float ratio = Math.Min(1f, topControl / 100f);
					entries.Add(new OrgCountryEntry(countryId, topOrgId, ratio));
				}
			}
			_state.OrgMap.Set(entries);
		}

		void UpdateDiscoveredCountries(IReadOnlyWorld world, int orgEntity) {
			string viewOrgId = orgEntity >= 0 ? world.Get<Organization>(orgEntity).OrganizationId : "";
			var ids = new System.Collections.Generic.HashSet<string>();
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == viewOrgId) { ids.Add(dcs[i].CountryId); }
				}
			}

			string recently = "";
			if (_previousDiscoveredIds.Count > 0) {
				foreach (var id in ids) {
					if (!_previousDiscoveredIds.Contains(id)) { recently = id; break; }
				}
			}
			_previousDiscoveredIds.Clear();
			foreach (var id in ids) { _previousDiscoveredIds.Add(id); }

			string pendingRecently = recently != "" ? recently : _state.DiscoveredCountries.RecentlyDiscovered;
			_state.DiscoveredCountries.Set(ids, pendingRecently);
		}

		void UpdateOrgActions(IReadOnlyWorld world) {
			if (!_state.PlayerOrganization.IsValid) {
				_state.PlayerOrganization.Actions.Set(
					new System.Collections.Generic.List<ActionCardEntry>(),
					new System.Collections.Generic.List<ActionCardEntry>(), 0);
				return;
			}
			string orgId = _state.PlayerOrganization.OrgId;

			int handSize = 1;
			int[] deckHandReq = { TypeId<CardDeck>.Value, TypeId<CardHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckHandReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && decks[i].CountryId == "") { handSize = hands[i].HandSize; break; }
				}
			}

			var hand = new System.Collections.Generic.List<ActionCardEntry>();
			var deck = new System.Collections.Generic.List<ActionCardEntry>();

			int[] handReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			int[] excludeCountry = { TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(handReq, excludeCountry)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId != orgId) { continue; }
					hand.Add(new ActionCardEntry(actions[i].ActionId, hands[i].SlotIndex, true));
				}
			}
			int[] deckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value };
			int[] excludeInHandOrCountry = { TypeId<CardInHand>.Value, TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHandOrCountry)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId != orgId) { continue; }
					deck.Add(new ActionCardEntry(actions[i].ActionId, -1, false));
				}
			}
			hand.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
			_state.PlayerOrganization.Actions.Set(hand, deck, handSize);
		}

		void UpdateCountryActions(IReadOnlyWorld world, int gameTimeEntity) {
			if (!_state.SelectedCountry.IsValid || !_state.PlayerOrganization.IsValid || _actionConfig == null) {
				_state.SelectedCountry.CountryActions.Set(
					new List<ActionCardEntry>(),
					new List<ActionCardEntry>(),
					0, DateTime.MinValue);
				return;
			}
			string orgId = _state.PlayerOrganization.OrgId;
			string countryId = _state.SelectedCountry.CountryId;

			DateTime currentTime = gameTimeEntity >= 0
				? world.Get<GameTime>(gameTimeEntity).CurrentTime
				: DateTime.MinValue;

			// Compute org control in country
			int orgControl = 0;
			int usedTotal = 0;
			int[] infReq = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(infReq, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].CountryId == countryId) {
						usedTotal += effects[i].Value;
						if (effects[i].OrgId == orgId) {
							orgControl += effects[i].Value;
						}
					}
				}
			}

			var hand = new List<ActionCardEntry>();
			var deck = new List<ActionCardEntry>();

			// Collect all country card entities with OrgContext + CountryContext + GameAction
			int[] baseReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value };
			int[] handReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
			int[] excludeHand = { TypeId<CardInHand>.Value };

			// Hand cards
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId || countries[i].CountryId != countryId) { continue; }
					var entry = BuildEntry(actions[i].ActionId, hands[i].SlotIndex, true, orgControl, usedTotal);
					if (entry != null) { hand.Add(entry); }
				}
			}

			// Deck cards
			foreach (var arch in world.GetMatchingArchetypes(baseReq, excludeHand)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId || countries[i].CountryId != countryId) { continue; }
					var entry = BuildEntry(actions[i].ActionId, -1, false, orgControl, usedTotal);
					if (entry != null) { deck.Add(entry); }
				}
			}

			hand.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
			int countryHandSize = _actionConfig?.GetHandSize("country") ?? 3;
			_state.SelectedCountry.CountryActions.Set(hand, deck, countryHandSize, currentTime);
		}

		ActionCardEntry? BuildEntry(
			string actionId, int slotIndex, bool isInHand,
			int orgControl, int usedTotal) {
			var def = _actionConfig?.Find(actionId);
			if (def == null) { return null; }

			var ctx = new ExpressionContext { Control = orgControl };

			bool insufficientControl = false;
			foreach (var cond in def.Conditions) {
				if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { insufficientControl = true; break; }
			}
			bool poolFull = actionId == "sphere_of_pressure" && usedTotal >= 100;
			bool isUnplayable = insufficientControl || poolFull;
			string unplayableReason = poolFull ? "pool_full" : (insufficientControl ? "insufficient_control" : "");

			return new ActionCardEntry(actionId, slotIndex, isInHand, isUnplayable, unplayableReason);
		}

		void UpdateProvinceOwnership(IReadOnlyWorld world) {
			int currentVersion = ProvinceOwnershipSystem.GetVersion(world);
			if (currentVersion == _lastSeenProvinceOwnershipVersion) {
				return;
			}
			_lastSeenProvinceOwnershipVersion = currentVersion;

			var ownerByProvinceId = new Dictionary<string, string>();
			int[] required = { TypeId<ProvinceOwnership>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ProvinceOwnership[] ownerships = arch.GetColumn<ProvinceOwnership>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ownerByProvinceId[ownerships[i].ProvinceId] = ownerships[i].OwnerId;
				}
			}

			_state.ProvinceOwnership.Set(
				ownerByProvinceId,
				_state.ProvinceOwnership.RecentProvinceId,
				_state.ProvinceOwnership.RecentOldOwnerId,
				_state.ProvinceOwnership.RecentNewOwnerId);
		}

		void UpdateProvinceOccupation(IReadOnlyWorld world) {
			int currentVersion = ProvinceOccupationSystem.GetVersion(world);
			if (currentVersion == _lastSeenProvinceOccupationVersion) {
				return;
			}
			_lastSeenProvinceOccupationVersion = currentVersion;

			_state.ProvinceOccupation.Set(
				ProvinceOccupationSystem.GetOccupierByProvinceId(world),
				_state.ProvinceOccupation.RecentProvinceId,
				_state.ProvinceOccupation.RecentOldOccupierId,
				_state.ProvinceOccupation.RecentNewOccupierId);
		}

		public void UpdateSelectedProvince(IReadOnlyWorld world) {
			int[] required = { TypeId<ProvinceSelection>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) {
					continue;
				}
				ProvinceSelection[] selections = arch.GetColumn<ProvinceSelection>();
				string provinceId = selections[0].ProvinceId;
				if (!string.IsNullOrEmpty(provinceId)) {
					_state.SelectedProvince.Set(true, provinceId);
					_state.SelectedProvince.Resources.Set(true, provinceId, BuildResources(world, provinceId));
					return;
				}
			}
			_state.SelectedProvince.Set(false, "");
			_state.SelectedProvince.Resources.Set(false, "", new List<ResourceStateEntry>());
		}

		public void UpdateCountryScore(IReadOnlyWorld world) {
			var scoreByCountryId = new Dictionary<string, double>();
			foreach (string countryId in GetCountryIds(world)) {
				scoreByCountryId[countryId] = ResourceQuery.GetValue(world, countryId, ResourceDefinitions.CountryScore);
			}
			_state.CountryScore.Set(scoreByCountryId);
		}

		public void UpdateLeaderboards(IReadOnlyWorld world) {
			var organizations = new List<LeaderboardEntryState>();
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string orgId = orgs[i].OrganizationId;
					organizations.Add(new LeaderboardEntryState(
						0,
						orgId,
						string.IsNullOrEmpty(orgs[i].DisplayName) ? orgId : orgs[i].DisplayName,
						ResourceQuery.GetValue(world, orgId, ResourceDefinitions.OrgScore)));
				}
			}

			var countries = new List<LeaderboardEntryState>();
			foreach (string countryId in GetCountryIds(world)) {
				countries.Add(new LeaderboardEntryState(
					0,
					countryId,
					GetCountryDisplayName(countryId),
					ResourceQuery.GetValue(world, countryId, ResourceDefinitions.CountryScore)));
			}

			SortAndAssignPlaces(organizations);
			SortAndAssignPlaces(countries);
			_state.Leaderboard.Set(organizations, countries);
		}

		IReadOnlyList<string> GetCountryIds(IReadOnlyWorld world) {
			var ids = new List<string>();
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ids.Add(countries[i].CountryId);
				}
			}
			ids.Sort(StringComparer.Ordinal);
			return ids;
		}

		string GetCountryDisplayName(string countryId) {
			var entry = _countryConfig?.FindByCountryId(countryId);
			if (entry != null && !string.IsNullOrEmpty(entry.DisplayName)) {
				return entry.DisplayName;
			}
			return countryId;
		}

		static void SortAndAssignPlaces(List<LeaderboardEntryState> entries) {
			entries.Sort((a, b) => {
				int scoreCompare = b.Score.CompareTo(a.Score);
				if (scoreCompare != 0) {
					return scoreCompare;
				}
				int nameCompare = StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName);
				if (nameCompare != 0) {
					return nameCompare;
				}
				return StringComparer.Ordinal.Compare(a.EntityId, b.EntityId);
			});

			for (int i = 0; i < entries.Count; i++) {
				var entry = entries[i];
				entries[i] = new LeaderboardEntryState(i + 1, entry.EntityId, entry.DisplayName, entry.Score);
			}
		}

		// Collection pass, not a diff pass — modeled on UpdateLastFrameEffects above, which already
		// scans a transient one-shot component archetype (ResourceChange) every tick the same way.
		// No baseline/init-guard needed: the four source components are only ever created at the
		// exact point their underlying effect is applied, never during InitSystem seeding, so there
		// is structurally nothing to collect on a fresh/loaded game. See
		// Docs/Specs/26_07_18_07_action-log-ui/plan.md "Collection logic" section.
		void UpdateGameLog(IReadOnlyWorld world, int orgEntity) {
			string playerOrgId = _state.PlayerOrganization.IsValid ? _state.PlayerOrganization.OrgId : "";
			var newEntries = new List<GameLogEntry>();

			int[] controlReq = { TypeId<ControlEffectApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(controlReq, null)) {
				ControlEffectApplied[] applied = arch.GetColumn<ControlEffectApplied>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!_gameLogIncludePlayerActions && applied[i].OrgId == playerOrgId) { continue; }
					newEntries.Add(new GameLogEntry(0, GameLogEntryKind.Control, applied[i].OrgId, applied[i].CountryId,
						"", "", Array.Empty<string>(), applied[i].Delta, applied[i].Total, false));
				}
			}

			bool opinionArchetypeNonEmpty = false;
			int[] opinionReq = { TypeId<OpinionEffectApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(opinionReq, null)) {
				if (arch.Count > 0) { opinionArchetypeNonEmpty = true; break; }
			}
			bool roleChangeArchetypeNonEmpty = false;
			int[] roleChangeReq = { TypeId<RoleChangeApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(roleChangeReq, null)) {
				if (arch.Count > 0) { roleChangeArchetypeNonEmpty = true; break; }
			}

			Dictionary<string, (string RoleId, string CountryId, string OrgId, string[] NamePartKeys)>? charLookup = null;
			if (opinionArchetypeNonEmpty || roleChangeArchetypeNonEmpty) {
				charLookup = new Dictionary<string, (string, string, string, string[])>();
				int[] charReq = { TypeId<Character>.Value };
				foreach (Archetype arch in world.GetMatchingArchetypes(charReq, null)) {
					Character[] chars = arch.GetColumn<Character>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						charLookup[chars[i].CharacterId] = (chars[i].RoleId, chars[i].CountryId, chars[i].OrgId, chars[i].NamePartKeys);
					}
				}
			}

			if (opinionArchetypeNonEmpty) {
				foreach (Archetype arch in world.GetMatchingArchetypes(opinionReq, null)) {
					OpinionEffectApplied[] applied = arch.GetColumn<OpinionEffectApplied>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (charLookup == null || !charLookup.TryGetValue(applied[i].CharacterId, out var charInfo)) { continue; }
						if (!_gameLogIncludePlayerActions && applied[i].OrgId == playerOrgId) { continue; }
						double clampedTotal = Math.Clamp((int)applied[i].Total, -100, 100);
						newEntries.Add(new GameLogEntry(0, GameLogEntryKind.Opinion, applied[i].OrgId, charInfo.CountryId,
							applied[i].CharacterId, charInfo.RoleId, charInfo.NamePartKeys, applied[i].Delta, clampedTotal, false));
					}
				}
			}

			int[] discoveryReq = { TypeId<DiscoveryApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(discoveryReq, null)) {
				DiscoveryApplied[] applied = arch.GetColumn<DiscoveryApplied>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!_gameLogIncludePlayerActions && applied[i].OrgId == playerOrgId) { continue; }
					newEntries.Add(new GameLogEntry(0, GameLogEntryKind.Discovery, applied[i].OrgId, applied[i].CountryId,
						"", "", Array.Empty<string>(), 0, 0, false));
				}
			}

			if (roleChangeArchetypeNonEmpty) {
				foreach (Archetype arch in world.GetMatchingArchetypes(roleChangeReq, null)) {
					RoleChangeApplied[] applied = arch.GetColumn<RoleChangeApplied>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						bool isOrgRole = !string.IsNullOrEmpty(applied[i].OrgId);
						if (isOrgRole && !_gameLogIncludePlayerActions && applied[i].OrgId == playerOrgId) { continue; }
						string[] namePartKeys = Array.Empty<string>();
						if (charLookup != null && charLookup.TryGetValue(applied[i].CharacterId, out var charInfo)) {
							namePartKeys = charInfo.NamePartKeys;
						}
						newEntries.Add(new GameLogEntry(0, GameLogEntryKind.NewCharacter, applied[i].OrgId, applied[i].CountryId,
							applied[i].CharacterId, applied[i].RoleId, namePartKeys, 0, 0, isOrgRole));
					}
				}
			}

			if (newEntries.Count == 0) { return; }

			foreach (var entry in newEntries) {
				_gameLogEntries.Add(new GameLogEntry(_nextGameLogSequenceId++, entry.Kind, entry.OrgId, entry.CountryId,
					entry.CharacterId, entry.RoleId, entry.NamePartKeys, entry.Delta, entry.Total, entry.IsOrgRole));
			}
			while (_gameLogEntries.Count > _gameLogMaxEntries) {
				_gameLogEntries.RemoveAt(0);
			}

			_state.GameLog.Set(new List<GameLogEntry>(_gameLogEntries));
		}

		List<EffectStateEntry> BuildEffects(IReadOnlyWorld world, string countryId, string resourceId) {
			var result = new List<EffectStateEntry>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId || links[i].ResourceId != resourceId) {
						continue;
					}
					result.Add(new EffectStateEntry(effects[i].EffectId, effects[i].Value, effects[i].PayType));
				}
			}
			return result;
		}
	}
}
