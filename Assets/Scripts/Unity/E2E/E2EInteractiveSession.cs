using System.Collections;
using System.IO;
using GS.Game.E2E;
using UnityEngine;

namespace GS.Unity.E2E {
	public class E2EInteractiveSession {
		readonly string _runId;
		readonly System.Func<StepDefinition, IEnumerator> _execute;
		readonly float _idleTimeoutSeconds;
		int _nextIndex;
		float _lastInboxAt;

		public E2EInteractiveSession(string runId, System.Func<StepDefinition, IEnumerator> execute, float idleTimeoutSeconds) {
			_runId = runId;
			_execute = execute;
			_idleTimeoutSeconds = idleTimeoutSeconds;
			_lastInboxAt = Time.realtimeSinceStartup;
			Directory.CreateDirectory(E2EPaths.InboxDir(runId));
			Directory.CreateDirectory(E2EPaths.OutboxDir(runId));
		}

		public IEnumerator Run(System.Action<string> onAbandoned, System.Action onEnded) {
			while (true) {
				string inboxPath = Path.Combine(E2EPaths.InboxDir(_runId), $"step_{_nextIndex}.json");
				if (File.Exists(inboxPath)) {
					_lastInboxAt = Time.realtimeSinceStartup;
					string json = File.ReadAllText(inboxPath);
					var step = E2EJson.Deserialize<StepDefinition>(json);
					if (string.Equals(step.Kind, "end", System.StringComparison.OrdinalIgnoreCase)) {
						WriteOutbox(_nextIndex, succeeded: true, "ended");
						onEnded();
						yield break;
					}

					yield return _execute(step);
					WriteOutbox(_nextIndex, succeeded: true, "executed");
					_nextIndex++;
					continue;
				}

				if (Time.realtimeSinceStartup - _lastInboxAt > _idleTimeoutSeconds) {
					onAbandoned($"Idle for {_idleTimeoutSeconds}s with no inbox step.");
					yield break;
				}

				yield return null;
			}
		}

		public void WriteOutbox(int index, bool succeeded, string message) {
			string path = Path.Combine(E2EPaths.OutboxDir(_runId), $"step_{index}.json");
			File.WriteAllText(path, E2EJson.Serialize(new {
				index,
				success = succeeded,
				message
			}));
		}
	}
}
