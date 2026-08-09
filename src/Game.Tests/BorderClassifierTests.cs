using System.Collections.Generic;
using GS.Game.Common;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class BorderClassifierTests {
		[Fact]
		void political_different_owners_renders_boundary() {
			Assert.True(BorderClassifier.ShouldRenderBoundary(
				"A", "B", MapLens.Political, EmptyOrgs()));
		}

		[Fact]
		void political_same_owner_skips_boundary() {
			Assert.False(BorderClassifier.ShouldRenderBoundary(
				"A", "A", MapLens.Political, EmptyOrgs()));
		}

		[Fact]
		void org_different_top_orgs_renders_boundary() {
			var orgs = new Dictionary<string, string> {
				["A"] = "org1",
				["B"] = "org2"
			};
			Assert.True(BorderClassifier.ShouldRenderBoundary(
				"A", "B", MapLens.Org, orgs));
		}

		[Fact]
		void org_same_top_org_skips_boundary() {
			var orgs = new Dictionary<string, string> {
				["A"] = "org1",
				["B"] = "org1"
			};
			Assert.False(BorderClassifier.ShouldRenderBoundary(
				"A", "B", MapLens.Org, orgs));
		}

		[Fact]
		void org_missing_org_data_falls_back_to_owner_comparison_different() {
			Assert.True(BorderClassifier.ShouldRenderBoundary(
				"A", "B", MapLens.Org, EmptyOrgs()));
		}

		[Fact]
		void org_missing_org_data_falls_back_to_owner_comparison_same() {
			Assert.False(BorderClassifier.ShouldRenderBoundary(
				"A", "A", MapLens.Org, EmptyOrgs()));
		}

		[Fact]
		void org_one_side_missing_org_falls_back_to_owner_comparison() {
			var orgs = new Dictionary<string, string> {
				["A"] = "org1"
			};
			Assert.True(BorderClassifier.ShouldRenderBoundary(
				"A", "B", MapLens.Org, orgs));
			Assert.False(BorderClassifier.ShouldRenderBoundary(
				"A", "A", MapLens.Org, orgs));
		}

		static Dictionary<string, string> EmptyOrgs() => new Dictionary<string, string>();
	}
}
