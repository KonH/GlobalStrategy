using System.Collections.Generic;
using GS.Game.Commands;
using GS.Main;

namespace GS.Game.Bots {
	public sealed class BotCommandSink : IBotCommandSink {
		readonly string _orgId;
		readonly IWriteOnlyCommandAccessor _commands;
		readonly IGameLogger? _logger;
		readonly HashSet<(string actionId, string countryId)> _playedThisPhase = new();

		public BotCommandSink(string orgId, IWriteOnlyCommandAccessor commands, IGameLogger? logger) {
			_orgId = orgId;
			_commands = commands;
			_logger = logger;
		}

		public void PlayOrgCard(string actionId) => TryEmit(actionId, "");
		public void PlayCountryCard(string actionId, string countryId) => TryEmit(actionId, countryId);

		public void BeginDecisionPhase() => _playedThisPhase.Clear();

		void TryEmit(string actionId, string countryId) {
			if (!_playedThisPhase.Add((actionId, countryId))) {
				_logger?.LogInfo($"[BotCommandSink] warning: duplicate play ignored org={_orgId} action={actionId} country={countryId}");
				return;
			}
			_commands.Push(new PlayCardActionCommand { ActionId = actionId, OrgId = _orgId, CountryId = countryId });
		}
	}
}
