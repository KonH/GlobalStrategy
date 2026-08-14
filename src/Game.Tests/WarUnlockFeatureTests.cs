using System;
using System.Collections.Generic;
using GS.Game.Bots;
using Xunit;

namespace GS.Game.Tests {
	public class WarUnlockFeatureTests {
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

		static BotCountryCharacterView Char(string roleId, double opinion) =>
			new BotCountryCharacterView {
				CharacterId = roleId + "_char",
				RoleId = roleId,
				OpinionOfMyOrg = opinion
			};

		static BotCountryView Country(
			string id,
			int myControl,
			int rivalControl,
			double countryScore,
			int ownedProvinces,
			double recruits,
			double damage,
			double durability,
			BotCardView[]? hand = null,
			BotCountryCharacterView[]? characters = null,
			string[]? rivals = null,
			bool isAtWar = false,
			string warOpponent = "",
			double ownWarProgress = 0) {
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
				Hand = hand ?? Array.Empty<BotCardView>(),
				Characters = characters ?? Array.Empty<BotCountryCharacterView>(),
				RivalCountryIds = rivals ?? Array.Empty<string>(),
				IsAtWar = isAtWar,
				WarOpponentCountryId = warOpponent,
				OwnWarProgress = ownWarProgress,
				TroopsDamageBonusPercent = 0
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

		static WarUnlockFeature Feature(Dictionary<string, double>? parameters = null) =>
			new WarUnlockFeature(parameters ?? new Dictionary<string, double>(), 100);

		static List<BotPlayProposal> Collect(WarUnlockFeature feature, FakeObservation obs) {
			var proposals = new List<BotPlayProposal>();
			feature.CollectProposals(obs, proposals, new Random(1));
			return proposals;
		}

		static double RawDeclareDelta(FakeObservation obs, BotCountryView attacker, BotCountryView defender) {
			int winPercent = GS.Game.Systems.WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits, attacker.Damage, attacker.Durability,
				defender.Recruits, defender.Damage, defender.Durability);
			return WarPeaceOrgScoreEstimator.Estimate(
				BotOrg, attacker, defender, obs.OrgScores,
				attackerWinProbability: winPercent / 100.0,
				goldCost: WarUnlockFeature.DeclareWarGoldCost);
		}

		static BotCountryCharacterView[] DeclareReadyOpinions() => new[] {
			Char(WarUnlockFeature.RoleDiplomacy, 40),
			Char(WarUnlockFeature.RoleMilitary, 60),
			Char(WarUnlockFeature.RoleRuler, 60)
		};

		static BotCountryCharacterView[] LowDiplomacyOpinions() => new[] {
			Char(WarUnlockFeature.RoleDiplomacy, 10),
			Char(WarUnlockFeature.RoleMilitary, 60),
			Char(WarUnlockFeature.RoleRuler, 60)
		};

		static BotCountryCharacterView[] LowDeclareOpinion() => new[] {
			Char(WarUnlockFeature.RoleDiplomacy, 40),
			Char(WarUnlockFeature.RoleMilitary, 20),
			Char(WarUnlockFeature.RoleRuler, 20)
		};

		static BotCountryCharacterView[] LowMilWartime() => new[] {
			Char(WarUnlockFeature.RoleDiplomacy, 40),
			Char(WarUnlockFeature.RoleMilitary, 40),
			Char(WarUnlockFeature.RoleRuler, 60)
		};

		[Fact]
		void war_unlock_proposes_make_rival_when_path_delta_positive() {
			var makeRival = Card(WarUnlockFeature.MakeRivalActionId, "A", "B", slot: 0, goldCost: 75);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { makeRival },
				characters: DeclareReadyOpinions());
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			double raw = RawDeclareDelta(obs, attacker, defender);
			Assert.True(raw > 0);

