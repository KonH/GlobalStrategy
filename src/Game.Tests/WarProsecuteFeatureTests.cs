using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarProsecuteFeatureTests {
		const string BotOrg = "Illuminati";
		const string RivalOrg = "Templars";

		sealed class FakeObservation : IBotObservation {
			readonly Dictionary<string, BotCountryView> _byId = new();

			public string OrgId { get; set; } = BotOrg;
			public DateTime CurrentDate => default;
			public double Gold { get; set; } = 1000;
			public double OrgScore { get; set; } = 100;
			public IReadOnlyList<BotOrgScoreView> OrgScores { get; set; } = new[] {
				new BotOrgScoreView { OrgId = BotOrg, OrgScore = 100 },
				new BotOrgScoreView { OrgId = RivalOrg, OrgScore = 200 }
			};
			public int OrgHandSize => 0;
			public int CountryHandCount => 0;
			public int CountryHandCapacity => 0;
			public bool CanStartCountryCardDraw => false;
			public int TotalControl => 0;
			public IReadOnlyList<BotCardView> OrgHand => Array.Empty<BotCardView>();
			public IReadOnlyList<BotCardDrawChoiceView> CountryCardDrawChoices => Array.Empty<BotCardDrawChoiceView>();
			public IReadOnlyList<BotCharacterSlotView> CharacterSlots => Array.Empty<BotCharacterSlotView>();
			public IReadOnlyList<BotCountryView> Countries { get; private set; } = Array.Empty<BotCountryView>();

			public void SetCountries(params BotCountryView[] countries) {
				Countries = countries;
				_byId.Clear();
				foreach (var c in countries) {
					_byId[c.CountryId] = c;
				}
			}

			public BotCountryView? GetCountry(string countryId) =>
				_byId.TryGetValue(countryId, out var view) ? view : null;
		}

		static BotControlShare Share(string orgId, int control) =>
			new BotControlShare { OrgId = orgId, Control = control };

		static BotCountryView Country(
			string id,
			int myControl,
			int rivalControl,
			double countryScore,
			int ownedProvinces,
			double recruits,
			double damage,
			double durability,
			bool atWar = false,
			string opponentId = "",
			double ownWarProgress = 0,
			double troopsDamageBonusPercent = 0,
			int occupiedOwned = 0,
			params BotCardView[] hand) {
			int total = myControl + rivalControl;
			return new BotCountryView {
				CountryId = id,
				MyControl = myControl,
				TotalControl = total,
				ControlByOrg = new[] {
					Share(BotOrg, myControl),
					Share(RivalOrg, rivalControl)
				},
				CountryScore = countryScore,
				OwnedProvinceCount = ownedProvinces,
				OccupiedOwnedProvinceCount = occupiedOwned,
				Recruits = recruits,
				Damage = damage,
				Durability = durability,
				IsAtWar = atWar,
				WarOpponentCountryId = opponentId,
				OwnWarProgress = ownWarProgress,
				TroopsDamageBonusPercent = troopsDamageBonusPercent,
				Hand = hand,
				RivalCountryIds = Array.Empty<string>()
			};
		}

		static BotCardView Card(
			string actionId,
			string countryId,
			int slot,
			double goldCost,
			bool playable = true,
			string targetId = "") {
			return new BotCardView {
				ActionId = actionId,
				CountryId = countryId,
				TargetCountryId = targetId,
				SlotIndex = slot,
				GoldCost = goldCost,
				IsPlayable = playable
			};
		}

		static WarProsecuteFeature Feature(Dictionary<string, double>? parameters = null, double sellArmsBonus = 30.0) =>
			new WarProsecuteFeature(parameters ?? new Dictionary<string, double>(), 100, sellArmsBonus);

		static List<BotPlayProposal> Collect(WarProsecuteFeature feature, FakeObservation obs) {
			var proposals = new List<BotPlayProposal>();
			feature.CollectProposals(obs, proposals, new Random(1));
			return proposals;
		}

		[Fact]
		void sell_arms_stacking_factor_adds_not_revenge_replace() {
			Assert.Equal(1.3, WarProsecuteFeature.SellArmsDamageScaleFactor(0, 30));
			Assert.Equal(160.0 / 130.0, WarProsecuteFeature.SellArmsDamageScaleFactor(30, 30));

			double liveDamage = 100.0;
			double scaled = liveDamage * WarProsecuteFeature.SellArmsDamageScaleFactor(30, 30);
			Assert.Equal(liveDamage * (160.0 / 130.0), scaled);

			double revengeReplace = liveDamage * 1.3;
			Assert.NotEqual(revengeReplace, scaled);
		}

		[Fact]
		void win_percent_numeric_overload_damage_scale_raises_attacker_and_lowers_when_defender_scaled() {
			int equal = WarWinChanceEstimator.EstimateAttackerWinPercent(20, 40, 40, 20, 40, 40);
			Assert.Equal(50, equal);

			Assert.Equal(1, WarWinChanceEstimator.EstimateAttackerWinPercent(0, 40, 40, 20, 40, 40));

			int attackerScaled = WarWinChanceEstimator.EstimateAttackerWinPercent(20, 40 * 1.3, 40, 20, 40, 40);
			Assert.True(attackerScaled > equal);

			int defenderScaled = WarWinChanceEstimator.EstimateAttackerWinPercent(20, 40, 40, 20, 40 * 1.3, 40);
			Assert.True(defenderScaled < equal);
		}

		[Fact]
		void prosecute_proposes_sell_arms_when_ev_positive() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 80, damage: 50, durability: 50,
				atWar: true, opponentId: "B", ownWarProgress: 10, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var defender = Country("B", myControl: 0, rivalControl: 60, countryScore: 1200, ownedProvinces: 12,
				recruits: 60, damage: 45, durability: 45,
				atWar: true, opponentId: "A", ownWarProgress: -10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			var proposals = Collect(Feature(), obs);
			BotPlayProposal proposal = Assert.Single(proposals);
			Assert.Equal(WarProsecuteFeature.SellArmsActionId, proposal.ActionId);
			Assert.Equal("A", proposal.CountryId);
			Assert.Equal(WarProsecuteFeature.Id, proposal.FeatureId);
			Assert.False(proposal.ApplyRevengeSoftWeight);
			Assert.True(proposal.EstimatedDeltaOrgScore > 0);
		}

		[Fact]
		void prosecute_skips_non_positive_ev() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			// Attacker already at the 99% ceiling — arms cannot raise win%, so Δ is just -goldCost*weight.
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 10000, damage: 500, durability: 100,
				atWar: true, opponentId: "B", ownWarProgress: 50, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var defender = Country("B", myControl: 0, rivalControl: 60, countryScore: 1200, ownedProvinces: 12,
				recruits: 1, damage: 1, durability: 1,
				atWar: true, opponentId: "A", ownWarProgress: -50);
			int pBefore = WarWinChanceEstimator.EstimateAttackerWinPercent(10000, 500, 100, 1, 1, 1);
			int pAfter = WarWinChanceEstimator.EstimateAttackerWinPercent(10000, 500 * 1.3, 100, 1, 1, 1);
			Assert.Equal(pBefore, pAfter);
			Assert.Equal(99, pBefore);

			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void prosecute_ignores_force_war_cards() {
			var win = Card(WarProsecuteFeature.ForceWarWinActionId, "A", slot: 0, goldCost: 0);
			var loss = Card(WarProsecuteFeature.ForceWarLossActionId, "A", slot: 1, goldCost: 0);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 80, damage: 50, durability: 50,
				atWar: true, opponentId: "B", ownWarProgress: 10, troopsDamageBonusPercent: 0, occupiedOwned: 0, win, loss);
			var defender = Country("B", myControl: 0, rivalControl: 60, countryScore: 1200, ownedProvinces: 12,
				recruits: 60, damage: 45, durability: 45,
				atWar: true, opponentId: "A", ownWarProgress: -10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void prosecute_skips_peacetime_sell_arms() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			var peaceful = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 80, damage: 50, durability: 50,
				atWar: false, opponentId: "", ownWarProgress: 0, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var other = Country("B", myControl: 0, rivalControl: 60, countryScore: 1200, ownedProvinces: 12,
				recruits: 60, damage: 45, durability: 45);
			var obs = new FakeObservation();
			obs.SetCountries(peaceful, other);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void gold_dip_allowed_when_enabled_blocked_when_disabled() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 80, damage: 50, durability: 50,
				atWar: true, opponentId: "B", ownWarProgress: 10, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var defender = Country("B", myControl: 0, rivalControl: 60, countryScore: 1200, ownedProvinces: 12,
				recruits: 60, damage: 45, durability: 45,
				atWar: true, opponentId: "A", ownWarProgress: -10);
			var obs = new FakeObservation { Gold = 200 };
			obs.SetCountries(attacker, defender);

			var blocked = Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 100,
				["allowHighEvGoldDip"] = 0
			}), obs);
			Assert.Empty(blocked);

			var allowed = Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 100,
				["allowHighEvGoldDip"] = 1,
				["highEvDipMinDelta"] = 0
			}), obs);
			Assert.Single(allowed);
		}

		[Fact]
		void destroy_side_effect_changes_prosecute_delta() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			var attackerBase = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 50, damage: 40, durability: 40,
				atWar: true, opponentId: "B", ownWarProgress: 5, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var defenderSparse = Country("B", myControl: 0, rivalControl: 80, countryScore: 2000, ownedProvinces: 2,
				recruits: 45, damage: 38, durability: 40,
				atWar: true, opponentId: "A", ownWarProgress: -5, occupiedOwned: 2);
			var defenderSafe = Country("B", myControl: 0, rivalControl: 80, countryScore: 2000, ownedProvinces: 20,
				recruits: 45, damage: 38, durability: 40,
				atWar: true, opponentId: "A", ownWarProgress: -5, occupiedOwned: 2);

			var obsDestroy = new FakeObservation();
			obsDestroy.SetCountries(attackerBase, defenderSparse);
			var obsSafe = new FakeObservation();
			var attackerSafe = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 50, damage: 40, durability: 40,
				atWar: true, opponentId: "B", ownWarProgress: 5, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			obsSafe.SetCountries(attackerSafe, defenderSafe);

			var destroyProposals = Collect(Feature(), obsDestroy);
			var safeProposals = Collect(Feature(), obsSafe);
			Assert.True(destroyProposals.Count == 1, $"destroy setup expected 1 proposal, got {destroyProposals.Count}");
			Assert.True(safeProposals.Count == 1, $"safe setup expected 1 proposal, got {safeProposals.Count}");
			Assert.NotEqual(destroyProposals[0].EstimatedDeltaOrgScore, safeProposals[0].EstimatedDeltaOrgScore);
		}

		[Fact]
		void soft_bias_on_enemy_side_without_revenge_weight() {
			var sell = Card(WarProsecuteFeature.SellArmsActionId, "A", slot: 0, goldCost: 175);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1500, ownedProvinces: 12,
				recruits: 80, damage: 50, durability: 50,
				atWar: true, opponentId: "EnemyHeavy", ownWarProgress: 10, troopsDamageBonusPercent: 0, occupiedOwned: 0, sell);
			var enemyHeavy = Country("EnemyHeavy", myControl: 10, rivalControl: 50, countryScore: 1200, ownedProvinces: 12,
				recruits: 60, damage: 45, durability: 45,
				atWar: true, opponentId: "A", ownWarProgress: -10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, enemyHeavy);

			BotPlayProposal proposal = Assert.Single(Collect(Feature(new Dictionary<string, double> {
				["targetBiasWeight"] = 0.10
			}), obs));
			Assert.Equal(1.10, proposal.TargetBiasMultiplier);
			Assert.False(proposal.ApplyRevengeSoftWeight);
			Assert.Equal(
				proposal.EstimatedDeltaOrgScore * 1.10,
				BotPlayArbitration.ArbitrationScore(proposal));
		}

		[Fact]
		void shared_peace_fractions_match_declare_helper_defaults() {
			Assert.Equal(0.5, WarPeaceOrgScoreEstimator.DefaultWinnerControlIncreaseFraction);
			Assert.Equal(1.0, WarPeaceOrgScoreEstimator.DefaultLoserControlDecreaseFraction);
			Assert.Equal(0.65, WarPeaceOrgScoreEstimator.DefaultOccupationTransferFraction);
			Assert.Equal(0.001, WarPeaceOrgScoreEstimator.DefaultGoldToOrgScore);
		}

		[Fact]
		void resolve_sell_arms_bonus_from_effect_config() {
			var config = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new CountryResourceModifierEffectParams {
						EffectId = WarProsecuteFeature.SellArmsDamageBonusEffectId,
						EffectType = "CountryResourceModifier",
						ResourceId = "troops_damage_bonus_percent",
						InitialValue = 42.0,
						DecayPerMonth = 2.0
					}
				}
			};
			Assert.Equal(42.0, WarProsecuteFeature.ResolveSellArmsDamageBonusPercent(config));
			Assert.Equal(30.0, WarProsecuteFeature.ResolveSellArmsDamageBonusPercent(null));
		}

		[Fact]
		void war_prosecute_is_registered_in_default_registry() {
			var registry = BotFeatureRegistry.CreateDefault(100);
			Assert.True(registry.IsRegistered(WarProsecuteFeature.Id));
			IBotFeature created = registry.Create(WarProsecuteFeature.Id, new Dictionary<string, double>());
			Assert.Equal(WarProsecuteFeature.Id, created.FeatureId);
			Assert.Throws<InvalidOperationException>(() =>
				registry.Create("notARealFeature", new Dictionary<string, double>()));
		}

		[Fact]
		void prosecute_path_does_not_use_pending_revenge_api_for_sell_arms() {
			double factor = WarProsecuteFeature.SellArmsDamageScaleFactor(0, 30);
			int viaScale = WarWinChanceEstimator.EstimateAttackerWinPercent(20, 40 * factor, 40, 20, 40, 40);
			int viaPending = WarWinChanceEstimator.EstimateAttackerWinPercent(
				20, 40, 40, 20, 40, 40,
				pendingAttackerDamageBonusPercent: 30);
			Assert.Equal(viaScale, viaPending);

			double factorStacked = WarProsecuteFeature.SellArmsDamageScaleFactor(30, 30);
			int viaScaleStacked = WarWinChanceEstimator.EstimateAttackerWinPercent(
				20, 40 * factorStacked, 40, 20, 40, 40);
			int viaPendingStacked = WarWinChanceEstimator.EstimateAttackerWinPercent(
				20, 40, 40, 20, 40, 40,
				pendingAttackerDamageBonusPercent: 30);
			Assert.NotEqual(viaScaleStacked, viaPendingStacked);
		}
	}
}
