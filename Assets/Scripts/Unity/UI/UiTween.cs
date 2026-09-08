using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	// Narrow imperative VisualElement interpolation helper (Docs/Specs/26_09_08_14_ui-animation-foundation).
	// Not a DI service, global registry, component, or general tween/sequence system - see
	// .claude/rules/unity/uitoolkit.md's animation-boundary rule for the hybrid USS/UiTween/barrier split.
	// Native USS transitions still own declarative class/style changes; AnimatableInt/AnimatableDouble and
	// their barriers still own project display-state; this helper only advances awaited imperative motion.
	public static class UiTween {
		public static float Linear(float t) => t;

		// Normalized/eased progress core shared by every adapter below. Returns true once the endpoint
		// was applied (successful completion), false when isCurrent reported supersession before that
		// point - callers use the result to decide whether a final bake is safe. Owner cancellation or
		// detachment from the panel always propagates as OperationCanceledException instead of returning,
		// so the caller's existing cancellation cleanup path runs.
		public static async UniTask<bool> RunAsync(
			VisualElement element,
			float duration,
			Action<float> update,
			CancellationToken cancellationToken,
			Func<float, float> easing = null,
			Func<bool> isCurrent = null) {
			if (element == null) {
				throw new ArgumentNullException(nameof(element));
			}
			if (update == null) {
				throw new ArgumentNullException(nameof(update));
			}
			easing ??= Linear;

			if (duration <= 0f) {
				cancellationToken.ThrowIfCancellationRequested();
				if (isCurrent != null && !isCurrent()) {
					return false;
				}
				EnsureAttached(element);
				update(easing(1f));
				return true;
			}

			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			// A detach can happen with no cancellation in flight (e.g. an unrelated HUD reload tearing
			// down the element's panel). Route it through the same linked token so the awaited frame
			// below throws OperationCanceledException instead of the loop spinning against a dead element.
			EventCallback<DetachFromPanelEvent> onDetach = _ => linked.Cancel();
			element.RegisterCallback(onDetach);
			try {
				float elapsed = 0f;
				while (elapsed < duration) {
					cancellationToken.ThrowIfCancellationRequested();
					if (isCurrent != null && !isCurrent()) {
						return false;
					}
					EnsureAttached(element);
					elapsed += Time.unscaledDeltaTime;
					update(easing(Mathf.Clamp01(elapsed / duration)));
					await UniTask.NextFrame(cancellationToken: linked.Token);
					cancellationToken.ThrowIfCancellationRequested();
					EnsureAttached(element);
				}
				if (isCurrent != null && !isCurrent()) {
					return false;
				}
				EnsureAttached(element);
				update(easing(1f));
				return true;
			} finally {
				element.UnregisterCallback(onDetach);
			}
		}

		// Establishes left/top at the source once, animates a pixel style.translate delta on top of
		// that (avoiding a per-frame layout write), then bakes left/top to the destination and resets
		// translate to zero only on successful completion - a cancelled or superseded motion never bakes,
		// leaving the element wherever RunAsync left it for the owning flow's own cleanup to handle.
		public static async UniTask MoveAsync(
			VisualElement element,
			Vector2 from,
			Vector2 to,
			float duration,
			CancellationToken cancellationToken,
			Func<float, float> easing = null,
			Func<bool> isCurrent = null) {
			if (element == null) {
				throw new ArgumentNullException(nameof(element));
			}
			element.style.left = from.x;
			element.style.top = from.y;
			element.style.translate = new Translate(0f, 0f);

			bool completed = await RunAsync(
				element,
				duration,
				t => {
					Vector2 current = Vector2.Lerp(from, to, t);
					element.style.translate = new Translate(current.x - from.x, current.y - from.y);
				},
				cancellationToken,
				easing,
				isCurrent);

			if (completed) {
				element.style.left = to.x;
				element.style.top = to.y;
				element.style.translate = new Translate(0f, 0f);
			}
		}

		static void EnsureAttached(VisualElement element) {
			if (element.panel == null) {
				throw new OperationCanceledException("UiTween target detached from its panel.");
			}
		}
	}
}
