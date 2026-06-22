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

		readonly Dictionary<string, EntryDouble> _doubles = new Dictionary<string, EntryDouble>();
		readonly Dictionary<string, EntryInt> _ints = new Dictionary<string, EntryInt>();

		public void AddDouble(string key, AnimatableDouble animatable, double offset) {
			var barrier = animatable.Hold(offset);
			_doubles[key] = new EntryDouble { Animatable = animatable, Barrier = barrier };
		}

		public void AddInt(string key, AnimatableInt animatable, int offset) {
			var barrier = animatable.Hold(offset);
			_ints[key] = new EntryInt { Animatable = animatable, Barrier = barrier };
		}

		public bool Has(string key) => _doubles.ContainsKey(key) || _ints.ContainsKey(key);

		public async UniTask Animate(string key, float duration) {
			if (_doubles.TryGetValue(key, out var de)) {
				de.Barrier.Release(duration);
				await UniTask.WaitUntil(() => de.Barrier.IsComplete);
			} else if (_ints.TryGetValue(key, out var ie)) {
				ie.Barrier.Release(duration);
				await UniTask.WaitUntil(() => ie.Barrier.IsComplete);
			}
		}

		public void CancelAll() {
			foreach (var e in _doubles.Values) { e.Animatable.Cancel(e.Barrier); }
			foreach (var e in _ints.Values) { e.Animatable.Cancel(e.Barrier); }
			_doubles.Clear();
			_ints.Clear();
		}
	}
}
