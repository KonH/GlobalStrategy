namespace ECS {
	struct EntityRecord {
		public int Generation;
		public Archetype? Archetype;
		public int Row;

		public EntityRecord(int gen, Archetype archetype, int row) {
			Generation = gen;
			Archetype = archetype;
			Row = row;
		}
	}
}
