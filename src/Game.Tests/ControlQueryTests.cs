using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ControlQueryTests {
		static int AddControl(World world, string orgId, string countryId, int value, string effectId) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = effectId
			});
			return entity;
		}

		static int GetTotal(World world, string orgId, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] controls = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (controls[i].OrgId == orgId && controls[i].CountryId == countryId) {
						total += controls[i].Value;
					}
				}
			}
			return total;
		}

		[Fact]
		void control_totals_are_scoped_by_org_and_country() {
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 20, "own");
			AddControl(world, "OrgA", "Prussia", 5, "own_second");
			AddControl(world, "OrgB", "Austria", 20, "other_country");
			AddControl(world, "OrgB", "Prussia", 10, "other_org");

			Assert.Equal(25, ControlQuery.GetOrgControlInCountry(world, "OrgA", "Prussia"));
			Assert.Equal(35, ControlQuery.GetTotalControlInCountry(world, "Prussia"));
			Assert.Equal(20, ControlQuery.GetTotalControlInCountry(world, "Austria"));
		}

		[Fact]
		void highest_other_org_uses_summed_control_and_ordinal_tie_breaking() {
			var world = new World();
			Assert.Null(ControlQuery.GetHighestControlOtherOrg(world, "OrgA", "Prussia"));

			AddControl(world, "OrgC", "Prussia", 8, "c1");
			Assert.Equal("OrgC", ControlQuery.GetHighestControlOtherOrg(world, "OrgA", "Prussia"));

			AddControl(world, "OrgB", "Prussia", 5, "b1");
			AddControl(world, "OrgB", "Prussia", 5, "b2");
			Assert.Equal("OrgB", ControlQuery.GetHighestControlOtherOrg(world, "OrgA", "Prussia"));

			AddControl(world, "OrgC", "Prussia", 2, "c2");
			Assert.Equal("OrgB", ControlQuery.GetHighestControlOtherOrg(world, "OrgA", "Prussia"));
		}

		[Fact]
		void reduce_org_control_partially_reduces_a_single_effect() {
			var world = new World();
			int effect = AddControl(world, "OrgB", "Prussia", 20, "a");

			ControlQuery.ReduceOrgControlInCountry(world, "OrgB", "Prussia", 7);

			Assert.Equal(13, world.Get<ControlEffect>(effect).Value);
		}

		[Fact]
		void reduce_org_control_consumes_effects_in_effect_id_order() {
			var world = new World();
			AddControl(world, "OrgB", "Prussia", 5, "a");
			int second = AddControl(world, "OrgB", "Prussia", 10, "b");
			AddControl(world, "OrgB", "Austria", 30, "other_country");
			AddControl(world, "OrgC", "Prussia", 30, "other_org");

			ControlQuery.ReduceOrgControlInCountry(world, "OrgB", "Prussia", 8);

			Assert.Equal(7, world.Get<ControlEffect>(second).Value);
			Assert.Equal(7, GetTotal(world, "OrgB", "Prussia"));
			Assert.Equal(30, GetTotal(world, "OrgB", "Austria"));
			Assert.Equal(30, GetTotal(world, "OrgC", "Prussia"));
		}

		[Fact]
		void reduce_org_control_clamps_at_zero_when_amount_exceeds_total() {
			var world = new World();
			AddControl(world, "OrgB", "Prussia", 5, "a");
			AddControl(world, "OrgB", "Prussia", 10, "b");

			ControlQuery.ReduceOrgControlInCountry(world, "OrgB", "Prussia", 100);

			Assert.Equal(0, GetTotal(world, "OrgB", "Prussia"));
		}

		[Fact]
		void destroy_all_control_in_country_removes_every_org_effect_for_that_country() {
			var world = new World();
			AddControl(world, "OrgA", "Prussia", 20, "a");
			AddControl(world, "OrgB", "Prussia", 10, "b");
			AddControl(world, "OrgA", "Austria", 15, "keep");

			ControlQuery.DestroyAllControlInCountry(world, "Prussia");

			Assert.Equal(0, ControlQuery.GetTotalControlInCountry(world, "Prussia"));
			Assert.Equal(15, ControlQuery.GetTotalControlInCountry(world, "Austria"));
		}
	}
}
