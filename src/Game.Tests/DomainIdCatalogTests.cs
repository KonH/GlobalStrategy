using System;
using System.Collections.Generic;
using GS.Game.Commands.Text.Suggestions;
using Xunit;

namespace GS.Game.Tests {
	public class DomainIdCatalogTests {
		sealed class FakeSource : ISuggestionSource {
			public IReadOnlyList<string> CountryIds { get; } = new[] { "c1" };
			public IReadOnlyList<string> OrgIds { get; } = new[] { "o1" };
			public IReadOnlyList<string> ProvinceIds { get; } = new[] { "p1" };
			public IReadOnlyList<string> ActionIds { get; } = Array.Empty<string>();
			public IReadOnlyList<string> RoleIds { get; } = Array.Empty<string>();
			public IReadOnlyList<string> LocaleIds { get; } = new[] { "en" };
			public IReadOnlyList<string> ResourceIds { get; } = new[] { "gold", "control_c1" };
			public IReadOnlyList<string> CharacterIds { get; } = new[] { "ch1" };
			public IReadOnlyList<string> WarIds { get; } = new[] { "w1" };
			public IReadOnlyList<string> BattleIds { get; } = Array.Empty<string>();
			public IReadOnlyList<string> EffectIds { get; } = Array.Empty<string>();
			public IReadOnlyList<string> TaskIds { get; } = Array.Empty<string>();
			public IReadOnlyList<string> CollectorIds { get; } = new[] { "col1" };
			public IReadOnlyList<string> CountryOrOrgIds { get; } = new[] { "c1", "o1" };
			public IReadOnlyList<string> OwnerUnionIds { get; } = new[] { "c1", "o1", "ch1" };

			public string CountryDisplayName(string id) => id;
			public string OrgDisplayName(string id) => id;
			public string ProvinceDisplayName(string id) => id;
			public string ActionNameKey(string id) => id;
			public string RoleNameKey(string id) => id;
		}

		[Fact]
		public void ResourceId_UsesCatalogNotSnapshotSubset() {
			var ids = DomainIdCatalog.IdsFor(new FakeSource(), "ResourceId", null);
			Assert.Contains("gold", ids);
			Assert.Contains("control_c1", ids);
		}

		[Fact]
		public void OwnerId_FollowsSiblingKind() {
			var source = new FakeSource();
			Assert.Equal(source.CountryIds, DomainIdCatalog.IdsFor(source, "OwnerId", "Country"));
			Assert.Equal(source.OrgIds, DomainIdCatalog.IdsFor(source, "OwnerId", "Org"));
			Assert.Equal(source.OwnerUnionIds, DomainIdCatalog.IdsFor(source, "OwnerId", null));
		}

		[Fact]
		public void CharacterOwnerId_IsCountryOrOrgUnion() {
			var source = new FakeSource();
			Assert.Equal(source.CountryOrOrgIds, DomainIdCatalog.IdsFor(source, "CharacterOwnerId", null));
		}
	}
}
