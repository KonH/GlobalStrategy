using System.Linq;
using GS.Game.Commands;
using GS.Game.WebClient.Terminal;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class CommandRegistryTests {
		[Fact]
		public void Commands_DiscoversRepresentativeNames() {
			var registry = new CommandRegistry();

			var names = registry.Commands.Select(c => c.Name).ToHashSet();

			Assert.Contains("Pause", names);
			Assert.Contains("SaveGame", names);
			Assert.Contains("DebugImproveOpinion", names);
		}

		[Fact]
		public void Commands_StripCommandSuffix() {
			var registry = new CommandRegistry();

			foreach (var descriptor in registry.Commands) {
				Assert.DoesNotContain("Command", descriptor.Name);
			}
		}

		[Fact]
		public void Find_FieldBasedCommand_ModelsPublicFields() {
			var registry = new CommandRegistry();

			var descriptor = registry.Find("DebugChangeGold");

			Assert.NotNull(descriptor);
			Assert.Equal(typeof(DebugChangeGoldCommand), descriptor!.Type);
			var orgId = descriptor.Parameters.Single(p => p.Name == "OrgId");
			var amount = descriptor.Parameters.Single(p => p.Name == "Amount");
			Assert.Equal(typeof(string), orgId.Type);
			Assert.True(orgId.IsRequired);
			Assert.Equal(typeof(double), amount.Type);
			Assert.True(amount.IsRequired);
		}

		[Fact]
		public void Find_RecordStructCommand_ModelsPositionalProperty() {
			var registry = new CommandRegistry();

			var descriptor = registry.Find("ChangeTimeMultiplier");

			Assert.NotNull(descriptor);
			Assert.Equal(typeof(ChangeTimeMultiplierCommand), descriptor!.Type);
			var index = descriptor.Parameters.Single(p => p.Name == "Index");
			Assert.Equal(typeof(int), index.Type);
			Assert.True(index.IsRequired);
		}

		[Fact]
		public void Find_RecordStructWithDefault_ParameterIsOptional() {
			var registry = new CommandRegistry();

			var descriptor = registry.Find("SaveGame");

			Assert.NotNull(descriptor);
			var isAutoSave = descriptor!.Parameters.Single(p => p.Name == "IsAutoSave");
			Assert.False(isAutoSave.IsRequired);
			Assert.Equal(false, isAutoSave.DefaultValue);
		}

		[Fact]
		public void Find_ZeroArgCommand_HasNoParameters() {
			var registry = new CommandRegistry();

			var descriptor = registry.Find("Pause");

			Assert.NotNull(descriptor);
			Assert.Empty(descriptor!.Parameters);
		}

		[Fact]
		public void Find_UnknownCommand_ReturnsNull() {
			var registry = new CommandRegistry();

			Assert.Null(registry.Find("NotACommand"));
		}
	}
}
