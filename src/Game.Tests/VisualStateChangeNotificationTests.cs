using System;
using System.Collections.Generic;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateChangeNotificationTests {
		[Fact]
		public void time_state_no_op_set_does_not_fire_property_changed() {
			var state = new TimeState();
			var time = new DateTime(1880, 1, 1);
			state.Set(time, false, 0);
			int fireCount = 0;
			state.PropertyChanged += (_, __) => fireCount++;
			state.Set(time, false, 0);
			Assert.Equal(0, fireCount);
			state.Set(time, true, 0);
			Assert.Equal(1, fireCount);
		}

		[Fact]
		public void country_control_state_no_op_set_does_not_fire_property_changed_but_used_control_still_updates() {
			var state = new CountryControlState();
			var entries = new List<OrgControlEntry> {
				new OrgControlEntry("gs", "Great Society", 10, 5, 2, 3.5)
			};
			state.Set(1, entries);
			int fireCount = 0;
			int usedControlFireCount = 0;
			state.PropertyChanged += (_, __) => fireCount++;
			state.UsedControl.PropertyChanged += (_, __) => usedControlFireCount++;

			var equalEntries = new List<OrgControlEntry> {
				new OrgControlEntry("gs", "Great Society", 10, 5, 2, 3.5)
			};
			state.Set(1, equalEntries);
			Assert.Equal(0, fireCount);
			Assert.Equal(1, state.UsedControl.Actual);

			state.Set(2, equalEntries);
			Assert.Equal(1, fireCount);
		}

		[Fact]
		public void country_score_state_no_op_set_does_not_fire_property_changed() {
			var state = new CountryScoreState();
			var scores = new Dictionary<string, double> {
				["Great_Britain"] = 10,
				["France"] = 20
			};
			state.Set(scores);
			int fireCount = 0;
			state.PropertyChanged += (_, __) => fireCount++;

			var reordered = new Dictionary<string, double> {
				["France"] = 20,
				["Great_Britain"] = 10
			};
			state.Set(reordered);
			Assert.Equal(0, fireCount);

			var changed = new Dictionary<string, double> {
				["France"] = 21,
				["Great_Britain"] = 10
			};
			state.Set(changed);
			Assert.Equal(1, fireCount);
		}

		[Fact]
		public void province_ownership_state_ignores_recent_fields_in_equality_check() {
			var state = new ProvinceOwnershipState();
			var owners = new Dictionary<string, string> {
				["province_1"] = "Great_Britain"
			};
			state.Set(owners, "province_1", "", "Great_Britain");
			int fireCount = 0;
			state.PropertyChanged += (_, __) => fireCount++;

			var sameOwners = new Dictionary<string, string> {
				["province_1"] = "Great_Britain"
			};
			state.Set(sameOwners, "province_2", "France", "Germany");
			Assert.Equal(0, fireCount);
			Assert.Equal("province_2", state.RecentProvinceId);
			Assert.Equal("France", state.RecentOldOwnerId);
			Assert.Equal("Germany", state.RecentNewOwnerId);

			var changedOwners = new Dictionary<string, string> {
				["province_1"] = "France"
			};
			state.Set(changedOwners, "province_1", "Great_Britain", "France");
			Assert.Equal(1, fireCount);
		}

		[Fact]
		public void discovered_countries_state_no_op_and_recently_discovered_ignored_in_equality_check() {
			var state = new DiscoveredCountriesState();
			var ids = new HashSet<string> { "Great_Britain", "France" };
			state.Set(ids, "France");
			int fireCount = 0;
			state.PropertyChanged += (_, __) => fireCount++;

			var reordered = new HashSet<string> { "France", "Great_Britain" };
			state.Set(reordered, "Great_Britain");
			Assert.Equal(0, fireCount);
			Assert.Equal("Great_Britain", state.RecentlyDiscovered);

			var added = new HashSet<string> { "France", "Great_Britain", "Germany" };
			state.Set(added, "Germany");
			Assert.Equal(1, fireCount);

			var removed = new HashSet<string> { "France", "Great_Britain" };
			state.Set(removed, "Germany");
			Assert.Equal(2, fireCount);
		}
	}
}
