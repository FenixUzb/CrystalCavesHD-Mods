using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LetterboxCamera;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrystalCavesHD.Widescreen
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class WidescreenFixPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.crystalcaveshd.widescreen";
        public const string PluginName = "Crystal Caves HD Widescreen Fix";
        public const string PluginVersion = "0.2.0";

        private const string GameplayScene = "Game";
        private const float OriginalAspect = 16f / 10f;

        private static readonly FieldInfo CameraHeightField =
            AccessTools.Field(typeof(CameraControl), "height");
        private static readonly FieldInfo CameraWidthField =
            AccessTools.Field(typeof(CameraControl), "width");
        private static readonly FieldInfo CameraHalfXField =
            AccessTools.Field(typeof(CameraControl), "halfX");
        private static readonly FieldInfo CameraHalfYField =
            AccessTools.Field(typeof(CameraControl), "halfY");

        private static WidescreenFixPlugin _instance;

        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _maximumAspect;
        private ConfigEntry<bool> _startInClassicMode;
        private ConfigEntry<KeyboardShortcut> _toggleClassicMode;
        private bool _classicMode;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        internal static bool ShouldOverrideForcedRatio =>
            _instance != null &&
            _instance._enabled.Value &&
            string.Equals(SceneManager.GetActiveScene().name, GameplayScene, StringComparison.Ordinal);

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable Hor+ widescreen rendering in the gameplay scene.");
            _maximumAspect = Config.Bind(
                "General",
                "MaximumAspectRatio",
                3.2f,
                new ConfigDescription(
                    "Maximum rendered aspect ratio. Wider displays retain small side bars to avoid exposing space outside 40-unit maps.",
                    new AcceptableValueRange<float>(OriginalAspect, 4f)));
            _startInClassicMode = Config.Bind(
                "Classic Mode",
                "StartInClassicMode",
                false,
                "Start gameplay in the original 16:10 presentation instead of Hor+ widescreen.");
            _toggleClassicMode = Config.Bind(
                "Classic Mode",
                "ToggleShortcut",
                new KeyboardShortcut(KeyCode.F10),
                "Switch between the original 16:10 presentation and Hor+ widescreen.");
            _classicMode = _startInClassicMode.Value;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(WidescreenFixPlugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(ApplyAfterSceneLoad());
            Logger.LogInfo("Widescreen fix initialized. Menus and story scenes remain at the original 16:10 ratio.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _lastScreenWidth = 0;
            _lastScreenHeight = 0;
            if (string.Equals(scene.name, GameplayScene, StringComparison.Ordinal))
            {
                StartCoroutine(ApplyAfterSceneLoad());
            }
        }

        private IEnumerator ApplyAfterSceneLoad()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            ApplyGameplayViewport();
        }

        private void Update()
        {
            if (_enabled.Value && _toggleClassicMode.Value.IsDown())
            {
                _classicMode = !_classicMode;
                _lastScreenWidth = 0;
                _lastScreenHeight = 0;
                Logger.LogInfo(_classicMode
                    ? "Classic Mode enabled: gameplay uses the original 16:10 presentation."
                    : "Classic Mode disabled: gameplay uses Hor+ widescreen.");

                if (ShouldOverrideForcedRatio)
                {
                    ApplyGameplayViewport();
                }
            }

            if (!ShouldOverrideForcedRatio)
            {
                return;
            }

            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                ApplyGameplayViewport();
            }
        }

        internal static void ApplyFromPatchedStart()
        {
            if (ShouldOverrideForcedRatio)
            {
                _instance.ApplyGameplayViewport();
            }
        }

        private void ApplyGameplayViewport()
        {
            if (!ShouldOverrideForcedRatio || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            float screenAspect = (float)Screen.width / Screen.height;
            float safeMaximumAspect = CalculateSafeMaximumAspect(scene);
            float renderedAspect = _classicMode
                ? OriginalAspect
                : Mathf.Min(screenAspect, safeMaximumAspect);
            Rect viewport = CalculateCenteredViewport(screenAspect, renderedAspect);

            Camera[] cameras = FindObjectsOfType<Camera>();
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera.gameObject.scene != scene ||
                    string.Equals(camera.name, "Letter Box Camera", StringComparison.Ordinal))
                {
                    continue;
                }
                camera.rect = viewport;
            }

            ForceCameraRatio[] ratioControllers = FindObjectsOfType<ForceCameraRatio>();
            foreach (ForceCameraRatio controller in ratioControllers)
            {
                if (controller == null || controller.gameObject.scene != scene)
                {
                    continue;
                }

                controller.forceRatioOnAwake = false;
                controller.listenForWindowChanges = false;
                if (controller.letterBoxCamera != null)
                {
                    controller.letterBoxCamera.rect = new Rect(0f, 0f, 1f, 1f);
                    controller.letterBoxCamera.enabled =
                        viewport.width < 0.9999f || viewport.height < 0.9999f;
                }
            }

            foreach (CameraControl cameraControl in FindObjectsOfType<CameraControl>())
            {
                if (cameraControl != null && cameraControl.gameObject.scene == scene)
                {
                    RefreshCameraBounds(cameraControl);
                }
            }

            Canvas.ForceUpdateCanvases();
            ResizeCompletionBorder(scene);

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            Logger.LogInfo(
                $"Gameplay viewport: {Screen.width}x{Screen.height}, " +
                $"screen aspect {screenAspect:F3}, rendered aspect {renderedAspect:F3}, " +
                $"mode {(_classicMode ? "Classic" : "Widescreen")}.");
        }

        private static Rect CalculateCenteredViewport(float screenAspect, float targetAspect)
        {
            if (screenAspect > targetAspect)
            {
                float width = Mathf.Clamp01(targetAspect / screenAspect);
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }

            float height = Mathf.Clamp01(screenAspect / targetAspect);
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        private float CalculateSafeMaximumAspect(Scene scene)
        {
            float maximum = Mathf.Clamp(_maximumAspect.Value, OriginalAspect, 4f);
            foreach (CameraControl cameraControl in FindObjectsOfType<CameraControl>())
            {
                if (cameraControl == null || cameraControl.gameObject.scene != scene || !cameraControl.useLimit)
                {
                    continue;
                }

                Camera camera = cameraControl.GetComponent<Camera>();
                if (camera == null || !camera.orthographic || camera.orthographicSize <= 0f ||
                    cameraControl.limit.width <= 0f)
                {
                    continue;
                }

                float mapAspect = cameraControl.limit.width / (camera.orthographicSize * 2f);
                maximum = Mathf.Min(maximum, Mathf.Max(OriginalAspect, mapAspect));
            }
            return maximum;
        }

        private void ResizeCompletionBorder(Scene scene)
        {
            const float borderThickness = 4f;
            RectTransform border = null;

            foreach (RectTransform candidate in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (candidate != null && candidate.gameObject.scene == scene &&
                    string.Equals(candidate.name, "Border", StringComparison.Ordinal))
                {
                    border = candidate;
                    break;
                }
            }

            RectTransform canvas = border != null ? border.parent as RectTransform : null;
            RectTransform blinkNode = border != null ? FindChild(border, "Blink Node") : null;
            if (canvas == null || blinkNode == null || canvas.rect.width <= 0f)
            {
                return;
            }

            RectTransform left = FindChild(blinkNode, "left");
            RectTransform right = FindChild(blinkNode, "right");
            RectTransform top = FindChild(blinkNode, "up");
            float sidePosition = Mathf.Max(0f, (canvas.rect.width - borderThickness) * 0.5f);

            if (left != null)
            {
                Vector2 position = left.anchoredPosition;
                position.x = -sidePosition;
                left.anchoredPosition = position;
            }

            if (right != null)
            {
                Vector2 position = right.anchoredPosition;
                position.x = sidePosition;
                right.anchoredPosition = position;
            }

            if (top != null)
            {
                Vector2 position = top.anchoredPosition;
                position.x = 0f;
                top.anchoredPosition = position;

                Vector2 size = top.sizeDelta;
                size.x = Mathf.Max(0f, canvas.rect.width - borderThickness);
                top.sizeDelta = size;
            }

            Logger.LogInfo($"Completion border width adjusted to {canvas.rect.width:F1} UI units.");
        }

        private static RectTransform FindChild(RectTransform parent, string childName)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                RectTransform child = parent.GetChild(index) as RectTransform;
                if (child == null)
                {
                    continue;
                }

                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                RectTransform descendant = FindChild(child, childName);
                if (descendant != null)
                {
                    return descendant;
                }
            }
            return null;
        }

        internal static void RefreshCameraBounds(CameraControl cameraControl)
        {
            Camera camera = cameraControl != null ? cameraControl.GetComponent<Camera>() : null;
            if (camera == null || !camera.orthographic)
            {
                return;
            }

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            CameraHeightField?.SetValue(cameraControl, halfHeight);
            CameraWidthField?.SetValue(cameraControl, halfWidth);
            CameraHalfXField?.SetValue(cameraControl, cameraControl.limit.width * 0.5f);
            CameraHalfYField?.SetValue(cameraControl, cameraControl.limit.height * 0.5f);
        }
    }

    [HarmonyPatch(typeof(ForceCameraRatio), nameof(ForceCameraRatio.CalculateAndSetAllCameraRatios))]
    internal static class ForcedRatioPatch
    {
        private static bool Prefix()
        {
            return !WidescreenFixPlugin.ShouldOverrideForcedRatio;
        }
    }

    [HarmonyPatch(typeof(ForceCameraRatio), "Start")]
    internal static class ForcedRatioStartPatch
    {
        private static void Postfix()
        {
            WidescreenFixPlugin.ApplyFromPatchedStart();
        }
    }

    [HarmonyPatch(typeof(CameraControl), "Start")]
    internal static class CameraControlStartPatch
    {
        private static void Postfix(CameraControl __instance)
        {
            if (WidescreenFixPlugin.ShouldOverrideForcedRatio)
            {
                WidescreenFixPlugin.RefreshCameraBounds(__instance);
            }
        }
    }
}
