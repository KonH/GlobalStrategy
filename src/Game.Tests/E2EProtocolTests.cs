using GS.Game.E2E;
using Xunit;

namespace GS.Game.Tests {
	public class E2EProtocolTests {
		[Fact]
		public void mismatched_protocol_version_produces_mismatch_outcome_naming_both_versions() {
			int requestVersion = E2EProtocol.Version + 1;

			Assert.False(ProtocolChecker.IsMatch(requestVersion));
			var report = ProtocolChecker.CreateMismatchReport(requestVersion);

			Assert.Equal(RunOutcomes.ProtocolMismatch, report.Outcome);
			Assert.Contains(requestVersion.ToString(), report.EndingReason);
			Assert.Contains(E2EProtocol.Version.ToString(), report.EndingReason);
		}

		[Fact]
		public void matching_protocol_version_does_not_mismatch() {
			Assert.True(ProtocolChecker.IsMatch(E2EProtocol.Version));
		}
	}
}
