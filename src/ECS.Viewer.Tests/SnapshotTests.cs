using System.Linq;
using ECS;
using ECS.Viewer;
using Xunit;

namespace ECS.Viewer.Tests {
	public class SnapshotTests {
		static WorldObserver Observer => new WorldObserver();

		[Fact]
		public void Capture_ReturnsAllEntities() {
			var world = new World();
			int a = world.Create();
			int b = world.Create();
			world.Add(a, new NameComp { Value = "alpha" });
			world.Add(b, new NameComp { Value = "beta" });

			WorldSnapshot snap = Observer.Capture(world);

			Assert.Equal(2, snap.Entities.Count);
			Assert.Contains(snap.Entities, e => e.Id == a);
			Assert.Contains(snap.Entities, e => e.Id == b);
		}

		[Fact]
		public void Capture_IncludesComponentTypeName() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new NameComp { Value = "x" });

			WorldSnapshot snap = Observer.Capture(world);
			EntitySnapshot es = snap.Entities.Single(x => x.Id == e);

			Assert.Contains(es.Components, c => c.TypeName == nameof(NameComp));
		}

		[Fact]
		public void Capture_EntityRefField_SerializedAsEntityRefValue() {
			var world = new World();
			int target = world.Create();
			int owner = world.Create();
			world.Add(owner, new LinkComp { Target = new EntityRef(target) });

			WorldSnapshot snap = Observer.Capture(world);
			EntitySnapshot es = snap.Entities.Single(x => x.Id == owner);
			ComponentSnapshot cs = es.Components.Single(c => c.TypeName == nameof(LinkComp));

			Assert.True(cs.Fields.ContainsKey(nameof(LinkComp.Target)));
			var refVal = cs.Fields[nameof(LinkComp.Target)] as EntityRefValue;
			Assert.NotNull(refVal);
			Assert.Equal(target, refVal!.EntityId);
		}

		[Fact]
		public void TrySetField_Int_UpdatesValue() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new ScoreComp { Value = 10 });

			bool ok = Observer.TrySetField(world, e, nameof(ScoreComp), nameof(ScoreComp.Value), "99");

			Assert.True(ok);
			Assert.Equal(99, world.Get<ScoreComp>(e).Value);
		}

		[Fact]
		public void TrySetField_Float_UpdatesValue() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new FloatComp { Value = 1.0f });

			bool ok = Observer.TrySetField(world, e, nameof(FloatComp), nameof(FloatComp.Value), "3.14");

			Assert.True(ok);
			Assert.Equal(3.14f, world.Get<FloatComp>(e).Value, 2);
		}

		[Fact]
		public void TrySetField_Bool_UpdatesValue() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new BoolComp { Flag = false });

			bool ok = Observer.TrySetField(world, e, nameof(BoolComp), nameof(BoolComp.Flag), "true");

			Assert.True(ok);
			Assert.True(world.Get<BoolComp>(e).Flag);
		}

		[Fact]
		public void TrySetField_String_UpdatesValue() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new NameComp { Value = "old" });

			bool ok = Observer.TrySetField(world, e, nameof(NameComp), nameof(NameComp.Value), "new");

			Assert.True(ok);
			Assert.Equal("new", world.Get<NameComp>(e).Value);
		}

		[Fact]
		public void TrySetField_Enum_UpdatesValue() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new StateComp { State = SimpleState.Off });

			bool ok = Observer.TrySetField(world, e, nameof(StateComp), nameof(StateComp.State), "On");

			Assert.True(ok);
			Assert.Equal(SimpleState.On, world.Get<StateComp>(e).State);
		}

		[Fact]
		public void TrySetField_EntityRef_ReturnsFalse() {
			var world = new World();
			int target = world.Create();
			int owner = world.Create();
			world.Add(owner, new LinkComp { Target = new EntityRef(target) });

			bool ok = Observer.TrySetField(world, owner, nameof(LinkComp), nameof(LinkComp.Target), "0");

			Assert.False(ok);
		}

		[Fact]
		public void TrySetField_UnknownEntity_ReturnsFalse() {
			var world = new World();
			bool ok = Observer.TrySetField(world, 99999, nameof(ScoreComp), nameof(ScoreComp.Value), "1");
			Assert.False(ok);
		}

		[Fact]
		public void TrySetField_UnknownType_ReturnsFalse() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new ScoreComp { Value = 5 });

			bool ok = Observer.TrySetField(world, e, "NoSuchType", "Value", "1");
			Assert.False(ok);
		}

		// ── Test component types ─────────────────────────────────────────────
		struct NameComp { public string Value; }
		struct ScoreComp { public int Value; }
		struct FloatComp { public float Value; }
		struct BoolComp { public bool Flag; }
		struct LinkComp { public EntityRef Target; }
		struct StateComp { public SimpleState State; }
		enum SimpleState { Off, On }
	}
}
