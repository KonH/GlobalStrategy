using System.Threading;

namespace ECS {
	static class TypeIdCounter {
		static int _next;
		internal static int Next() => Interlocked.Increment(ref _next);
	}

	public static class TypeId<T> {
		public static readonly int Value = TypeIdCounter.Next();
	}
}
