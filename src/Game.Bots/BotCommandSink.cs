using System.Collections.Generic;
using GS.Game.Commands;
using GS.Main;

namespace GS.Game.Bots {
	public delegate void BotEmissionCallback(string actionId, string countryId);

	public sealed class BotCommandSink : IBotCommandSink {
		readonly string _orgId;
		readonly IWriteOnlyCommandAccessor _commands;
		readonly IGameLogger? _logger;
		readonly BotEmissionCallback? _emissionCallback;
		readonly HashSet<(string actionId, string countryId, int slotIndex, string targetCountryId)> _playedThisPhase = new();

		public BotCommandSink(string orgId, IWriteOnlyCommandAccessor commands, IGameLogger? logger, BotEmissionCallback? emissionCallback = null) {
			_orgId = orgId;
			_commands = commands;
			_logger = logger;
			_emissionCallback = emissionCallback;
		}

		public void PlayOrgCard(string actionId, int slotIndex) => TryEmit(actionId, "", slotIndex, "");
		public void PlayCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId) {
			TryEmit(actionId, countryId, slotIndex, targetCountryId);
		}

		public void BeginDecisionPhase() => _playedThisPhase.Clear();

		void TryEmit(string actionId, string countryId, int slotIndex, string targetCountryId) {
			if (!_playedThisPhase.Add((actionId, countryId, slotIndex, targetCountryId))) {
				_logger?.LogInfo($"[BotCommandSink] warning: duplicate play ignored org={_orgId} action={actionId} country={countryId}");
				return;
			}
			_commands.Push(new PlayCardActionCommand {
				ActionId = actionId,
				OrgId = _orgId,
				CountryId = countryId,
				TargetCountryId = targetCountryId,
				SlotIndex = slotIndex
			});
			string target = string.IsNullOrEmpty(countryId) ? "" : $" -> {countryId}";
			_logger?.LogInfo($"[BotCommandSink] play org={_orgId} action={actionId}{target}");
			_emissionCallback?.Invoke(actionId, countryId);
		}
	}
}
