using System;
using System.Collections.Generic;

namespace ECS {
	public sealed class Archetype {
		readonly ArchetypeSignature _signature;
		readonly Dictionary<int, Array> _columns = new Dictionary<int, Array>();
		int[] _entities;
		int _count;
		int _capacity;

		public ArchetypeSignature Signature => _signature;
		public int Count => _count;
		public int[] Entities => _entities;

		public Archetype(ArchetypeSignature signature) {
			_signature = signature;
			_capacity = 4;
			_entities = new int[_capacity];
		}

		public bool HasColumn(int typeId) => _columns.ContainsKey(typeId);

		public void EnsureColumn<T>(int typeId) {
			if (!_columns.ContainsKey(typeId))
				_columns[typeId] = new T[_capacity];
		}

		public void InitColumn(int typeId, Type elementType) {
			if (!_columns.ContainsKey(typeId))
				_columns[typeId] = Array.CreateInstance(elementType, _capacity);
		}

		public T[] GetColumn<T>() => (T[])_columns[TypeId<T>.Value];

		public Array GetColumnRaw(int typeId) => _columns[typeId];

		public Dictionary<int, Array>.KeyCollection GetColumnTypeIds() => _columns.Keys;

		// Adds entity to this archetype; returns the new row index.
		public int Add(int entityId) {
			EnsureCapacity(_count + 1);
			int row = _count++;
			_entities[row] = entityId;
			return row;
		}

		// Removes the row by swap-remove. Returns the entity that was moved into `row`,
		// or -1 if the removed row was the last one (no swap needed).
		public int Remove(int row) {
			int last = _count - 1;
			if (row != last) {
				int moved = _entities[last];
				_entities[row] = moved;
				foreach (var kvp in _columns)
					Array.Copy(kvp.Value, last, kvp.Value, row, 1);
				_count--;
				return moved;
			}
			_count--;
			return -1;
		}

		// Copies component data for shared columns from srcRow in this archetype to dstRow in dst.
		public void CopyRowTo(int srcRow, Archetype dst, int dstRow) {
			foreach (var kvp in _columns) {
				if (dst._columns.TryGetValue(kvp.Key, out var dstArr))
					Array.Copy(kvp.Value, srcRow, dstArr, dstRow, 1);
			}
		}

		void EnsureCapacity(int needed) {
			if (needed <= _capacity) return;
			int newCap = Math.Max(_capacity * 2, needed);
			var newEntities = new int[newCap];
			Array.Copy(_entities, newEntities, _count);
			_entities = newEntities;

			var keys = new List<int>(_columns.Keys);
			foreach (int typeId in keys) {
				Array old = _columns[typeId];
				Array resized = Array.CreateInstance(old.GetType().GetElementType()!, newCap);
				Array.Copy(old, resized, _count);
				_columns[typeId] = resized;
			}
			_capacity = newCap;
		}
	}
}
