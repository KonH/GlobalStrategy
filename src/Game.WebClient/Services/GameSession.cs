using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GS.Configs;
using GS.Core.Map;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Main;

namespace GS.Game.WebClient.Services {
	// Owns one running GameLogic/BotSession "session" for the lifetime of the app,
	// recreated by StartNew/ContinueLatest. Drives a ~30 Hz wall-clock timer loop
	// (counterpart of GameLoopRunner.Tick in the Unity client) that keeps ticking
	// regardless of the in-game pause flag - GameLogic.Update itself is what decides
	// not to advance simulated time while paused, so SaveGameCommand/terminal commands
	// keep being processed even when paused.
	public class GameSession : IDisposable {
		static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.0 / 30.0);

		readonly IGameConfigSource _configSource;
		readonly IPersistentStorage _storage;
		readonly ISnapshotSerializer _serializer;
		readonly AppPreferences _preferences;
		readonly IGameLogger _logger;

		CancellationTokenSource? _loopCts;
		Task? _loopTask;
		bool _firstTickPending;

		public GameLogic? Logic { get; private set; }
		public BotSession? Session { get; private set; }

		// Raised after every tick (manual or from the timer loop) so a Blazor component
		// can call StateHasChanged().
		public event Action? Ticked;

		public GameSession(
				IGameConfigSource configSource,
				IPersistentStorage storage,
				ISnapshotSerializer serializer,
				AppPreferences preferences,
				IGameLogger logger) {
			_configSource = configSource;
			_storage = storage;
			_serializer = serializer;
			_preferences = preferences;
			_logger = logger;
		}

		// autoStart is a testability seam: production callers always take the default
		// (spawn the real ~30 Hz wall-clock loop immediately). Game.WebClient.Tests
		// passes autoStart:false and drives Tick(...) manually instead - starting the
		// real timer loop alongside manual ticks would race two concurrent
		// GameLogic.Update calls against the same World from different threads.
		public void StartNew(string organizationId, bool autoStart = true) {
			if (string.IsNullOrEmpty(organizationId)) {
				throw new ArgumentException("GameSession.StartNew requires a non-empty organizationId.", nameof(organizationId));
			}

			StopLoop();
			var context = BuildContext(organizationId);
			Logic = new GameLogic(context);
			Session = BotSession.Create(Logic, rngSeed: unchecked((int)DateTime.UtcNow.Ticks), logger: _logger);
			_firstTickPending = true;
			if (autoStart) {
				StartLoop();
			}
		}

		// Throws if this browser has no saves - callers (later UI phases) are expected
		// to gate the CONTINUE button on SaveFileManager.GetLastSave() != null first,
		// so reaching here with no saves is a programming error, not a user-facing case.
		public void ContinueLatest(bool autoStart = true) {
			var saveFileManager = new SaveFileManager(_storage, _serializer);
			var lastSave = saveFileManager.GetLastSave();
			if (lastSave == null) {
				throw new InvalidOperationException("GameSession.ContinueLatest: no save exists in this browser.");
			}

			StopLoop();
			var context = BuildContext(lastSave.OrganizationId);
			Logic = new GameLogic(context);
			Session = BotSession.Create(Logic, rngSeed: unchecked((int)DateTime.UtcNow.Ticks), logger: _logger);
			Logic.LoadState(lastSave.SaveName);
			_firstTickPending = true;
			if (autoStart) {
				StartLoop();
			}
		}

		// Callable directly by tests (manual ticking) and by the timer loop alike - the
		// single place that applies the first-tick settings override and advances the
		// wrapped BotSession.
		public void Tick(double elapsedSeconds) {
			if (Logic == null || Session == null) {
				throw new InvalidOperationException("GameSession.Tick called before StartNew/ContinueLatest.");
			}

			if (_firstTickPending) {
				Logic.Commands.Push(new ChangeLocaleCommand(_preferences.Locale));
				Logic.Commands.Push(new ChangeAutoSaveIntervalCommand(_preferences.AutoSaveInterval));
				_firstTickPending = false;
			}

			Session.Update((float)elapsedSeconds);
			Ticked?.Invoke();
		}

		GameLogicContext BuildContext(string initialOrganizationId) {
			var organizationConfig = _configSource.Organization.Load();
			List<string>? participatingOrgIds = null;
			if (!string.IsNullOrEmpty(initialOrganizationId) && organizationConfig.Organizations.Count >= 2) {
				participatingOrgIds = new List<string> { initialOrganizationId };
				foreach (var orgEntry in organizationConfig.Organizations) {
					if (orgEntry.OrganizationId != initialOrganizationId) {
						participatingOrgIds.Add(orgEntry.OrganizationId);
					}
				}
			}

			return new GameLogicContext(
				_configSource.GeoJson,
				_configSource.MapEntry,
				_configSource.Country,
				_configSource.GameSettings,
				_configSource.Resource,
				_configSource.Organization,
				_storage,
				_serializer,
				_logger,
				initialOrganizationId,
				character: _configSource.Character,
				action: _configSource.Action,
				effect: _configSource.Effect,
				mapGeometry: new InMemoryMapGeometryConfig(_configSource.MapGeometry),
				province: _configSource.Province,
				participatingOrganizationIds: participatingOrgIds);
		}

		void StartLoop() {
			_loopCts = new CancellationTokenSource();
			_loopTask = RunLoopAsync(_loopCts.Token);
		}

		async Task RunLoopAsync(CancellationToken token) {
			using var timer = new PeriodicTimer(TickInterval);
			var lastTick = DateTime.UtcNow;
			try {
				while (await timer.WaitForNextTickAsync(token)) {
					var now = DateTime.UtcNow;
					double elapsedSeconds = (now - lastTick).TotalSeconds;
					lastTick = now;
					try {
						Tick(elapsedSeconds);
					} catch (Exception ex) {
						_logger.LogError($"GameSession: tick failed: {ex}");
					}
				}
			} catch (OperationCanceledException) {
				// Expected when Dispose()/StartNew()/ContinueLatest() cancels the loop.
			}
		}

		void StopLoop() {
			if (_loopCts != null) {
				_loopCts.Cancel();
				_loopCts.Dispose();
				_loopCts = null;
			}
			_loopTask = null;
		}

		public void Dispose() {
			StopLoop();
		}

		// Adapter: ConfigProvider parses geojson_world.json once via GeoJsonParser.Parse
		// at fetch time and hands back a plain List<MapFeature>, but GameLogicContext
		// wants an IReadOnlyConfigSource<List<MapFeature>> - this just returns the
		// already-parsed list, mirroring Game.ConsoleRunner/MapGeometryFileConfig.cs
		// without re-parsing from disk.
		sealed class InMemoryMapGeometryConfig : IReadOnlyConfigSource<List<MapFeature>> {
			readonly List<MapFeature> _geometry;

			public InMemoryMapGeometryConfig(List<MapFeature> geometry) {
				_geometry = geometry;
			}

			public List<MapFeature> Load() => _geometry;
		}
	}
}
