namespace ECS {
	static class EntityPacker {
		const int IndexBits = 20;
		const int IndexMask = (1 << IndexBits) - 1; // 0xFFFFF
		const int GenMask = 0xFFF;                   // 12 bits, wraps at 4096

		public static int Pack(int index, int gen) =>
			((gen & GenMask) << IndexBits) | (index & IndexMask);

		public static void Unpack(int id, out int index, out int gen) {
			index = id & IndexMask;
			gen = (id >> IndexBits) & GenMask;
		}
	}
}