			BotPlayProposal proposal = Assert.Single(Collect(Feature(), obs));
			Assert.Equal(WarUnlockFeature.MakeRivalActionId, proposal.ActionId);
			Assert.Equal("A", proposal.CountryId);
			Assert.Equal("B", proposal.TargetCountryId);
			Assert.Equal(0, proposal.SlotIndex);
			// k=1 (declare opinion already met) → score = unlockPathDiscount × Δ
			Assert.Equal(0.4 * raw, proposal.EstimatedDeltaOrgScore, 6);
		}

		[Fact]
		void war_unlock_skips_make_rival_when_path_delta_non_positive() {
			var makeRival = Card(WarUnlockFeature.MakeRivalActionId, "A", "B", slot: 0, goldCost: 75);
			var attacker = Country("A", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40, hand: new[] { makeRival },
				characters: DeclareReadyOpinions());
			var defender = Country("B", myControl: 40, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 50, damage: 40, durability: 40);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.True(RawDeclareDelta(obs, attacker, defender) <= 0);
			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void war_unlock_proposes_diplomacy_opinion_when_below_make_rival_gate() {
			var diplo = Card(WarUnlockFeature.ImproveDiplomacyActionId, "A", "", slot: 0, goldCost: 30);
			var attackerLow = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { diplo },
				characters: LowDiplomacyOpinions());
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation();
			obs.SetCountries(attackerLow, defender);

			BotPlayProposal proposal = Assert.Single(Collect(Feature(), obs));
			Assert.Equal(WarUnlockFeature.ImproveDiplomacyActionId, proposal.ActionId);

			// Diplomacy already ≥ 30 → skip for that reason.
			var diploReady = Card(WarUnlockFeature.ImproveDiplomacyActionId, "A", "", slot: 0, goldCost: 30);
			var attackerReady = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { diploReady },
				characters: DeclareReadyOpinions());
			obs.SetCountries(attackerReady, defender);
			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void war_unlock_proposes_mil_or_ruler_opinion_to_clear_declare_gate() {
			var mil = Card(WarUnlockFeature.ImproveMilitaryActionId, "A", "", slot: 0, goldCost: 30);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { mil },
				characters: LowDeclareOpinion(),
				rivals: new[] { "B" });
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			BotPlayProposal proposal = Assert.Single(Collect(Feature(), obs));
			Assert.Equal(WarUnlockFeature.ImproveMilitaryActionId, proposal.ActionId);

