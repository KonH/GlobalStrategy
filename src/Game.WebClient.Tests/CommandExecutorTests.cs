using System.Collections.Generic;
using System.Linq;
using GS.Game.Commands;
using GS.Game.WebClient.Terminal;
using GS.Main;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Recording fake for IWriteOnlyCommandAccessor - the generic Push<TCommand> call can't be
	// asserted on directly (there is no single non-generic call to intercept), so this fake
	// records the boxed command instance plus its runtime type on every invocation, letting
	// tests assert both "was anything pushed" (Pushed.Count) and "what, exactly" (type + fields).
	public class FakeCommandAccessor : IWriteOnlyCommandAccessor {
		public List<(System.Type Type, object Command)> Pushed { get; } = new();

		public void Push<TCommand>(TCommand cmd) where TCommand : ICommand {
			Pushed.Add((typeof(TCommand), cmd!));
		}
	}

	public class CommandExecutorTests {
		static CommandExecutor CreateExecutor() => new(new CommandRegistry());

		[Fact]
		public void Execute_ZeroArgCommand_PushesCommand() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("Pause", accessor);

			Assert.True(result.Success);
			Assert.Single(accessor.Pushed);
			Assert.Equal(typeof(PauseCommand), accessor.Pushed[0].Type);
		}

		[Fact]
		public void Execute_FieldBasedCommand_CoercesAndPushesFields() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("DebugChangeGold orgId=usa amount=100.5", accessor);

			Assert.True(result.Success);
			Assert.Single(accessor.Pushed);
			var pushed = (DebugChangeGoldCommand)accessor.Pushed[0].Command;
			Assert.Equal("usa", pushed.OrgId);
			Assert.Equal(100.5, pushed.Amount);
		}

		[Fact]
		public void Execute_RecordStructCommand_CoercesPositionalProperty() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("ChangeTimeMultiplier index=2", accessor);

			Assert.True(result.Success);
			var pushed = (ChangeTimeMultiplierCommand)accessor.Pushed[0].Command;
			Assert.Equal(2, pushed.Index);
		}

		[Fact]
		public void Execute_OptionalArgumentOmitted_UsesDefaultValue() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("SaveGame", accessor);

			Assert.True(result.Success);
			var pushed = (SaveGameCommand)accessor.Pushed[0].Command;
			Assert.False(pushed.IsAutoSave);
		}

		[Fact]
		public void Execute_EnumArgument_Coerces() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("ChangeLens lens=Geographic", accessor);

			Assert.True(result.Success);
			var pushed = (ChangeLensCommand)accessor.Pushed[0].Command;
			Assert.Equal(MapLens.Geographic, pushed.Lens);
		}

		[Fact]
		public void Execute_UnknownCommand_FailsAndPushesNothing() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("NotACommand", accessor);

			Assert.False(result.Success);
			Assert.Empty(accessor.Pushed);
		}

		[Fact]
		public void Execute_MalformedArgument_FailsAndPushesNothing() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("DebugChangeGold orgId", accessor);

			Assert.False(result.Success);
			Assert.Empty(accessor.Pushed);
		}

		[Fact]
		public void Execute_MissingRequiredArgument_FailsAndPushesNothing() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("DebugChangeGold orgId=usa", accessor);

			Assert.False(result.Success);
			Assert.Empty(accessor.Pushed);
			Assert.Contains("amount", result.Message, System.StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void Execute_InvalidValue_FailsAndPushesNothing() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("DebugChangeGold orgId=usa amount=notanumber", accessor);

			Assert.False(result.Success);
			Assert.Empty(accessor.Pushed);
		}

		[Fact]
		public void Execute_UnknownArgumentName_FailsAndPushesNothing() {
			var executor = CreateExecutor();
			var accessor = new FakeCommandAccessor();

			var result = executor.Execute("Pause bogus=1", accessor);

			Assert.False(result.Success);
			Assert.Empty(accessor.Pushed);
		}
	}
}
