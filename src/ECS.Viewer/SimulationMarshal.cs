using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ECS.Viewer {
	public class SimulationMarshal {
		readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

		public void Enqueue(Action action) {
			if (action == null) {
				throw new ArgumentNullException(nameof(action));
			}
			_queue.Enqueue(action);
		}

		public Task<T> EnqueueFunc<T>(Func<T> func) {
			if (func == null) {
				throw new ArgumentNullException(nameof(func));
			}
			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			_queue.Enqueue(() => {
				try {
					tcs.SetResult(func());
				} catch (Exception ex) {
					tcs.SetException(ex);
				}
			});
			return tcs.Task;
		}

		public void Drain() {
			while (_queue.TryDequeue(out Action action)) {
				action();
			}
		}
	}
}
