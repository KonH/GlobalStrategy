using System;

namespace ECS {
	public readonly struct ArchetypeSignature : IEquatable<ArchetypeSignature> {
		readonly int[] _ids;
		readonly int _hash;

		static readonly int[] _empty = Array.Empty<int>();

		public int Length => _ids?.Length ?? 0;

		public ArchetypeSignature(int[] sortedIds) {
			_ids = sortedIds;
			int h = 17;
			foreach (int id in sortedIds)
				h = h * 31 + id;
			_hash = h;
		}

		public bool Contains(int typeId) =>
			_ids != null && Array.BinarySearch(_ids, typeId) >= 0;

		public bool ContainsAll(int[] typeIds) {
			foreach (int id in typeIds)
				if (!Contains(id)) return false;
			return true;
		}

		public bool ContainsAny(int[] typeIds) {
			foreach (int id in typeIds)
				if (Contains(id)) return true;
			return false;
		}

		public ArchetypeSignature With(int typeId) {
			if (Contains(typeId)) return this;
			int[] ids = _ids ?? _empty;
			var newIds = new int[ids.Length + 1];
			int i = 0;
			while (i < ids.Length && ids[i] < typeId) {
				newIds[i] = ids[i];
				i++;
			}
			newIds[i] = typeId;
			for (int j = i; j < ids.Length; j++)
				newIds[j + 1] = ids[j];
			return new ArchetypeSignature(newIds);
		}

		public ArchetypeSignature Without(int typeId) {
			if (!Contains(typeId)) return this;
			var newIds = new int[_ids.Length - 1];
			int ni = 0;
			foreach (int id in _ids)
				if (id != typeId) newIds[ni++] = id;
			return new ArchetypeSignature(newIds);
		}

		public bool Equals(ArchetypeSignature other) {
			if (Length != other.Length) return false;
			if (_ids == null) return true;
			for (int i = 0; i < _ids.Length; i++)
				if (_ids[i] != other._ids[i]) return false;
			return true;
		}

		public override bool Equals(object? obj) => obj is ArchetypeSignature s && Equals(s);
		public override int GetHashCode() => _hash;
	}
}
