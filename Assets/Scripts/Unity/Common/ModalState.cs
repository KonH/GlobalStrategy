using System;
using System.Collections.Generic;

namespace GS.Unity.Common {
	public class ModalState {
		readonly List<WeakReference> _owners = new List<WeakReference>();

		// Fires whenever a lock is released, even if other locks remain. Windows that queue their
		// own content (war result, country destroyed) subscribe to this to re-check whether they
		// can open now that another modal window may have just closed — this is what keeps any two
		// modal windows mutually exclusive by default instead of allowed to stack.
		public event Action Unlocked;

		public void Lock(object owner) {
			if (owner == null) {
				return;
			}
			_owners.Add(new WeakReference(owner));
		}

		public void Unlock(object owner) {
			if (owner == null) {
				return;
			}
			_owners.RemoveAll(o => !o.IsAlive || ReferenceEquals(o.Target, owner));
			Unlocked?.Invoke();
		}

		public bool IsLocked() {
			_owners.RemoveAll(o => !o.IsAlive);
			return _owners.Count > 0;
		}
	}
}
