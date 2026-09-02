using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.Map;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>
	/// Composition root for the Gallery scene. Owns an ordered list of GalleryBlocks and builds
	/// one ui:Foldout per block into the "gallery-blocks" container - it does not know anything
	/// about any block's own content, just its title and persisted expand/selection state. Every
	/// dependency a block needs is resolved through GalleryLifetimeScope and handed to the block's
	/// constructor here, so this class never grows into a second HUDDocument.
	/// </summary>
	public class GalleryDocument : MonoBehaviour {
		[SerializeField] PanelRenderer _document;
		[SerializeField] double _discardGoldCost = 50;
		[SerializeField] string _sampleTargetCountryId = "france";
		// Kept as a plain SerializeField rather than DI-registered - see GalleryLifetimeScope's
		// comment on why a second TextAsset dependency can't safely go through the container.
		[SerializeField] TextAsset _characterConfigAsset;
		// Same reasoning as _characterConfigAsset above: a third TextAsset would be just as
		// ambiguous to register by type, so the HUD panel gallery blocks (phase 7) deserialize it
		// themselves via HudConfigLoader.
		[SerializeField] TextAsset _resourceConfigAsset;
		// Same reasoning again: used to filter every country-id dropdown down to
		// CountryConfig.Countries[].IsAvailable == true, matching what the real game shows -
		// CountryVisualConfig only carries flag/visual metadata, not availability.
		[SerializeField] TextAsset _countryConfigAsset;
		// Debug-tools gallery blocks (Docs/Specs/26_08_28_16_ui-refactoring phase 5) clone this to
		// preview the debug panel and its sub-menus without a running game.
		[SerializeField] VisualTreeAsset _debugUxml;
		// HUD panel gallery blocks (phase 7) clone named sub-elements out of this the same way -
		// see HudGalleryPreview.
		[SerializeField] VisualTreeAsset _hudUxml;
		[SerializeField] VisualTreeAsset _flyTextUxml;
		// Hand/deck and animation gallery blocks (phase 7) clone named sub-elements out of this
		// the same way - OrgInfo.uxml is a separate UIDocument, not part of HUD.uxml's tree.
		[SerializeField] VisualTreeAsset _orgInfoUxml;
		// "The seven windows that already have a view" gallery blocks (phase 7) - each window's own
		// full UXML, cloned via HudGalleryPreview.CloneNamed the same way the HUD panel blocks clone
		// out of HUD.uxml (these window roots are the same "absolute full-screen overlay" shape).
		[SerializeField] VisualTreeAsset _leaderboardWindowUxml;
		[SerializeField] VisualTreeAsset _goalsWindowUxml;
		[SerializeField] VisualTreeAsset _warProgressWindowUxml;
		[SerializeField] VisualTreeAsset _warResultWindowUxml;
		[SerializeField] VisualTreeAsset _endGameWindowUxml;
		[SerializeField] VisualTreeAsset _countryDestroyedWindowUxml;
		[SerializeField] VisualTreeAsset _orgDestroyedWindowUxml;
		// "The six view-less documents" gallery blocks (phase 7) - same treatment as the seven
		// windows above, now that each has its own extracted view class.
		[SerializeField] VisualTreeAsset _mainMenuUxml;
		[SerializeField] VisualTreeAsset _gameMenuUxml;
		[SerializeField] VisualTreeAsset _settingsWindowUxml;
		[SerializeField] VisualTreeAsset _loadWindowUxml;
		[SerializeField] VisualTreeAsset _selectCountryUxml;

		// Serialized so the current selection also survives the domain reload a script recompile
		// causes, not just a UXML/USS re-import. Keyed by block id, not by a fixed set of fields,
		// so every block - not just one - keeps its expansion and both selections.
		[SerializeField, HideInInspector] List<GalleryBlockState> _blockStates = new();

		ILocalization _loc;
		TextAsset _actionConfigAsset;
		ActionVisualConfig _actionVisualConfig;
		CountryVisualConfig _countryVisualConfig;
		CharacterVisualConfig _characterVisualConfig;
		OrgVisualConfig _orgVisualConfig;

		// Loaded once in BuildBlocks and reused across many block constructors below to filter every
		// country-id dropdown down to CountryConfig.Countries[].IsAvailable == true.
		CountryConfig _countryConfig;

		List<IGalleryBlock> _blocks;
		VisualElement _root;
		bool _built;

		// Focus-mode host (see EnterFocusMode/ExitFocusMode below): a genuine full-screen sibling
		// of gallery-scroll, not something nested inside it, so a full-surface block previewed here
		// renders as a true top-level panel root instead of a clone embedded in someone else's layout.
		VisualElement _galleryScroll;
		VisualElement _focusRoot;
		Label _focusTitle;
		Button _focusBackButton;
		VisualElement _focusContent;
		GalleryBlockBase _focusedBlock;
		GalleryBlockState _focusedState;

		[Inject]
		void Construct(
			ILocalization loc,
			TextAsset actionConfigAsset,
			ActionVisualConfig actionVisualConfig,
			CountryVisualConfig countryVisualConfig,
			CharacterVisualConfig characterVisualConfig,
			OrgVisualConfig orgVisualConfig) {
			_loc = loc;
			_actionConfigAsset = actionConfigAsset;
			_actionVisualConfig = actionVisualConfig;
			_countryVisualConfig = countryVisualConfig;
			_characterVisualConfig = characterVisualConfig;
			_orgVisualConfig = orgVisualConfig;
		}

		void OnEnable() {
			if (_document == null) {
				_document = GetComponent<PanelRenderer>();
			}
			if (_document == null) {
				Debug.LogError("[Gallery] GalleryDocument is missing its PanelRenderer reference - check the Gallery scene wiring.");
				return;
			}
			// PanelRenderer.rootVisualElement is internal - RegisterUIReloadCallback is the only
			// public route to the root. It fires once the panel first loads and again on every
			// later UXML/USS reload while playing, which replaces the per-frame
			// "_blocksHost.panel != null" polling this document used to do in Update().
			_document.RegisterUIReloadCallback(OnUIReload);
		}

		void OnDisable() {
			if (_document != null) {
				_document.UnregisterUIReloadCallback(OnUIReload);
			}
		}

		void Start() {
			// VContainer injects during the scope's Awake/Build, which can run after OnEnable, so
			// _loc may still be null when the first reload callback fires. Bind from the root the
			// callback already captured once injection has definitely completed.
			if (!_built && _root != null && _loc != null) {
				Bind(_root);
			}
		}

		void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement) {
			_root = rootElement;
			if (_loc == null) {
				// Injection hasn't run yet - Start() binds once it has.
				return;
			}
			Bind(rootElement);
		}

		void Bind(VisualElement root) {
			if (root == null) {
				return;
			}
			VisualElement blocksHost = root.Q<VisualElement>("gallery-blocks");
			if (blocksHost == null) {
				Debug.LogError("[Gallery] 'gallery-blocks' container not found in the reloaded Gallery UXML.");
				return;
			}

			_galleryScroll = root.Q<VisualElement>("gallery-scroll");
			_focusRoot = root.Q<VisualElement>("gallery-focus-root");
			_focusTitle = root.Q<Label>("gallery-focus-title");
			_focusBackButton = root.Q<Button>("gallery-focus-back-button");
			_focusContent = root.Q<VisualElement>("gallery-focus-content");
			if (_focusRoot == null || _focusContent == null) {
				Debug.LogError("[Gallery] focus-mode elements not found in the reloaded Gallery UXML.");
				return;
			}
			// Every UXML/USS reload rebuilds the tree, so re-wire the button and reset focus mode
			// closed each time rather than trying to preserve a focused state across a live edit.
			_focusBackButton?.OnClick(ExitFocusMode);
			_focusRoot.style.display = DisplayStyle.None;
			_focusedBlock = null;
			_focusedState = null;

			if (_blocks == null) {
				_blocks = BuildBlocks();
			}

			blocksHost.Clear();
			foreach (IGalleryBlock block in _blocks) {
				BuildFoldoutFor(block, blocksHost);
			}
			_built = true;
		}

		/// <summary>
		/// Switches the whole Gallery view into full-screen focus mode, showing `block` rendered into
		/// the shared _focusContent container instead of gallery-scroll's small inline stage.
		/// _focusContent is a true full-panel-size, position:Relative sibling of gallery-scroll (see
		/// Gallery.uss ".gallery-focus-content"), so real UI content dropped into it resolves
		/// width:100%/height:100%/absolute layout against the TRUE full Gallery panel size - the same
		/// rendering context production windows get against hud-root, not a clone nested inside
		/// gallery-scroll's flex column.
		/// </summary>
		void EnterFocusMode(GalleryBlockBase block, GalleryBlockState state) {
			if (block == null || _focusRoot == null || _focusContent == null) {
				return;
			}
			_focusedBlock = block;
			_focusedState = state;
			if (_focusTitle != null) {
				_focusTitle.text = block.Title;
			}

			block.RenderInto(_focusContent, state);

			if (_galleryScroll != null) {
				_galleryScroll.style.display = DisplayStyle.None;
			}
			_focusRoot.style.display = DisplayStyle.Flex;
		}

		void ExitFocusMode() {
			if (_focusRoot == null) {
				return;
			}
			_focusContent?.Clear();
			_focusRoot.style.display = DisplayStyle.None;
			if (_galleryScroll != null) {
				_galleryScroll.style.display = DisplayStyle.Flex;
			}
			_focusedBlock = null;
			_focusedState = null;
		}

		void Update() {
			// Escape is a convenience exit alongside the explicit Back button. Only checked while a
			// block is actually focused, so this does not reintroduce the kind of per-frame
			// panel-presence polling the PanelRenderer migration removed from this class - it is one
			// key check, gated on focus state, not a query against panel/element existence.
			if (_focusedBlock == null || _focusRoot == null || _focusRoot.style.display != DisplayStyle.Flex) {
				return;
			}
			Keyboard keyboard = Keyboard.current;
			if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) {
				ExitFocusMode();
			}
		}

		List<IGalleryBlock> BuildBlocks() {
			_countryConfig = HudConfigLoader.LoadCountryConfig(_countryConfigAsset);
			CountryConfig countryConfig = _countryConfig;
			return new List<IGalleryBlock> {
				new ActionCardGalleryBlock(
					_loc, _actionConfigAsset, _actionVisualConfig, _countryVisualConfig,
					_discardGoldCost, _sampleTargetCountryId, countryConfig),
				new FlagBadgeGalleryBlock(_countryVisualConfig, countryConfig),
				new ResourceChipGalleryBlock(_loc),
				new StatChipGalleryBlock(_loc),
				new ProgressBarGalleryBlock(),
				new RankRowGalleryBlock(_loc, _countryVisualConfig, countryConfig),
				new EffectRowGalleryBlock(_loc),
				new BattleRowGalleryBlock(_loc),
				new ProvinceTransferRowGalleryBlock(_loc, _countryVisualConfig, countryConfig),
				new RequirementRowGalleryBlock(),
				new CharacterCardGalleryBlock(_loc, _characterConfigAsset, _characterVisualConfig),
				new TaskCardGalleryBlock(_loc),
				new TooltipBodyGalleryBlock(),
				new HandContainerGalleryBlock(),
				new DrawSlotGalleryBlock(),
				new FlagNameHeaderGalleryBlock(_loc, _countryVisualConfig, countryConfig),
				new DebugPanelGalleryBlock(_debugUxml),
				new DebugProvinceMenuGalleryBlock(_debugUxml),
				new DebugRelationMenuGalleryBlock(_debugUxml),
				new DebugControlOrgMenuGalleryBlock(_debugUxml),
				new DebugCharacterMenuGalleryBlock(_debugUxml),
				new DebugCardAvailabilityGalleryBlock(_loc, _actionConfigAsset),
				new FpsCounterGalleryBlock(_debugUxml),
				new CountryInfoGalleryBlock(
					_loc, _hudUxml, _resourceConfigAsset, _characterConfigAsset, _actionConfigAsset,
					_actionVisualConfig, _characterVisualConfig, _countryVisualConfig, _orgVisualConfig, countryConfig),
				new ProvinceInfoGalleryBlock(_loc, _hudUxml, _resourceConfigAsset, _countryVisualConfig),
				new HudResourcesGalleryBlock(_loc, _resourceConfigAsset),
				new HudPlayerOrgGalleryBlock(_loc, _hudUxml, _resourceConfigAsset, _orgVisualConfig),
				new HudPlayerTasksGalleryBlock(_loc, _hudUxml, _resourceConfigAsset),
				new HudTimeGalleryBlock(_hudUxml),
				new HudLensSwitcherGalleryBlock(_loc, _hudUxml),
				new HudOrgLensCountryGalleryBlock(_loc, _hudUxml, _resourceConfigAsset, _countryVisualConfig, _orgVisualConfig, countryConfig),
				new HudActionLogGalleryBlock(_loc, _hudUxml, _countryVisualConfig, _orgVisualConfig),
				new HudWarIconsGalleryBlock(_loc, _hudUxml, _countryVisualConfig),
				new HudTutorialHighlightGalleryBlock(_hudUxml),
				new HudFlyTextGalleryBlock(_flyTextUxml),
				new CountryActionsHandGalleryBlock(
					_loc, _hudUxml, _actionConfigAsset, _actionVisualConfig, _countryVisualConfig,
					_resourceConfigAsset, _discardGoldCost),
				new OrgActionsGalleryBlock(_loc, _orgInfoUxml, _actionConfigAsset, _actionVisualConfig, _resourceConfigAsset),
				new CardTransitionGalleryBlock(_loc, _actionConfigAsset, _actionVisualConfig, _countryVisualConfig),
				new CardDrawOfferGalleryBlock(_loc, _actionConfigAsset, _actionVisualConfig, _countryVisualConfig),
				new CardPlayTestOverlayGalleryBlock(_loc, _hudUxml, _actionConfigAsset, _actionVisualConfig, _countryVisualConfig),
				new CountryCharactersGalleryBlock(
					_loc, _hudUxml, _characterConfigAsset, _characterVisualConfig, _actionConfigAsset, _actionVisualConfig),
				new OrgCharactersGalleryBlock(_loc, _orgInfoUxml, _characterConfigAsset, _characterVisualConfig),
				new OrgInfoGalleryBlock(
					_loc, _orgInfoUxml, _resourceConfigAsset, _characterConfigAsset, _characterVisualConfig,
					_orgVisualConfig, _actionConfigAsset, _actionVisualConfig),
				new LeaderboardWindowGalleryBlock(_loc, _leaderboardWindowUxml, _countryVisualConfig, _orgVisualConfig, countryConfig),
				new GoalsWindowGalleryBlock(_loc, _goalsWindowUxml, _orgVisualConfig),
				new WarProgressWindowGalleryBlock(_loc, _warProgressWindowUxml, _countryVisualConfig),
				new WarResultWindowGalleryBlock(_loc, _warResultWindowUxml, _countryVisualConfig),
				new EndGameWindowGalleryBlock(_loc, _endGameWindowUxml, _orgVisualConfig),
				new CountryDestroyedWindowGalleryBlock(_loc, _countryDestroyedWindowUxml, _countryVisualConfig, countryConfig),
				new OrgDestroyedWindowGalleryBlock(_loc, _orgDestroyedWindowUxml, _orgVisualConfig),
				new MainMenuGalleryBlock(_loc, _mainMenuUxml),
				new GameMenuGalleryBlock(_loc, _gameMenuUxml),
				new SettingsWindowGalleryBlock(_loc, _settingsWindowUxml),
				new LoadWindowGalleryBlock(_loc, _loadWindowUxml),
				new SelectOrgGalleryBlock(_loc, _selectCountryUxml, _orgVisualConfig),
			};
		}

		void BuildFoldoutFor(IGalleryBlock block, VisualElement parent) {
			GalleryBlockState state = GetOrCreateState(block.Id);

			if (block is GalleryBlockBase baseBlock) {
				baseBlock.EnterFocusModeRequested = EnterFocusMode;
			}

			var foldout = new Foldout { text = block.Title, value = state.Expanded };
			foldout.AddToClassList("gallery-block");
			foldout.RegisterValueChangedCallback(evt => {
				// A Foldout also receives bool change events bubbling up from its content, so
				// only its own toggle counts as the block being expanded or collapsed.
				if (evt.target == foldout) {
					state.Expanded = evt.newValue;
				}
			});
			parent.Add(foldout);

			block.Build(foldout, state);
		}

		GalleryBlockState GetOrCreateState(string blockId) {
			foreach (GalleryBlockState state in _blockStates) {
				if (state.BlockId == blockId) {
					return state;
				}
			}
			var created = new GalleryBlockState { BlockId = blockId };
			_blockStates.Add(created);
			return created;
		}

		/// <summary>
		/// Sizes a dropdown to its widest choice instead of a fixed width, so adding a longer
		/// action id or state name never truncates the popup text. Measures with the popup's own
		/// text element so the field's real font and size are used, and re-measures on the first
		/// geometry pass because fonts do not resolve until the field is laid out in a panel.
		/// Host infrastructure - every block uses it, none should copy it.
		/// </summary>
		public static void FitDropdownToWidestChoice(DropdownField dropdown) {
			if (dropdown == null) {
				return;
			}
			float applied = -1f;

			void Apply() {
				var text = dropdown.Q<TextElement>(className: "unity-base-popup-field__text");
				if (text == null || dropdown.choices == null || dropdown.choices.Count == 0) {
					return;
				}
				float widest = 0f;
				foreach (string choice in dropdown.choices) {
					Vector2 size = text.MeasureTextSize(
						choice, 0f, VisualElement.MeasureMode.Undefined,
						0f, VisualElement.MeasureMode.Undefined);
					if (size.x > widest) {
						widest = size.x;
					}
				}
				if (widest <= 0f) {
					return;
				}
				// A few px so the longest entry never sits flush against the dropdown arrow.
				float target = Mathf.Ceil(widest) + 8f;
				// Guard against re-entering from the geometry change this very assignment causes.
				if (Mathf.Abs(applied - target) < 0.5f) {
					return;
				}
				applied = target;
				text.style.minWidth = target;
			}

			dropdown.RegisterCallback<GeometryChangedEvent>(_ => Apply());
			Apply();
		}
	}
}
