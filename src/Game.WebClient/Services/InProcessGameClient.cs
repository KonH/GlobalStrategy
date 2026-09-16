using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ECS;
using ECS.Viewer;
using GS.Game.Commands;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Services {
	public sealed class InProcessGameClient : IGameClient {
		readonly GameSession _session;
		readonly CommandExecutor _executor;
		readonly SuggestionEngine _engine;
		readonly ISuggestionSource _source;

		public InProcessGameClient(GameSession session, CommandExecutor executor, SuggestionEngine engine, ISuggestionSource source) {
			_session = session;
			_executor = executor;
			_engine = engine;
			_source = source;
			_session.Ticked += () => Changed?.Invoke();
		}

		public bool ShowPauseMenu => true;
		public bool HasSession => _session.Logic != null;
		public bool IsFrozen => _session.PauseToken.IsPaused;
		public TimeHud? Time => CopyTime();
		public IReadOnlyList<GameLogEntry>? LogEntries => _session.Logic?.VisualState.GameLog.Entries;
		public GameCompletionState? Completion => _session.Logic?.VisualState.GameCompletion;
		public EcsWorldSnapshot? Snapshot { get; private set; }
		public event Action? Changed;

		public Task RefreshHudAsync() {
			Changed?.Invoke();
			return Task.CompletedTask;
		}

		public Task RefreshSnapshotAsync() {
			if (_session.Logic == null) {
				return Task.CompletedTask;
			}
			var observer = new WorldObserver();
			Snapshot = observer.Capture(_session.Logic.World, ECS.Viewer.Host.SnapshotFieldMetadata.Apply);
			Changed?.Invoke();
			return Task.CompletedTask;
		}

		public Task TogglePlayPauseAsync() {
			if (_session.Logic == null || Time == null) {
				return Task.CompletedTask;
			}
			if (Time.IsPaused) {
				_session.Logic.Commands.Push(new UnpauseCommand());
			} else {
				_session.Logic.Commands.Push(new PauseCommand());
			}
			return Task.CompletedTask;
		}

		public Task SetSpeedAsync(int index) {
			_session.Logic?.Commands.Push(new ChangeTimeMultiplierCommand(index));
			return Task.CompletedTask;
		}

		public Task SetFrozenAsync(bool frozen) {
			_session.PauseToken.IsPaused = frozen;
			Changed?.Invoke();
			return Task.CompletedTask;
		}

		public Task<ExecutionResult> ExecuteAsync(string line) {
			if (_session.Logic == null) {
				return Task.FromResult(ExecutionResult.Failure("No running game session."));
			}
			return Task.FromResult(_executor.Execute(line, _session.Logic.Commands));
		}

		public Task<SuggestionResult> SuggestAsync(string input) {
			return Task.FromResult(_engine.GetSuggestions(input));
		}

		public Task PatchFieldAsync(int entityId, string typeName, string fieldName, string rawValue) {
			if (_session.Logic == null) {
				return Task.CompletedTask;
			}
			var observer = new WorldObserver();
			World world = _session.Logic.World;
			_session.Marshal.Enqueue(() => observer.TrySetField(world, entityId, typeName, fieldName, rawValue));
			_session.Marshal.Drain();
			return RefreshSnapshotAsync();
		}

		public IReadOnlyList<string> DomainIds(string? domainIdKind, string? ownerType) {
			return DomainIdCatalog.IdsFor(_source, domainIdKind, ownerType);
		}

		TimeHud? CopyTime() {
			if (_session.Logic == null) {
				return null;
			}
			TimeState t = _session.Logic.VisualState.Time;
			return new TimeHud {
				CurrentTime = t.CurrentTime,
				IsPaused = t.IsPaused,
				MultiplierIndex = t.MultiplierIndex
			};
		}
	}
}
