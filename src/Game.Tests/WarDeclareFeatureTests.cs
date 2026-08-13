using System;
using System.Collections.Generic;
using System.Linq;
using GS.Game.Bots;
using Xunit;

namespace GS.Game.Tests {
	public class WarDeclareFeatureTests {
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
				OccupiedOwnedProvinceCount = 0,
				Recruits = recruits,
				Damage = damage,
				Durability = durability,
				Hand = hand,
				RivalCountryIds = Array.Empty<string>()
			};
		}

		static BotCardView Card(
			string actionId,
			string countryId,
			string targetId,
			int slot,
			double goldCost,
			bool playable = true) {
			return new BotCardView {
				ActionId = actionId,
				CountryId = countryId,
				TargetCountryId = targetId,
				SlotIndex = slot,
				GoldCost = goldCost,
				IsPlayable = playable
			};
		}

		static WarDeclareFeature Feature(Dictionary<string, double>? parameters = null) =>
			new WarDeclareFeature(parameters ?? new Dictionary<string, double>(), 100);

		static List<BotPlayProposal> Collect(WarDeclareFeature feature, FakeObservation obs) {
			var proposals = new List<BotPlayProposal>();
			feature.CollectProposals(obs, proposals, new Random(1));
			return proposals;
		}

		[Fact]
		void peace_ev_positive_when_bot_skew_favors_likely_winner() {
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50);
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var orgScores = new[] {
				new BotOrgScoreView { OrgId = BotOrg, OrgScore = 100 },
				new BotOrgScoreView { OrgId = RivalOrg, OrgScore = 200 }
			};

			double delta = WarPeaceOrgScoreEstimator.Estimate(
				BotOrg, attacker, defender, orgScores,
				attackerWinProbability: 0.9,
				goldCost: 150);

			Assert.True(delta > 0, $"expected positive Δ, got {delta}");
		}

		[Fact]
		void peace_ev_non_positive_when_bot_holds_both_sides_symmetrically() {
			var attacker = Country("A", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40);
			var defender = Country("B", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40);
			var orgScores = new[] {
				new BotOrgScoreView { OrgId = BotOrg, OrgScore = 100 },
				new BotOrgScoreView { OrgId = RivalOrg, OrgScore = 80 }
			};

			double delta = WarPeaceOrgScoreEstimator.Estimate(
				BotOrg, attacker, defender, orgScores,
				attackerWinProbability: 0.5,
				goldCost: 150);

			Assert.True(delta <= 0, $"expected non-positive Δ, got {delta}");
		}

		[Fact]
		void declare_skips_non_positive_delta_even_if_playable() {
			var declare = Card(WarDeclareFeature.DeclareWarActionId, "A", "B", slot: 0, goldCost: 150);
			var attacker = Country("A", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40, declare);
			var defender = Country("B", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void declare_picks_highest_delta_not_first_slot() {
			// Strong declare vs weak defender (high Δ) in later slot; weak declare first.
			var lowCard = Card(WarDeclareFeature.DeclareWarActionId, "A", "Weak", slot: 0, goldCost: 150);
			var highCard = Card(WarDeclareFeature.DeclareWarActionId, "A", "Rich", slot: 1, goldCost: 150);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1200, ownedProvinces: 12,
				recruits: 200, damage: 80, durability: 80, lowCard, highCard);
			var weak = Country("Weak", myControl: 0, rivalControl: 20, countryScore: 200, ownedProvinces: 4,
				recruits: 5, damage: 10, durability: 10);
			var rich = Country("Rich", myControl: 0, rivalControl: 60, countryScore: 2000, ownedProvinces: 20,
				recruits: 5, damage: 10, durability: 10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, weak, rich);

			var proposals = Collect(Feature(), obs);
			Assert.True(proposals.Count >= 2);
			BotPlayProposal? best = BotPlayArbitration.SelectBest(proposals);
			Assert.NotNull(best);
			Assert.Equal("Rich", best!.TargetCountryId);
			Assert.Equal(1, best.SlotIndex);
		}

		[Fact]
		void revenge_skipped_when_not_profitable() {
			var revenge = Card(WarDeclareFeature.DeclareRevengeWarActionId, "A", "B", slot: 0, goldCost: 125);
			var attacker = Country("A", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40, revenge);
			var defender = Country("B", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void revenge_proposal_marked_for_b_weight_without_double_multiply() {
			var revenge = Card(WarDeclareFeature.DeclareRevengeWarActionId, "A", "B", slot: 0, goldCost: 125);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, revenge);
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			var proposals = Collect(Feature(), obs);
			BotPlayProposal proposal = Assert.Single(proposals);
			Assert.True(proposal.ApplyRevengeSoftWeight);
			Assert.Equal(WarDeclareFeature.DeclareRevengeWarActionId, proposal.ActionId);

			int winPercent = GS.Game.Systems.WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits, attacker.Damage, attacker.Durability,
				defender.Recruits, defender.Damage, defender.Durability,
				pendingAttackerDamageBonusPercent: 10,
				pendingAttackerDurabilityBonusPercent: 5);
			double raw = WarPeaceOrgScoreEstimator.Estimate(
				BotOrg, attacker, defender, obs.OrgScores,
				attackerWinProbability: winPercent / 100.0,
				goldCost: 125);
			Assert.Equal(raw, proposal.EstimatedDeltaOrgScore);
			Assert.NotEqual(raw * BotPlayArbitration.RevengeSoftWeight, proposal.EstimatedDeltaOrgScore);
		}

		[Fact]
		void soft_bias_metadata_when_enemy_higher_or_no_bot_control() {
			var feature = Feature(new Dictionary<string, double> { ["targetBiasWeight"] = 0.10 });

			var declareEnemy = Card(WarDeclareFeature.DeclareWarActionId, "A", "EnemyHeavy", slot: 0, goldCost: 150);
			var declareOwn = Card(WarDeclareFeature.DeclareWarActionId, "A", "OwnHeavy", slot: 1, goldCost: 150);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1200, ownedProvinces: 12,
				recruits: 200, damage: 80, durability: 80, declareEnemy, declareOwn);
			var enemyHeavy = Country("EnemyHeavy", myControl: 10, rivalControl: 50, countryScore: 900, ownedProvinces: 10,
				recruits: 5, damage: 10, durability: 10);
			var ownHeavy = new BotCountryView {
				CountryId = "OwnHeavy",
				MyControl = 50,
				TotalControl = 60,
				ControlByOrg = new[] {
					Share(BotOrg, 50),
					Share(RivalOrg, 10)
				},
				CountryScore = 900,
				OwnedProvinceCount = 10,
				Recruits = 5,
				Damage = 10,
				Durability = 10,
				Hand = Array.Empty<BotCardView>()
			};
			// Force OwnHeavy to be profitable despite bot-majority control: high attacker skew already.
			// Use zero bot control target for the bias==1+weight case via a third country.
			var noBot = Country("NoBot", myControl: 0, rivalControl: 40, countryScore: 900, ownedProvinces: 10,
				recruits: 5, damage: 10, durability: 10);
			var declareNoBot = Card(WarDeclareFeature.DeclareWarActionId, "A", "NoBot", slot: 2, goldCost: 150);
			attacker.Hand = new[] { declareEnemy, declareOwn, declareNoBot };

			var obs = new FakeObservation();
			obs.SetCountries(attacker, enemyHeavy, ownHeavy, noBot);
			var proposals = Collect(feature, obs);

			BotPlayProposal enemyProp = Assert.Single(proposals.Where(p => p.TargetCountryId == "EnemyHeavy"));
			Assert.Equal(1.10, enemyProp.TargetBiasMultiplier);

			BotPlayProposal noBotProp = Assert.Single(proposals.Where(p => p.TargetCountryId == "NoBot"));
			Assert.Equal(1.10, noBotProp.TargetBiasMultiplier);

			BotPlayProposal? ownProp = proposals.FirstOrDefault(p => p.TargetCountryId == "OwnHeavy");
			if (ownProp != null) {
				Assert.Equal(1.0, ownProp.TargetBiasMultiplier);
			}
		}

		[Fact]
		void min_win_percent_floor() {
			var declare = Card(WarDeclareFeature.DeclareWarActionId, "A", "B", slot: 0, goldCost: 150);
			// Attacker much weaker → win % below default 40.
			var attacker = Country("A", myControl: 80, rivalControl: 0, countryScore: 2000, ownedProvinces: 20,
				recruits: 1, damage: 10, durability: 10, declare);
			var defender = Country("B", myControl: 0, rivalControl: 80, countryScore: 2000, ownedProvinces: 20,
				recruits: 1000, damage: 200, durability: 100);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			int winPercent = GS.Game.Systems.WarWinChanceEstimator.EstimateAttackerWinPercent(
				1, 10, 10, 1000, 200, 100);
			Assert.True(winPercent < 40);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void gold_reserve_blocks_unless_high_ev_dip() {
			var declare = Card(WarDeclareFeature.DeclareWarActionId, "A", "B", slot: 0, goldCost: 150);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, declare);
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation { Gold = 200 };
			obs.SetCountries(attacker, defender);

			var blocked = Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 100,
				["highEvDipMinDelta"] = 1_000_000
			}), obs);
			Assert.Empty(blocked);

			var allowed = Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 100,
				["highEvDipMinDelta"] = 0
			}), obs);
			Assert.Single(allowed);
		}

		[Fact]
		void determinism_same_observation_same_chosen_proposal() {
			var declareA = Card(WarDeclareFeature.DeclareWarActionId, "A", "B", slot: 0, goldCost: 150);
			var declareB = Card(WarDeclareFeature.DeclareWarActionId, "A", "C", slot: 1, goldCost: 150);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1200, ownedProvinces: 12,
				recruits: 200, damage: 80, durability: 80, declareA, declareB);
			var b = Country("B", myControl: 0, rivalControl: 40, countryScore: 900, ownedProvinces: 10,
				recruits: 5, damage: 10, durability: 10);
			var c = Country("C", myControl: 0, rivalControl: 50, countryScore: 1100, ownedProvinces: 12,
				recruits: 5, damage: 10, durability: 10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, b, c);
			var feature = Feature();

			var first = Collect(feature, obs);
			var second = Collect(feature, obs);
			Assert.Equal(first.Count, second.Count);
			for (int i = 0; i < first.Count; i++) {
				Assert.Equal(first[i].ActionId, second[i].ActionId);
				Assert.Equal(first[i].CountryId, second[i].CountryId);
				Assert.Equal(first[i].TargetCountryId, second[i].TargetCountryId);
				Assert.Equal(first[i].SlotIndex, second[i].SlotIndex);
				Assert.Equal(first[i].EstimatedDeltaOrgScore, second[i].EstimatedDeltaOrgScore);
				Assert.Equal(first[i].ApplyRevengeSoftWeight, second[i].ApplyRevengeSoftWeight);
				Assert.Equal(first[i].TargetBiasMultiplier, second[i].TargetBiasMultiplier);
			}

			BotPlayProposal? best1 = BotPlayArbitration.SelectBest(first);
			BotPlayProposal? best2 = BotPlayArbitration.SelectBest(second);
			Assert.NotNull(best1);
			Assert.NotNull(best2);
			Assert.Equal(best1!.TargetCountryId, best2!.TargetCountryId);
			Assert.Equal(best1.SlotIndex, best2.SlotIndex);
		}

		[Fact]
		void war_declare_is_registered_in_default_registry() {
			var registry = BotFeatureRegistry.CreateDefault(100);
			Assert.True(registry.IsRegistered(WarDeclareFeature.Id));
			IBotFeature created = registry.Create(WarDeclareFeature.Id, new Dictionary<string, double>());
			Assert.Equal(WarDeclareFeature.Id, created.FeatureId);
		}
	}
}
