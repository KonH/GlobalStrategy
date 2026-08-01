using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public sealed class EventNotificationDecision {
		public bool WriteLog { get; }
		public bool Show { get; }
		public bool Pause { get; }
		public WarResultSnapshotState? Snapshot { get; }

		public EventNotificationDecision(bool writeLog, bool show, bool pause, WarResultSnapshotState? snapshot) {
			WriteLog = writeLog;
			Show = show;
			Pause = pause;
			Snapshot = snapshot;
		}
	}

	public static class EventNotificationDispatcher {
		public const string WarResolvedEventType = "war_resolved";

		public static EventNotificationDecision EvaluateWarResolved(
			IReadOnlyWorld world,
			EventNotificationSettings? settings,
			string playerOrgId,
			WarResolvedApplied applied) {
			EventNotificationEntry entry = ResolveWarResolvedEntry(settings);
			string attackerId = applied.AttackerCountryId ?? "";
			string defenderId = applied.DefenderCountryId ?? "";
			// Card resolve historically omitted attacker/defender; fall back to winner/loser so
			// the influence gate still sees participant countries.
			string[] participants = !string.IsNullOrEmpty(attackerId) || !string.IsNullOrEmpty(defenderId)
				? new[] { attackerId, defenderId }
				: new[] { applied.WinnerCountryId ?? "", applied.LoserCountryId ?? "" };
			// Peace already applied control shifts; reverse this event's deltas so influence
			// gates see pre-peace player control (loser cuts must not hide the result window).
			int ControlBeforePeace(string countryId) {
				int control = ControlQuery.GetOrgControlInCountry(world, playerOrgId, countryId);
				if (applied.ControlDeltas == null || string.IsNullOrEmpty(playerOrgId)) {
					return control;
				}
				for (int i = 0; i < applied.ControlDeltas.Count; i++) {
					WarControlDeltaSnapshot delta = applied.ControlDeltas[i];
					if (delta.OrgId == playerOrgId && delta.CountryId == countryId) {
						control -= delta.Delta;
					}
				}
				return control;
			}

			bool writeLog = entry.WriteActionLog
				&& EventNotificationConditionContext.Passes(
					world, playerOrgId, participants, entry.WriteActionLogCondition);
			bool show = entry.ShowWindow
				&& EventNotificationConditionContext.Passes(
					world, playerOrgId, participants, entry.ShowWindowCondition, ControlBeforePeace);
			bool pause = show
				&& entry.Pause
				&& EventNotificationConditionContext.Passes(
					world, playerOrgId, participants, entry.PauseCondition, ControlBeforePeace);

			WarResultSnapshotState? snapshot = null;
			if (show) {
				snapshot = MapSnapshot(applied, pause);
			}

			return new EventNotificationDecision(writeLog, show, pause, snapshot);
		}

		static EventNotificationEntry ResolveWarResolvedEntry(EventNotificationSettings? settings) {
			if (settings?.Events != null) {
				for (int i = 0; i < settings.Events.Count; i++) {
					EventNotificationEntry candidate = settings.Events[i];
					if (candidate != null
						&& string.Equals(candidate.EventType, WarResolvedEventType, StringComparison.Ordinal)) {
						// Present war_resolved rows with omitted conditions would otherwise treat
						// null as always-true and bypass the influence gate; fill v1 defaults.
						return new EventNotificationEntry {
							EventType = candidate.EventType,
							Pause = candidate.Pause,
							ShowWindow = candidate.ShowWindow,
							WriteActionLog = candidate.WriteActionLog,
							PauseCondition = candidate.PauseCondition
								?? EventNotificationSettings.CreateGtControlZero(),
							ShowWindowCondition = candidate.ShowWindowCondition
								?? EventNotificationSettings.CreateGtControlZero(),
							WriteActionLogCondition = candidate.WriteActionLogCondition
						};
					}
				}
			}
			return EventNotificationSettings.CreateWarResolvedDefault();
		}

		static WarResultSnapshotState MapSnapshot(WarResolvedApplied applied, bool shouldPause) {
			var goldRecipients = new List<WarGoldRecipientState>(applied.GoldRecipients?.Count ?? 0);
			if (applied.GoldRecipients != null) {
				foreach (WarGoldRecipientSnapshot recipient in applied.GoldRecipients) {
					goldRecipients.Add(new WarGoldRecipientState(
						recipient.OwnerType, recipient.OwnerId ?? "", recipient.Amount));
				}
			}

			var controlDeltas = new List<WarControlDeltaState>(applied.ControlDeltas?.Count ?? 0);
			if (applied.ControlDeltas != null) {
				foreach (WarControlDeltaSnapshot delta in applied.ControlDeltas) {
					controlDeltas.Add(new WarControlDeltaState(
						delta.CountryId ?? "", delta.OrgId ?? "", delta.Delta, delta.TotalAfter));
				}
			}

			var transferred = new List<WarProvinceTransferState>(applied.TransferredProvinces?.Count ?? 0);
			if (applied.TransferredProvinces != null) {
				foreach (WarProvinceTransferSnapshot transfer in applied.TransferredProvinces) {
					transferred.Add(new WarProvinceTransferState(
						transfer.ProvinceId ?? "",
						transfer.OldOwnerCountryId ?? "",
						transfer.NewOwnerCountryId ?? ""));
				}
			}

			List<WarProgressHistorySnapshot> historySnapshots = applied.History
				?? new List<WarProgressHistorySnapshot>();
			List<WarBattleRowSnapshot> battleSnapshots = applied.Battles
				?? new List<WarBattleRowSnapshot>();

			return new WarResultSnapshotState(
				applied.WarId ?? "",
				applied.AttackerCountryId ?? "",
				applied.DefenderCountryId ?? "",
				applied.WinnerCountryId ?? "",
				applied.LoserCountryId ?? "",
				applied.Progress,
				shouldPause,
				applied.GoldTaken,
				goldRecipients,
				controlDeltas,
				transferred,
				SelectedWarProjector.MapHistory(historySnapshots),
				SelectedWarProjector.MapSideStats(applied.Attacker),
				SelectedWarProjector.MapSideStats(applied.Defender),
				SelectedWarProjector.MapBattles(battleSnapshots));
		}
	}
}
