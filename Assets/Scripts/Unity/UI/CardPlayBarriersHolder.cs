using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GS.Main;

namespace GS.Unity.UI {
	public class CardPlayBarriersHolder {
		class EntryDouble {
			public AnimatableDouble Animatable;
			public AnimationBarrierDouble Barrier;
		}

		class EntryInt {
			public AnimatableInt Animatable;
			public AnimationBarrierInt Barrier;
		}

		readonly Dictionary<string, List<EntryDouble>> _doubles = new Dictionary<string, List<EntryDouble>>();
		readonly Dictionary<string, List<EntryInt>> _ints = new Dictionary<string, List<EntryInt>>();

		public void AddDouble(string key, AnimatableDouble animatable, double offset) {
			var barrier = animatable.Hold(offset);
			if (!_doubles.TryGetValue(key, out var entries)) {
				entries = new List<EntryDouble>();
				_doubles[key] = entries;
			}
			entries.Add(new EntryDouble { Animatable = animatable, Barrier = barrier });
		}

		public void AddInt(string key, AnimatableInt animatable, int offset) {
			var barrier = animatable.Hold(offset);
			if (!_ints.TryGetValue(key, out var entries)) {
				entries = new List<EntryInt>();
				_ints[key] = entries;
			}
			entries.Add(new EntryInt { Animatable = animatable, Barrier = barrier });
		}

		public bool Has(string key) =>
			(_doubles.TryGetValue(key, out var doubles) && doubles.Count > 0)
			|| (_ints.TryGetValue(key, out var ints) && ints.Count > 0);

		public async UniTask Animate(string key, float duration) {
			var tasks = new List<UniTask>();
			if (_doubles.TryGetValue(key, out var doubles)) {
				foreach (var entry in doubles) {
					entry.Barrier.Release(duration);
					tasks.Add(UniTask.WaitUntil(() => entry.Barrier.IsComplete));
				}
			}
			if (_ints.TryGetValue(key, out var ints)) {
				foreach (var entry in ints) {
					entry.Barrier.Release(duration);
					tasks.Add(UniTask.WaitUntil(() => entry.Barrier.IsComplete));
				}
			}
			if (tasks.Count > 0) {
				await UniTask.WhenAll(tasks);
			}
		}

		public void CancelAll() {
			foreach (var entries in _doubles.Values) {
				foreach (var entry in entries) {
					entry.Animatable.Cancel(entry.Barrier);
				}
			}
			foreach (var entries in _ints.Values) {
				foreach (var entry in entries) {
					entry.Animatable.Cancel(entry.Barrier);
				}
			}
			_doubles.Clear();
			_ints.Clear();
		}
	}
}
