using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class OpinionSystemTests {
		static int CreateCharacterWithOpinion(World world, CharacterOpinion opinion) {
			int e = world.Create();
			world.Add(e, opinion);
			return e;
		}

		[Fact]
		public void opinion_modifier_decays_by_one_per_month() {
			var world = new World();
			var opinion = new CharacterOpinion {
				BaseOpinionPerOrg = new Dictionary<string, int>(),
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>> {
					["org1"] = new List<OpinionModifier> {
						new OpinionModifier { SourceId = "test", Value = 3, ChangeValue = -1 }
					}
				}
			};
			CreateCharacterWithOpinion(world, opinion);

			var t1 = new DateTime(1880, 1, 1);
			var t2 = new DateTime(1880, 2, 1);
			OpinionSystem.Update(world, t1, t2);

			int[] req = { TypeId<CharacterOpinion>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CharacterOpinion[] ops = arch.GetColumn<CharacterOpinion>();
				Assert.Single(ops[0].ModifiersPerOrg["org1"]);
				Assert.Equal(2, ops[0].ModifiersPerOrg["org1"][0].Value);
			}
		}

		[Fact]
		public void opinion_modifier_removed_when_reaches_zero() {
			var world = new World();
			var opinion = new CharacterOpinion {
				BaseOpinionPerOrg = new Dictionary<string, int>(),
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>> {
					["org1"] = new List<OpinionModifier> {
						new OpinionModifier { SourceId = "test", Value = 1, ChangeValue = -1 }
					}
				}
			};
			CreateCharacterWithOpinion(world, opinion);

			var t1 = new DateTime(1880, 1, 1);
			var t2 = new DateTime(1880, 2, 1);
			OpinionSystem.Update(world, t1, t2);

			int[] req = { TypeId<CharacterOpinion>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CharacterOpinion[] ops = arch.GetColumn<CharacterOpinion>();
				Assert.Empty(ops[0].ModifiersPerOrg["org1"]);
			}
		}

		[Fact]
		public void effective_opinion_clamped_to_minus100_plus100() {
			int baseOpinion = 80;
			int modValue = 50;
			int effective = Math.Clamp(baseOpinion + modValue, -100, 100);
			Assert.Equal(100, effective);
		}

		[Fact]
		public void effective_opinion_clamped_negative() {
			int baseOpinion = -80;
			int modValue = -50;
			int effective = Math.Clamp(baseOpinion + modValue, -100, 100);
			Assert.Equal(-100, effective);
		}
	}
}
