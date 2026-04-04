using System;

namespace ECS {
	public sealed class QueryBuilder<C1> {
		readonly World _world;
		int[] _excludes = Array.Empty<int>();

		internal QueryBuilder(World world) { _world = world; }

		public QueryBuilder<C1> Exclude<T>() {
			var ex = new int[_excludes.Length + 1];
			Array.Copy(_excludes, ex, _excludes.Length);
			ex[_excludes.Length] = TypeId<T>.Value;
			_excludes = ex;
			return this;
		}

		public void Run(QueryCallback<C1> callback) {
			int[] required = { TypeId<C1>.Value };
			foreach (Archetype arch in _world.GetMatchingArchetypes(required, _excludes)) {
				C1[] c1 = arch.GetColumn<C1>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++)
					callback(entities[i], ref c1[i]);
			}
		}
	}

	public sealed class QueryBuilder<C1, C2> {
		readonly World _world;
		int[] _excludes = Array.Empty<int>();

		internal QueryBuilder(World world) { _world = world; }

		public QueryBuilder<C1, C2> Exclude<T>() {
			var ex = new int[_excludes.Length + 1];
			Array.Copy(_excludes, ex, _excludes.Length);
			ex[_excludes.Length] = TypeId<T>.Value;
			_excludes = ex;
			return this;
		}

		public void Run(QueryCallback<C1, C2> callback) {
			int[] required = { TypeId<C1>.Value, TypeId<C2>.Value };
			foreach (Archetype arch in _world.GetMatchingArchetypes(required, _excludes)) {
				C1[] c1 = arch.GetColumn<C1>();
				C2[] c2 = arch.GetColumn<C2>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++)
					callback(entities[i], ref c1[i], ref c2[i]);
			}
		}
	}

	public sealed class QueryBuilder<C1, C2, C3> {
		readonly World _world;
		int[] _excludes = Array.Empty<int>();

		internal QueryBuilder(World world) { _world = world; }

		public QueryBuilder<C1, C2, C3> Exclude<T>() {
			var ex = new int[_excludes.Length + 1];
			Array.Copy(_excludes, ex, _excludes.Length);
			ex[_excludes.Length] = TypeId<T>.Value;
			_excludes = ex;
			return this;
		}

		public void Run(QueryCallback<C1, C2, C3> callback) {
			int[] required = { TypeId<C1>.Value, TypeId<C2>.Value, TypeId<C3>.Value };
			foreach (Archetype arch in _world.GetMatchingArchetypes(required, _excludes)) {
				C1[] c1 = arch.GetColumn<C1>();
				C2[] c2 = arch.GetColumn<C2>();
				C3[] c3 = arch.GetColumn<C3>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++)
					callback(entities[i], ref c1[i], ref c2[i], ref c3[i]);
			}
		}
	}

	public sealed class QueryBuilder<C1, C2, C3, C4> {
		readonly World _world;
		int[] _excludes = Array.Empty<int>();

		internal QueryBuilder(World world) { _world = world; }

		public QueryBuilder<C1, C2, C3, C4> Exclude<T>() {
			var ex = new int[_excludes.Length + 1];
			Array.Copy(_excludes, ex, _excludes.Length);
			ex[_excludes.Length] = TypeId<T>.Value;
			_excludes = ex;
			return this;
		}

		public void Run(QueryCallback<C1, C2, C3, C4> callback) {
			int[] required = {
				TypeId<C1>.Value, TypeId<C2>.Value, TypeId<C3>.Value, TypeId<C4>.Value
			};
			foreach (Archetype arch in _world.GetMatchingArchetypes(required, _excludes)) {
				C1[] c1 = arch.GetColumn<C1>();
				C2[] c2 = arch.GetColumn<C2>();
				C3[] c3 = arch.GetColumn<C3>();
				C4[] c4 = arch.GetColumn<C4>();
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++)
					callback(entities[i], ref c1[i], ref c2[i], ref c3[i], ref c4[i]);
			}
		}
	}
}
