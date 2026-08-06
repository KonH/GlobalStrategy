using System;
using System.Collections.Generic;

namespace GS.Unity.Common {
	public class ModalState {
		readonly List<WeakReference> _owners = new List<WeakReference>();

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
		}

		public bool IsLocked() {
			_owners.RemoveAll(o => !o.IsAlive);
			return _owners.Count > 0;
		}
	}
}
