using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Game.Tests.Helpers {
	/// <summary>
	/// Fluent fixture around <see cref="ECS.World"/> for tests.
	/// <para>
	/// Every builder method creates one entity, records it as <see cref="Last"/> and returns
	/// <c>this</c>, so setup reads as a chain instead of repeated Create/Add pairs. The
	/// singleton-ish entities that systems and <c>VisualStateConverter</c> need by id are
	/// remembered as <see cref="GameTimeEntity"/>, <see cref="LocaleEntity"/> and
	/// <see cref="OrgEntity"/>, so callers no longer thread them through <c>out</c> parameters.
	/// </para>
	/// Implicitly converts to <see cref="ECS.World"/>, so it can be passed straight to systems.
	/// </summary>
	public sealed class TestWorld : IReadOnlyWorld {
		public const string DefaultOrgId = "OrgA";
		public const string DefaultCountryId = "Prussia";
		public const string DefaultLocale = "en";

		/// <summary>The default in-game "now" — the campaign start date.</summary>
		public static readonly DateTime DefaultTime = new DateTime(1880, 1, 1);

		readonly Dictionary<string, int> _named = new Dictionary<string, int>(StringComparer.Ordinal);

		public World World { get; } = new World();

		/// <summary>Entity created by the most recent builder call.</summary>
		public int Last { get; private set; } = -1;

		public int GameTimeEntity { get; private set; } = -1;
		public int LocaleEntity { get; private set; } = -1;
		public int OrgEntity { get; private set; } = -1;

		public static TestWorld Create() {
			return new TestWorld();
		}

		/// <summary>
		/// The setup nearly every gameplay / visual-state test needs: game time, locale, one org
		/// and one selected country.
		/// </summary>
		public static TestWorld CreateWithSelectedCountry(
			string countryId = DefaultCountryId,
			string orgId = DefaultOrgId,
			DateTime? time = null,
			string locale = DefaultLocale) {
			return Create()
				.GameTime(time)
				.Locale(locale)
				.Org(orgId)
				.Country(countryId, selected: true);
		}

		public static implicit operator World(TestWorld testWorld) => testWorld.World;

		// --- raw entity plumbing -------------------------------------------------

		/// <summary>Creates a bare entity and makes it <see cref="Last"/>.</summary>
		public TestWorld Entity() {
			Last = World.Create();
			return this;
		}

		/// <summary>Adds a component to <see cref="Last"/>.</summary>
		public TestWorld With<T>(T component) {
			World.Add(RequireLast(), component);
			return this;
		}

		/// <summary>Remembers <see cref="Last"/> under <paramref name="name"/> for later <see cref="Id"/> lookup.</summary>
		public TestWorld As(string name) {
			_named[name] = RequireLast();
			return this;
		}

		/// <summary>Entity previously tagged with <see cref="As"/>.</summary>
		public int Id(string name) {
			if (!_named.TryGetValue(name, out int entity)) {
				throw new InvalidOperationException($"No entity named '{name}' was registered via As().");
			}
			return entity;
		}

		/// <summary>Adds a component to an entity other than <see cref="Last"/>.</summary>
		public TestWorld Add<T>(int entity, T component) {
			World.Add(entity, component);
			return this;
		}

		public TestWorld Remove<T>(int entity) {
			World.Remove<T>(entity);
			return this;
		}

		public TestWorld Destroy(int entity) {
			World.Destroy(entity);
			return this;
		}

		/// <summary>Escape hatch for setup this builder does not cover.</summary>
		public TestWorld Do(Action<World> action) {
			action(World);
			return this;
		}

		// --- world singletons ----------------------------------------------------

		public TestWorld GameTime(DateTime? time = null, bool paused = false, int multiplierIndex = 0) {
			Entity().With(new GS.Game.Components.GameTime {
				CurrentTime = time ?? DefaultTime,
				IsPaused = paused,
				MultiplierIndex = multiplierIndex
			});
			GameTimeEntity = Last;
			return this;
		}

		public TestWorld Locale(string value = DefaultLocale) {
			Entity().With(new GS.Game.Components.Locale { Value = value });
			LocaleEntity = Last;
			return this;
		}

		// --- actors --------------------------------------------------------------

		public TestWorld Country(string countryId = DefaultCountryId, bool selected = false, bool destroyed = false) {
			Entity().With(new GS.Game.Components.Country(countryId));
			if (selected) {
				With(new IsSelected());
			}
			if (destroyed) {
				With(new IsDestroyed());
			}
			return this;
		}

		public TestWorld Countries(params string[] countryIds) {
			foreach (string countryId in countryIds) {
				Country(countryId);
			}
			return this;
		}

		/// <summary>
		/// Creates an organization. The first org created, or any created with
		/// <paramref name="isPrimary"/>, becomes <see cref="OrgEntity"/>.
		/// </summary>
		public TestWorld Org(
			string orgId = DefaultOrgId,
			string? displayName = null,
			bool destroyed = false,
			bool isPrimary = true) {
			Entity().With(new Organization { OrganizationId = orgId, DisplayName = displayName ?? orgId });
			if (destroyed) {
				With(new IsOrgDestroyed());
			}
			if (isPrimary || (OrgEntity < 0)) {
				OrgEntity = Last;
			}
			return this;
		}

		public TestWorld Character(
			string characterId,
			string countryId = DefaultCountryId,
			string roleId = "diplomacy_advisor",
			string orgId = "",
			string[]? namePartKeys = null) {
			return Entity().With(new GS.Game.Components.Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = orgId,
				RoleId = roleId,
				NamePartKeys = namePartKeys ?? Array.Empty<string>()
			});
		}

		// --- resources -----------------------------------------------------------

		public TestWorld Resource(
			string ownerId,
			string resourceId,
			double value,
			OwnerType ownerType = OwnerType.Org) {
			return Entity()
				.With(new ResourceOwner(ownerId, ownerType))
				.With(new GS.Game.Components.Resource { ResourceId = resourceId, Value = value });
		}

		/// <summary>Character opinion toward an org, stored as the <c>opinion_&lt;orgId&gt;</c> resource.</summary>
		public TestWorld Opinion(string characterId, double value, string orgId = DefaultOrgId) {
			return Resource(characterId, "opinion_" + orgId, value, OwnerType.Character);
		}

		public TestWorld ResourceEffect(
			string ownerId,
			string resourceId,
			double value,
			string effectId,
			OwnerType ownerType = OwnerType.Org,
			PayType payType = PayType.Instant,
			double maxTotal = 0,
			double accumulatedTotal = 0,
			bool clampToZero = false,
			string orgId = "") {
			return Entity()
				.With(new ResourceOwner(ownerId, ownerType))
				.With(new ResourceLink(resourceId))
				.With(new GS.Game.Components.ResourceEffect {
					EffectId = effectId,
					Value = value,
					PayType = payType,
					MaxTotal = maxTotal,
					AccumulatedTotal = accumulatedTotal,
					ClampToZero = clampToZero,
					OrgId = orgId
				});
		}

		public TestWorld Control(
			string countryId = DefaultCountryId,
			int value = 10,
			string orgId = DefaultOrgId,
			string? effectId = null) {
			return Entity().With(new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = effectId ?? ("control_" + orgId + "_" + countryId)
			});
		}

		// --- cards ---------------------------------------------------------------

		public TestWorld Deck(
			string orgId = DefaultOrgId,
			CardOwnerKind kind = CardOwnerKind.Country,
			int handSize = 1) {
			return Entity()
				.With(new CardDeck { OrgId = orgId })
				.With(new CardOwnerType(kind))
				.With(new CardHand { HandSize = handSize });
		}

		/// <summary>
		/// A card entity. A <paramref name="slotIndex"/> of <c>null</c> leaves the card out of hand
		/// (no <see cref="CardInHand"/>); a <paramref name="countryId"/> of <c>null</c> leaves it
		/// untargeted (no <see cref="CountryContext"/>).
		/// </summary>
		public TestWorld Card(
			string actionId,
			int? slotIndex = 0,
			string orgId = DefaultOrgId,
			string? countryId = DefaultCountryId,
			CardOwnerKind kind = CardOwnerKind.Country) {
			Entity()
				.With(new GameAction { ActionId = actionId })
				.With(new OrgContext { OrgId = orgId })
				.With(new CardOwnerType(kind));
			if (countryId != null) {
				With(new CountryContext { CountryId = countryId });
			}
			if (slotIndex.HasValue) {
				With(new CardInHand { SlotIndex = slotIndex.Value });
			}
			return this;
		}

		// --- wars & provinces ----------------------------------------------------

		public TestWorld War(string warId, DateTime? declaredAt = null) {
			return Entity().With(new GS.Game.Components.War { WarId = warId, DeclaredAt = declaredAt ?? DefaultTime });
		}

		public TestWorld WarParticipant(string warId, string countryId, WarParticipantKind kind) {
			return Entity().With(new GS.Game.Components.WarParticipant { WarId = warId, CountryId = countryId, Kind = kind });
		}

		/// <summary>A war plus its attacker and defender participant entities.</summary>
		public TestWorld WarBetween(
			string warId,
			string attackerCountryId,
			string defenderCountryId,
			DateTime? declaredAt = null) {
			return War(warId, declaredAt)
				.WarParticipant(warId, attackerCountryId, WarParticipantKind.Attacker)
				.WarParticipant(warId, defenderCountryId, WarParticipantKind.Defender);
		}

		public TestWorld Province(string provinceId, string ownerCountryId) {
			return Entity().With(new ProvinceOwnership { ProvinceId = provinceId, OwnerId = ownerCountryId });
		}

		// --- queries -------------------------------------------------------------

		// IReadOnlyWorld is implemented by delegation so a TestWorld can be handed straight to
		// read-only consumers (queries, projectors) that take the interface — an implicit
		// conversion operator to an interface is not legal in C#.
		public bool IsAlive(int entity) => World.IsAlive(entity);

		public bool Has<TComp>(int entity) => World.Has<TComp>(entity);

		public ref TComp Get<TComp>(int entity) => ref World.Get<TComp>(entity);

		public bool TryGet<TComp>(int entity, out TComp comp) => World.TryGet(entity, out comp);

		public IEnumerable<Archetype> GetMatchingArchetypes(int[] required, int[]? excluded) =>
			World.GetMatchingArchetypes(required, excluded);

		public int Count<T>() {
			int count = 0;
			World.Query((int _, ref T _) => count++);
			return count;
		}

		public List<int> Entities<T>() {
			var result = new List<int>();
			World.Query((int entity, ref T _) => result.Add(entity));
			return result;
		}

		public List<T> AllComponents<T>() {
			var result = new List<T>();
			World.Query((int _, ref T component) => result.Add(component));
			return result;
		}

		/// <summary>The single entity carrying <typeparamref name="T"/>; throws unless there is exactly one.</summary>
		public int SingleEntity<T>() {
			List<int> entities = Entities<T>();
			if (entities.Count != 1) {
				throw new InvalidOperationException(
					$"Expected exactly one entity with {typeof(T).Name}, found {entities.Count}.");
			}
			return entities[0];
		}

		/// <summary>The single <typeparamref name="T"/> component in the world; throws unless there is exactly one.</summary>
		public T Single<T>() {
			return World.Get<T>(SingleEntity<T>());
		}

		int RequireLast() {
			if (Last < 0) {
				throw new InvalidOperationException(
					"No entity has been created yet — call Entity() or a builder method first.");
			}
			return Last;
		}
	}
}
