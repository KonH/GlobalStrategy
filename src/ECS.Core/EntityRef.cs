namespace ECS {
	public readonly struct EntityRef {
		public readonly int Id;
		public EntityRef(int id) => Id = id;
		public static implicit operator int(EntityRef r) => r.Id;
		public static implicit operator EntityRef(int id) => new EntityRef(id);
		public override string ToString() => $"Entity#{Id}";
	}
}
