using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ECS.Viewer;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Services {
	public sealed class TimeHud {
		public DateTime CurrentTime { get; set; }
		public bool IsPaused { get; set; }
		public int MultiplierIndex { get; set; }
	}

	public interface IGameClient {
		bool ShowPauseMenu { get; }
		bool HasSession { get; }
		bool IsFrozen { get; }
		TimeHud? Time { get; }
		IReadOnlyList<GameLogEntry>? LogEntries { get; }
		GameCompletionState? Completion { get; }
		EcsWorldSnapshot? Snapshot { get; }
		event Action? Changed;
		Task RefreshHudAsync();
		Task RefreshSnapshotAsync();
		Task TogglePlayPauseAsync();
		Task SetSpeedAsync(int index);
		Task SetFrozenAsync(bool frozen);
		Task<ExecutionResult> ExecuteAsync(string line);
		Task<SuggestionResult> SuggestAsync(string input);
		Task PatchFieldAsync(int entityId, string typeName, string fieldName, string rawValue);
		IReadOnlyList<string> DomainIds(string? domainIdKind, string? ownerType);
	}
}
