using System;
using System.Collections.Generic;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SaveResultTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		sealed class ThrowingStorage : IPersistentStorage {
			public void Write(string relativePath, string content) => throw new InvalidOperationException("disk full");
			public string Read(string relativePath) => "";
			public bool Exists(string relativePath) => false;
			public void Delete(string relativePath) { }
			public IReadOnlyList<string> List(string relativeDir) => Array.Empty<string>();
		}

		sealed class InMemoryStorage : IPersistentStorage {
			public string? LastWritten;
			public void Write(string relativePath, string content) => LastWritten = content;
			public string Read(string relativePath) => "";
			public bool Exists(string relativePath) => false;
			public void Delete(string relativePath) { }
			public IReadOnlyList<string> List(string relativeDir) => Array.Empty<string>();
		}

		sealed class NoopSerializer : ISnapshotSerializer {
			public string Serialize(WorldSnapshot snapshot) => "{}";
			public WorldSnapshot Deserialize(string json) => new WorldSnapshot();
		}

		static GameLogic BuildLogic(IPersistentStorage storage) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly"
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition>()
			};
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				storage: storage,
				serializer: new NoopSerializer(),
				initialOrganizationId: "Illuminati"
			);
			return new GameLogic(ctx);
		}

		[Fact]
		void manual_save_success_sets_save_result() {
			var logic = BuildLogic(new InMemoryStorage());
			logic.Update(0f);
			logic.Commands.Push(new SaveGameCommand { IsAutoSave = false });

			bool changed = false;
			logic.VisualState.SaveResult.PropertyChanged += (s, e) => changed = true;
			logic.Update(0f);

			Assert.True(changed);
			Assert.True(logic.VisualState.SaveResult.Success);
			Assert.Null(logic.VisualState.SaveResult.ErrorType);
		}

		[Fact]
		void manual_save_failure_sets_error_type_and_does_not_throw() {
			var logic = BuildLogic(new ThrowingStorage());
			logic.Update(0f);
			logic.Commands.Push(new SaveGameCommand { IsAutoSave = false });

			var ex = Record.Exception(() => logic.Update(0f));

			Assert.Null(ex);
			Assert.False(logic.VisualState.SaveResult.Success);
			Assert.Equal(nameof(InvalidOperationException), logic.VisualState.SaveResult.ErrorType);
		}

		[Fact]
		void auto_save_does_not_raise_save_result() {
			var logic = BuildLogic(new InMemoryStorage());
			logic.Update(0f);
			logic.Commands.Push(new SaveGameCommand { IsAutoSave = true });

			bool changed = false;
			logic.VisualState.SaveResult.PropertyChanged += (s, e) => changed = true;
			logic.Update(0f);

			Assert.False(changed);
		}
	}
}
