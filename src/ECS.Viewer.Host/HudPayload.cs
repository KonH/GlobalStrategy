using System.Collections.Generic;
using GS.Main;

namespace ECS.Viewer.Host {
	public sealed class HudPayload {
		public string CurrentTime { get; set; } = "";
		public bool IsPaused { get; set; }
		public int MultiplierIndex { get; set; }
		public IReadOnlyList<GameLogEntry> Entries { get; set; } = System.Array.Empty<GameLogEntry>();
		public bool IsCompleted { get; set; }
		public string WinnerOrganizationId { get; set; } = "";
		public GameResult Result { get; set; }
	}
}
