using System;
using System.Collections.Generic;

namespace ECS {
	public sealed partial class World {
		EntityRecord[] _records = new EntityRecord[256];
		int _entityCount;
		readonly Stack<int> _freeList = new Stack<int>();
		readonly Dictionary<ArchetypeSignature, Archetype> _archetypes =
			new Dictionary<ArchetypeSignature, Archetype>();
		readonly Archetype _emptyArchetype;

		public World() {
			_emptyArchetype = GetOrCreateArchetype(new ArchetypeSignature(Array.Empty<int>()));
		}

		// --- Entity lifecycle ---

		public int Create() {
			int index, gen;
			if (_freeList.Count > 0) {
				index = _freeList.Pop();
				gen = (_records[index].Generation + 1) & 0xFFF;
			} else {
				index = _entityCount++;
				gen = 0;
				EnsureRecords(index);
			}
			int id = EntityPacker.Pack(index, gen);
			int row = _emptyArchetype.Add(id);
			_records[index] = new EntityRecord(gen, _emptyArchetype, row);
			return id;
		}

		public void Destroy(int entity) {
			EntityPacker.Unpack(entity, out int index, out int gen);
			ref EntityRecord record = ref _records[index];
			if (record.Generation != gen || record.Archetype == null) return;

			Archetype arch = record.Archetype;
			int movedEntity = arch.Remove(record.Row);
			if (movedEntity >= 0) {
				EntityPacker.Unpack(movedEntity, out int mi, out _);
				_records[mi].Row = record.Row;
			}
			record.Archetype = null;
			record.Row = -1;
			_freeList.Push(index);
		}

		public bool IsAlive(int entity) {
			EntityPacker.Unpack(entity, out int index, out int gen);
			if (index >= _entityCount) return false;
			ref EntityRecord r = ref _records[index];
			return r.Generation == gen && r.Archetype != null;
		}

		// --- Component ops ---

		public void Add<T>(int entity, T component) {
			EntityPacker.Unpack(entity, out int index, out _);
			ref EntityRecord record = ref _records[index];
			int typeId = TypeId<T>.Value;

			if (record.Archetype!.Signature.Contains(typeId)) {
				record.Archetype.GetColumn<T>()[record.Row] = component;
				return;
			}

			Archetype oldArch = record.Archetype;
			ArchetypeSignature newSig = oldArch.Signature.With(typeId);
			Archetype newArch = GetOrCreateArchetype(newSig);

			MigrateColumns(oldArch, newArch);
			newArch.EnsureColumn<T>(typeId);

			int newRow = newArch.Add(entity);
			oldArch.CopyRowTo(record.Row, newArch, newRow);
			newArch.GetColumn<T>()[newRow] = component;

			int movedEntity = oldArch.Remove(record.Row);
			if (movedEntity >= 0) {
				EntityPacker.Unpack(movedEntity, out int mi, out _);
				_records[mi].Row = record.Row;
			}
			record.Archetype = newArch;
			record.Row = newRow;
		}

		public void Remove<T>(int entity) {
			EntityPacker.Unpack(entity, out int index, out _);
			ref EntityRecord record = ref _records[index];
			int typeId = TypeId<T>.Value;

			if (!record.Archetype!.Signature.Contains(typeId))
				throw new InvalidOperationException(
					$"Entity does not have component {typeof(T).Name}");

			Archetype oldArch = record.Archetype;
			ArchetypeSignature newSig = oldArch.Signature.Without(typeId);
			Archetype newArch = GetOrCreateArchetype(newSig);

			foreach (int tid in oldArch.GetColumnTypeIds()) {
				if (tid == typeId) continue;
				if (!newArch.HasColumn(tid))
					newArch.InitColumn(tid, oldArch.GetColumnRaw(tid).GetType().GetElementType()!);
			}

			int newRow = newArch.Add(entity);
			oldArch.CopyRowTo(record.Row, newArch, newRow);

			int movedEntity = oldArch.Remove(record.Row);
			if (movedEntity >= 0) {
				EntityPacker.Unpack(movedEntity, out int mi, out _);
				_records[mi].Row = record.Row;
			}
			record.Archetype = newArch;
			record.Row = newRow;
		}

		public bool Has<T>(int entity) {
			EntityPacker.Unpack(entity, out int index, out _);
			return _records[index].Archetype!.Signature.Contains(TypeId<T>.Value);
		}

		public ref T Get<T>(int entity) {
			EntityPacker.Unpack(entity, out int index, out _);
			ref EntityRecord record = ref _records[index];
			return ref record.Archetype!.GetColumn<T>()[record.Row];
		}

		public bool TryGet<T>(int entity, out T component) {
			EntityPacker.Unpack(entity, out int index, out _);
			ref EntityRecord record = ref _records[index];
			if (record.Archetype == null || !record.Archetype.Signature.Contains(TypeId<T>.Value)) {
				component = default!;
				return false;
			}
			component = record.Archetype.GetColumn<T>()[record.Row];
			return true;
		}

		// --- Query support ---

		public IEnumerable<Archetype> GetMatchingArchetypes(int[] required, int[]? excluded) {
			foreach (Archetype arch in _archetypes.Values) {
				if (!arch.Signature.ContainsAll(required)) continue;
				if (excluded != null && excluded.Length > 0 && arch.Signature.ContainsAny(excluded))
					continue;
				yield return arch;
			}
		}

		// --- Helpers ---

		Archetype GetOrCreateArchetype(ArchetypeSignature sig) {
			if (!_archetypes.TryGetValue(sig, out Archetype? arch)) {
				arch = new Archetype(sig);
				_archetypes[sig] = arch;
			}
			return arch;
		}

		void MigrateColumns(Archetype src, Archetype dst) {
			foreach (int typeId in src.GetColumnTypeIds()) {
				if (!dst.HasColumn(typeId))
					dst.InitColumn(typeId, src.GetColumnRaw(typeId).GetType().GetElementType()!);
			}
		}

		void EnsureRecords(int index) {
			if (index < _records.Length) return;
			int newSize = Math.Max(_records.Length * 2, index + 1);
			var newRecords = new EntityRecord[newSize];
			Array.Copy(_records, newRecords, _records.Length);
			_records = newRecords;
		}
	}
}