			// Already ≥ 50 ⇒ skip opinion grind.
			var milReady = Card(WarUnlockFeature.ImproveMilitaryActionId, "A", "", slot: 0, goldCost: 30);
			var attackerReady = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { milReady },
				characters: DeclareReadyOpinions(),
				rivals: new[] { "B" });
			obs.SetCountries(attackerReady, defender);
			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void war_unlock_proposes_mil_opinion_for_sell_arms_gate_when_at_war() {
			var mil = Card(WarUnlockFeature.ImproveMilitaryActionId, "A", "", slot: 0, goldCost: 30);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { mil },
				characters: LowMilWartime(),
				isAtWar: true, warOpponent: "B", ownWarProgress: 1);
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20,
				isAtWar: true, warOpponent: "A", ownWarProgress: -1);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			BotPlayProposal proposal = Assert.Single(Collect(Feature(), obs));
			Assert.Equal(WarUnlockFeature.ImproveMilitaryActionId, proposal.ActionId);
			Assert.Equal("A", proposal.CountryId);
		}

		[Fact]
		void war_unlock_respects_min_gold_reserve() {
			var makeRival = Card(WarUnlockFeature.MakeRivalActionId, "A", "B", slot: 0, goldCost: 75);
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { makeRival },
				characters: DeclareReadyOpinions());
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation { Gold = 100 };
			obs.SetCountries(attacker, defender);

			// 100 - 75 = 25 < minGoldReserve 50 ⇒ no proposal (no dip for unlocks).
			Assert.Empty(Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 50
			}), obs));

			obs.Gold = 200;
			Assert.Single(Collect(Feature(new Dictionary<string, double> {
				["minGoldReserve"] = 50
			}), obs));
		}

		[Fact]
		void war_unlock_picks_best_candidate_deterministically() {
			// Two make_rival cards: higher path Δ wins; equal scores use SlotIndex / ActionId / Target ordinal.
			var lowTarget = Card(WarUnlockFeature.MakeRivalActionId, "A", "Weak", slot: 0, goldCost: 75);
			var highTarget = Card(WarUnlockFeature.MakeRivalActionId, "A", "Rich", slot: 1, goldCost: 75);
			var attacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1200, ownedProvinces: 12,
				recruits: 200, damage: 80, durability: 80, hand: new[] { lowTarget, highTarget },
				characters: DeclareReadyOpinions());
			var weak = Country("Weak", myControl: 0, rivalControl: 20, countryScore: 200, ownedProvinces: 4,
				recruits: 5, damage: 10, durability: 10);
			var rich = Country("Rich", myControl: 0, rivalControl: 60, countryScore: 2000, ownedProvinces: 20,
				recruits: 5, damage: 10, durability: 10);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, weak, rich);

			BotPlayProposal best = Assert.Single(Collect(Feature(), obs));
			Assert.Equal("Rich", best.TargetCountryId);
			Assert.Equal(1, best.SlotIndex);

			// Equal scores → lower SlotIndex wins.
			var a = Card(WarUnlockFeature.MakeRivalActionId, "A", "TwinB", slot: 2, goldCost: 75);
			var b = Card(WarUnlockFeature.MakeRivalActionId, "A", "TwinA", slot: 1, goldCost: 75);
			var twinAttacker = Country("A", myControl: 70, rivalControl: 0, countryScore: 1200, ownedProvinces: 12,
				recruits: 200, damage: 80, durability: 80, hand: new[] { a, b },
				characters: DeclareReadyOpinions());
			var twinA = Country("TwinA", myControl: 0, rivalControl: 50, countryScore: 1000, ownedProvinces: 10,
				recruits: 5, damage: 10, durability: 10);
			var twinB = Country("TwinB", myControl: 0, rivalControl: 50, countryScore: 1000, ownedProvinces: 10,
				recruits: 5, damage: 10, durability: 10);
			obs.SetCountries(twinAttacker, twinA, twinB);
			BotPlayProposal tie = Assert.Single(Collect(Feature(), obs));
			Assert.Equal(1, tie.SlotIndex);
			Assert.Equal("TwinA", tie.TargetCountryId);
		}

		[Fact]
		void war_unlock_ignores_non_owned_action_ids() {
			var declare = Card("declare_war", "A", "B", slot: 0, goldCost: 150);
			var sellArms = Card("sell_arms", "A", "", slot: 1, goldCost: 175);
			var control = Card("improve_control", "A", "", slot: 2, goldCost: 50, playable: true);
			control.RaisesControl = true;
			var attacker = Country("A", myControl: 60, rivalControl: 10, countryScore: 1000, ownedProvinces: 10,
				recruits: 100, damage: 50, durability: 50, hand: new[] { declare, sellArms, control },
				characters: DeclareReadyOpinions(),
				rivals: new[] { "B" });
			var defender = Country("B", myControl: 0, rivalControl: 50, countryScore: 800, ownedProvinces: 10,
				recruits: 10, damage: 20, durability: 20);
			var obs = new FakeObservation();
			obs.SetCountries(attacker, defender);

			Assert.Empty(Collect(Feature(), obs));
		}

		[Fact]
		void war_unlock_is_registered_in_default_registry() {
			var registry = BotFeatureRegistry.CreateDefault(100);
			Assert.True(registry.IsRegistered(WarUnlockFeature.Id));
			IBotFeature created = registry.Create(WarUnlockFeature.Id, new Dictionary<string, double>());
			Assert.Equal(WarUnlockFeature.Id, created.FeatureId);
		}
	}
}
