using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	static class ActionCardBuilder {
		const int CooldownTextureSize = 128;

		static readonly Dictionary<int, Texture2D> _cooldownTextureCache = new();

		public struct CardResult {
			public VisualElement Card;
			public VisualElement Body;
			public Label CostLabel;
		}

		public static CardResult Build(
			string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			var card = new VisualElement();
			card.AddToClassList("action-card");
			var result = Populate(card, name, desc, goldCostText, art, warWinChancePercent, cooldownFractionRemaining, cooldownRemainingDays);
			result.Card = card;
			return result;
		}

		public static CardResult PopulateSlot(
			VisualElement slot, string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			slot.Clear();
			slot.RemoveFromClassList("action-card--success");
			slot.RemoveFromClassList("action-card--fail");
			var result = Populate(slot, name, desc, goldCostText, art, warWinChancePercent, cooldownFractionRemaining, cooldownRemainingDays);
			result.Card = slot;
			return result;
		}

		static CardResult Populate(
			VisualElement container, string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			var header = new Label(name);
			header.AddToClassList("action-card-header");
			container.Add(header);
			SetupHeaderAutoSize(header);

			var artEl = new VisualElement();
			artEl.AddToClassList("action-card-art");
			var artImage = new VisualElement();
			artImage.AddToClassList("action-card-art-image");
			if (art != null) {
				artImage.style.backgroundImage = new StyleBackground(art);
			}
			artEl.Add(artImage);
			if (warWinChancePercent.HasValue) {
				artEl.Add(BuildWarWinChanceBadge(warWinChancePercent.Value));
			}
			container.Add(artEl);

			var body = new VisualElement();
			body.AddToClassList("action-card-body");

			var descLabel = new Label(desc);
			descLabel.AddToClassList("action-card-desc");
			body.Add(descLabel);
			SetupDescAutoSize(descLabel);

			var footer = new VisualElement();
			footer.AddToClassList("action-card-footer");

			Label costLabel = null;
			if (!string.IsNullOrEmpty(goldCostText)) {
				var costRow = new VisualElement();
				costRow.AddToClassList("action-card-cost");
				costLabel = new Label(goldCostText);
				costLabel.AddToClassList("action-card-cost-label");
				costRow.Add(costLabel);
				var costIcon = new VisualElement();
				costIcon.AddToClassList("action-card-cost-icon");
				costRow.Add(costIcon);
				footer.Add(costRow);
			}

			body.Add(footer);
			container.Add(body);

			if (cooldownFractionRemaining.HasValue) {
				container.Add(BuildCooldownOverlay(cooldownFractionRemaining.Value, cooldownRemainingDays));
			}

			return new CardResult { Body = body, CostLabel = costLabel };
		}

		static VisualElement BuildCooldownOverlay(double fractionRemaining, double? remainingDays) {
			var overlay = new VisualElement();
			overlay.AddToClassList("action-card-cooldown-overlay");

			var radial = new VisualElement();
			radial.AddToClassList("action-card-cooldown-radial");
			radial.style.backgroundImage = new StyleBackground(GetOrCreateCooldownTexture(fractionRemaining));
			overlay.Add(radial);

			var label = new Label(FormatCooldownRemaining(remainingDays));
			label.AddToClassList("action-card-cooldown-label");
			overlay.Add(label);

			return overlay;
		}

		static Texture2D GetOrCreateCooldownTexture(double fractionRemaining) {
			int bucket = Mathf.RoundToInt(Mathf.Clamp01((float)fractionRemaining) * 100);
			if (_cooldownTextureCache.TryGetValue(bucket, out var cached)) {
				return cached;
			}

			float fraction = bucket / 100f;
			var texture = new Texture2D(CooldownTextureSize, CooldownTextureSize, TextureFormat.RGBA32, false);
			texture.filterMode = FilterMode.Bilinear;
			float center = CooldownTextureSize / 2f;
			float radius = CooldownTextureSize / 2f;
			var fillColor = new Color(0f, 0f, 0f, 0.6f);
			var clearColor = new Color(0f, 0f, 0f, 0f);
			for (int y = 0; y < CooldownTextureSize; y++) {
				for (int x = 0; x < CooldownTextureSize; x++) {
					float dx = x + 0.5f - center;
					float dy = y + 0.5f - center;
					float dist = Mathf.Sqrt(dx * dx + dy * dy);
					if (dist > radius) {
						texture.SetPixel(x, y, clearColor);
						continue;
					}
					float angleDeg = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
					if (angleDeg < 0f) { angleDeg += 360f; }
					bool filled = (angleDeg / 360f) < fraction;
					texture.SetPixel(x, y, filled ? fillColor : clearColor);
				}
			}
			texture.Apply();
			_cooldownTextureCache[bucket] = texture;
			return texture;
		}

		static string FormatCooldownRemaining(double? remainingDays) {
			if (!remainingDays.HasValue || remainingDays.Value <= 0) { return ""; }
			int days = (int)remainingDays.Value;
			if (days >= 365) { return $"{days / 365} year(s)"; }
			if (days >= 30) { return $"{days / 30} month(s)"; }
			if (days >= 2) { return $"{days} days"; }
			if (days == 1) { return "1 day"; }
			return "less than a day";
		}

		static Label BuildWarWinChanceBadge(int percent) {
			var badge = new Label($"{percent}%");
			badge.AddToClassList("action-card-war-win-chance");
			if (percent <= 33) {
				badge.AddToClassList("action-card-war-win-chance--low");
			} else if (percent <= 66) {
				badge.AddToClassList("action-card-war-win-chance--mid");
			} else {
				badge.AddToClassList("action-card-war-win-chance--high");
			}
			return badge;
		}

		static void SetupDescAutoSize(Label desc, float minSize = 11f) {
			desc.RegisterCallback<GeometryChangedEvent>(_ => {
				float availH = desc.resolvedStyle.height;
				float availW = desc.resolvedStyle.width;
				if (availH <= 0 || availW <= 0) { return; }
				var measured = desc.MeasureTextSize(
					desc.text, availW, VisualElement.MeasureMode.AtMost, float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.y > availH + 0.5f) {
					float cur = desc.resolvedStyle.fontSize;
					if (cur > minSize) {
						float scale = availH / measured.y;
						float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
						if (newSize < cur) { desc.style.fontSize = newSize; }
					}
				}
			});
		}

		static void SetupHeaderAutoSize(Label header, float minSize = 12f) {
			header.RegisterCallback<GeometryChangedEvent>(_ => {
				float availW = header.resolvedStyle.width;
				if (availW <= 0) { return; }
				var measured = header.MeasureTextSize(
					header.text, float.PositiveInfinity, VisualElement.MeasureMode.Undefined,
					float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.x > availW + 0.5f) {
					float cur = header.resolvedStyle.fontSize;
					if (cur > minSize) {
						float scale = availW / measured.x;
						float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
						if (newSize < cur) { header.style.fontSize = newSize; }
					}
				}
			});
		}
	}
}
