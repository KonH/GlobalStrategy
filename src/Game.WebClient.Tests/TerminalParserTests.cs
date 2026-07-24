using GS.Game.WebClient.Terminal;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class TerminalParserTests {
		readonly TerminalParser _parser = new();

		[Fact]
		public void Parse_EmptyInput_Fails() {
			var result = _parser.Parse("");

			Assert.False(result.Success);
			Assert.NotNull(result.Error);
		}

		[Fact]
		public void Parse_WhitespaceInput_Fails() {
			var result = _parser.Parse("   ");

			Assert.False(result.Success);
		}

		[Fact]
		public void Parse_CommandNameOnly_SucceedsWithNoArguments() {
			var result = _parser.Parse("Pause");

			Assert.True(result.Success);
			Assert.Equal("Pause", result.CommandName);
			Assert.Empty(result.Arguments);
		}

		[Fact]
		public void Parse_CommandWithArguments_SucceedsWithParsedPairs() {
			var result = _parser.Parse("DebugChangeGold orgId=usa amount=100.5");

			Assert.True(result.Success);
			Assert.Equal("DebugChangeGold", result.CommandName);
			Assert.Equal("usa", result.Arguments["orgId"]);
			Assert.Equal("100.5", result.Arguments["amount"]);
		}

		[Fact]
		public void Parse_ExtraWhitespaceBetweenTokens_IsIgnored() {
			var result = _parser.Parse("  DebugChangeGold   orgId=usa   amount=1  ");

			Assert.True(result.Success);
			Assert.Equal("DebugChangeGold", result.CommandName);
			Assert.Equal(2, result.Arguments.Count);
		}

		[Fact]
		public void Parse_ArgumentWithoutEquals_Fails() {
			var result = _parser.Parse("DebugChangeGold orgId");

			Assert.False(result.Success);
			Assert.NotNull(result.Error);
		}

		[Fact]
		public void Parse_ArgumentWithEmptyKey_Fails() {
			var result = _parser.Parse("DebugChangeGold =usa");

			Assert.False(result.Success);
		}

		[Fact]
		public void Parse_CommandNameWithEquals_Fails() {
			var result = _parser.Parse("Foo=Bar");

			Assert.False(result.Success);
		}
	}
}
