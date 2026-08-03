using System;
using System.Collections.Generic;
using System.IO;
using ECS;
using GS.Configs;
using GS.Configs.IO;
using GS.Core.Map;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.WebClient.Services;
using GS.Main;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Headless GameSession exercised against the real Assets/Configs/*.json via
	// FileConfig<T>, so no HttpClient/browser is involved - same repo-root-walking
	// pattern StringConfigParityTests uses to locate Assets/Configs from the test
	// assembly's output directory.
	public class GameSessionTests {
		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}

		sealed class FileGameConfigSource : IGameConfigSource {
			public IReadOnlyConfigSource<GeoJsonConfig> GeoJson { get; }
			public IReadOnlyConfigSource<MapEntryConfig> MapEntry { get; }
			public IReadOnlyConfigSource<CountryConfig> Country { get; }
			public IReadOnlyConfigSource<GameSettings> GameSettings { get; }
			public IReadOnlyConfigSource<ResourceConfig> Resource { get; }
			public IReadOnlyConfigSource<OrganizationConfig> Organization { get; }
			public IReadOnlyConfigSource<CharacterConfig> Character { get; }
			public IReadOnlyConfigSource<ActionConfig> Action { get; }
			public IReadOnlyConfigSource<EffectConfig> Effect { get; }
			public IReadOnlyConfigSource<ProvinceConfig> Province { get; }
			public List<MapFeature> MapGeometry { get; }

			public FileGameConfigSource() {
				string geoJsonPath = FindRepoRootConfigPath("geojson_world.json");
				GeoJson = new FileConfig<GeoJsonConfig>(geoJsonPath);
				MapEntry = new FileConfig<MapEntryConfig>(FindRepoRootConfigPath("map_entry_config.json"));
				Country = new FileConfig<CountryConfig>(FindRepoRootConfigPath("country_config.json"));
				GameSettings = new FileConfig<GameSettings>(FindRepoRootConfigPath("game_settings.json"));
				Resource = new FileConfig<ResourceConfig>(FindRepoRootConfigPath("resource_config.json"));
				Organization = new FileConfig<OrganizationConfig>(FindRepoRootConfigPath("organizations.json"));
				Character = new FileConfig<CharacterConfig>(FindRepoRootConfigPath("character_config.json"));
				Action = new FileConfig<ActionConfig>(FindRepoRootConfigPath("action_config.json"));
				Effect = new FileConfig<EffectConfig>(FindRepoRootConfigPath("effect_config.json"));
				Province = new FileConfig<ProvinceConfig>(FindRepoRootConfigPath("province_config.json"));
				MapGeometry = GeoJsonParser.Parse(File.ReadAllText(geoJsonPath));
			}
		}

		sealed class InMemoryStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new();

			public void Write(string relativePath, string content) => _files[relativePath] = content;

			public string Read(string relativePath) => _files[relativePath];

			public bool Exists(string relativePath) => _files.ContainsKey(relativePath);

			public void Delete(string relativePath) => _files.Remove(relativePath);

			public IReadOnlyList<string> List(string relativeDir) {
				string prefix = relativeDir.TrimEnd('/') + "/";
				var result = new List<string>();
				foreach (var key in _files.Keys) {
					if (key.StartsWith(prefix, StringComparison.Ordinal)) {
						result.Add(key.Substring(prefix.Length));
					}
				}
				return result;
			}
		}

		sealed class CapturingLogger : IGameLogger {
			public List<string> Errors = new();
			public void LogError(string message) => Errors.Add(message);
			public void LogInfo(string message) { }
			public void LogDebug(string message) { }
		}

		sealed class FakePreferencesStore : IPreferencesStore {
			public Dictionary<string, string> Values = new();
			public string? GetItem(string key) => Values.TryGetValue(key, out var value) ? value : null;
			public void SetItem(string key, string value) => Values[key] = value;
		}

		static string FirstOrganizationId(IGameConfigSource configSource) {
			var organizations = configSource.Organization.Load().Organizations;
			Assert.NotEmpty(organizations);
			return organizations[0].OrganizationId;
		}

		static AutoSaveInterval ReadAutoSaveInterval(GameLogic logic) {
			int[] required = { TypeId<AppSettings>.Value };
			foreach (var archetype in logic.World.GetMatchingArchetypes(required, null)) {
				if (archetype.Count > 0) {
					return archetype.GetColumn<AppSettings>()[0].AutoSaveInterval;
				}
			}
			throw new InvalidOperationException("No AppSettings entity found.");
		}

		static GameSession CreateSession(IGameConfigSource configSource, IPersistentStorage storage, AppPreferences preferences, CapturingLogger? logger = null) {
			return new GameSession(configSource, storage, new WebSnapshotSerializer(), preferences, logger ?? new CapturingLogger());
		}

		[Fact]
		void tick_advances_visual_state_time() {
			var configSource = new FileGameConfigSource();
			var storage = new InMemoryStorage();
			var preferences = new AppPreferences(new FakePreferencesStore());
			var session = CreateSession(configSource, storage, preferences);

			session.StartNew(FirstOrganizationId(configSource), autoStart: false);
			var initialTime = session.Logic!.VisualState.Time.CurrentTime;

			session.Tick(2); // > 1 in-game hour at x1 speed - no background loop running to race with

			Assert.True(session.Logic!.VisualState.Time.CurrentTime > initialTime);

			session.Dispose();
		}

		[Fact]
		void continue_latest_overrides_saved_settings_with_app_preferences() {
			var configSource = new FileGameConfigSource();
			var storage = new InMemoryStorage();
			var preferences = new AppPreferences(new FakePreferencesStore()); // defaults: en / monthly
			string orgId = FirstOrganizationId(configSource);

			var firstSession = CreateSession(configSource, storage, preferences);
			firstSession.StartNew(orgId, autoStart: false);
			firstSession.Tick(1); // first tick: applies en/monthly

			firstSession.Logic!.Commands.Push(new ChangeLocaleCommand("ru"));
			firstSession.Logic!.Commands.Push(new ChangeAutoSaveIntervalCommand("yearly"));
			firstSession.Tick(1); // apply ru/yearly

			firstSession.Logic!.Commands.Push(new SaveGameCommand());
			firstSession.Tick(1); // process save
			Assert.True(firstSession.Logic!.VisualState.SaveResult.Success);
			firstSession.Dispose();

			var secondSession = CreateSession(configSource, storage, preferences);
			secondSession.ContinueLatest(autoStart: false);
			secondSession.Tick(1); // first tick of the new session: re-applies en/monthly

			Assert.Equal("en", secondSession.Logic!.VisualState.Locale.Locale);
			Assert.Equal(AutoSaveInterval.Monthly, ReadAutoSaveInterval(secondSession.Logic!));

			secondSession.Dispose();
		}

		[Fact]
		void save_command_processes_while_game_is_paused() {
			var configSource = new FileGameConfigSource();
			var storage = new InMemoryStorage();
			var preferences = new AppPreferences(new FakePreferencesStore());
			var session = CreateSession(configSource, storage, preferences);

			session.StartNew(FirstOrganizationId(configSource), autoStart: false);
			session.Tick(1); // init

			session.Logic!.Commands.Push(new PauseCommand());
			session.Tick(1);
			Assert.True(session.Logic!.VisualState.Time.IsPaused);

			session.Logic!.Commands.Push(new SaveGameCommand());
			session.Tick(1);

			Assert.True(session.Logic!.VisualState.SaveResult.Success);

			session.Dispose();
		}

		[Fact]
		void continue_latest_with_no_saves_throws() {
			var configSource = new FileGameConfigSource();
			var storage = new InMemoryStorage();
			var preferences = new AppPreferences(new FakePreferencesStore());
			var session = CreateSession(configSource, storage, preferences);

			Assert.Throws<InvalidOperationException>(() => session.ContinueLatest());
		}

		[Fact]
		void tick_before_start_throws() {
			var configSource = new FileGameConfigSource();
			var storage = new InMemoryStorage();
			var preferences = new AppPreferences(new FakePreferencesStore());
			var session = CreateSession(configSource, storage, preferences);

			Assert.Throws<InvalidOperationException>(() => session.Tick(1));
		}
	}
}
