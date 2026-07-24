using GS.Game.Commands;
using GS.Game.WebClient.Terminal;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class ValueCoercionTests {
		[Fact]
		public void TryCoerce_String_PassesThrough() {
			bool ok = ValueCoercion.TryCoerce("hello", typeof(string), out var value, out var error);

			Assert.True(ok);
			Assert.Equal("hello", value);
			Assert.Null(error);
		}

		[Theory]
		[InlineData("42", 42)]
		[InlineData("-7", -7)]
		public void TryCoerce_Int_Valid(string raw, int expected) {
			bool ok = ValueCoercion.TryCoerce(raw, typeof(int), out var value, out var error);

			Assert.True(ok);
			Assert.Equal(expected, value);
			Assert.Null(error);
		}

		[Fact]
		public void TryCoerce_Int_Invalid_FailsWithFriendlyMessage() {
			bool ok = ValueCoercion.TryCoerce("abc", typeof(int), out var value, out var error);

			Assert.False(ok);
			Assert.Null(value);
			Assert.NotNull(error);
		}

		[Fact]
		public void TryCoerce_Double_Valid() {
			bool ok = ValueCoercion.TryCoerce("100.5", typeof(double), out var value, out var error);

			Assert.True(ok);
			Assert.Equal(100.5, value);
		}

		[Fact]
		public void TryCoerce_Double_Invalid_Fails() {
			bool ok = ValueCoercion.TryCoerce("notanumber", typeof(double), out var value, out var error);

			Assert.False(ok);
			Assert.NotNull(error);
		}

		[Theory]
		[InlineData("true", true)]
		[InlineData("True", true)]
		[InlineData("1", true)]
		[InlineData("false", false)]
		[InlineData("False", false)]
		[InlineData("0", false)]
		public void TryCoerce_Bool_Valid(string raw, bool expected) {
			bool ok = ValueCoercion.TryCoerce(raw, typeof(bool), out var value, out var error);

			Assert.True(ok);
			Assert.Equal(expected, value);
		}

		[Fact]
		public void TryCoerce_Bool_Invalid_Fails() {
			bool ok = ValueCoercion.TryCoerce("maybe", typeof(bool), out var value, out var error);

			Assert.False(ok);
			Assert.NotNull(error);
		}

		[Fact]
		public void TryCoerce_Enum_Valid() {
			bool ok = ValueCoercion.TryCoerce("Political", typeof(MapLens), out var value, out var error);

			Assert.True(ok);
			Assert.Equal(MapLens.Political, value);
		}

		[Fact]
		public void TryCoerce_Enum_CaseInsensitive() {
			bool ok = ValueCoercion.TryCoerce("geographic", typeof(MapLens), out var value, out var error);

			Assert.True(ok);
			Assert.Equal(MapLens.Geographic, value);
		}

		[Fact]
		public void TryCoerce_Enum_Invalid_Fails() {
			bool ok = ValueCoercion.TryCoerce("NotALens", typeof(MapLens), out var value, out var error);

			Assert.False(ok);
			Assert.NotNull(error);
		}
	}
}
