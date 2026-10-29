using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace CrystalCavesHD.CompletionTracker
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class CompletionTrackerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.crystalcaveshd.completiontracker";
        public const string PluginName = "Crystal Caves HD Completion Tracker";
        public const string PluginVersion = "0.1.1";

        private const int LevelsPerEpisode = 16;

        private static readonly BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo AllCrystalsField =
            typeof(Level).GetField("allCrystals", PrivateInstance);
        private static readonly FieldInfo CollectedCrystalsField =
            typeof(Level).GetField("crystalsCollected", PrivateInstance);
        private static readonly FieldInfo AllHiddenCrystalsField =
            typeof(Level).GetField("allHiddenCrystals", PrivateInstance);
        private static readonly FieldInfo CollectedHiddenCrystalsField =
            typeof(Level).GetField("secretCrystalsCollected", PrivateInstance);
        private static readonly MethodInfo EpisodeCrystalTotalsMethod =
            typeof(CoreMethods).GetMethod(
                "GetAllCrystalsInEpisode",
                BindingFlags.Static | BindingFlags.NonPublic);

        private readonly Dictionary<string, string> _translations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly int[] _episodeCrystals = new int[4];
        private readonly int[] _episodeHiddenCrystals = new int[4];
        private readonly bool[] _episodeTotalsLoaded = new bool[4];

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _visibleAtStartup;
        private ConfigEntry<KeyboardShortcut> _toggleShortcut;
        private ConfigEntry<KeyboardShortcut> _nextAchievementPageShortcut;
        private ConfigEntry<int> _achievementsToList;
        private ConfigEntry<float> _panelWidth;
        private ConfigEntry<float> _panelTop;

        private bool _visible;
        private int _achievementPage;
        private Font _font;
        private Texture2D _panelBackground;
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _hintStyle;

        private void Awake()
        {
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable the completion tracker.");
            _visibleAtStartup = Config.Bind(
                "General",
                "VisibleAtStartup",
                true,
                "Show the tracker automatically when a level is loaded.");
            _toggleShortcut = Config.Bind(
                "Controls",
                "ToggleShortcut",
                new KeyboardShortcut(KeyCode.F9),
                "Show or hide the completion tracker.");
            _nextAchievementPageShortcut = Config.Bind(
                "Controls",
                "NextAchievementPageShortcut",
                new KeyboardShortcut(KeyCode.F8),
                "Show the next page of incomplete achievements.");
            _achievementsToList = Config.Bind(
                "Display",
                "AchievementsToList",
                5,
                new ConfigDescription(
                    "Maximum number of incomplete achievements shown in the panel.",
                    new AcceptableValueRange<int>(0, 12)));
            _panelWidth = Config.Bind(
                "Display",
                "PanelWidth",
                460f,
                new ConfigDescription(
                    "Tracker panel width in screen pixels.",
                    new AcceptableValueRange<float>(340f, 700f)));
            _panelTop = Config.Bind(
                "Display",
                "PanelTop",
                130f,
                new ConfigDescription(
                    "Distance between the panel and the top edge of the screen.",
                    new AcceptableValueRange<float>(10f, 400f)));

            _visible = _visibleAtStartup.Value;
            LoadTranslations();
            Logger.LogInfo("Completion Tracker initialized. Press F9 to show or hide it.");
        }

        private void Update()
        {
            if (_enabled.Value && _toggleShortcut.Value.IsDown())
            {
                _visible = !_visible;
                Logger.LogInfo(_visible ? "Completion Tracker shown." : "Completion Tracker hidden.");
            }
            if (_enabled.Value && _visible && _nextAchievementPageShortcut.Value.IsDown())
            {
                _achievementPage++;
            }
        }

        private void OnGUI()
        {
            if (!_enabled.Value || !_visible || Level.instance == null)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            Color previousBackgroundColor = GUI.backgroundColor;
            int previousDepth = GUI.depth;

            try
            {
                // Crystal Caves scales its own legacy IMGUI.  Reset that matrix so
                // the tracker dimensions remain true screen pixels at every resolution.
                GUI.matrix = Matrix4x4.identity;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.backgroundColor = Color.white;

                EnsureStyles();
                if (_panelStyle == null)
                {
                    return;
                }

                float width = Mathf.Min(_panelWidth.Value, Screen.width - 24f);
                float top = Mathf.Min(_panelTop.Value, Mathf.Max(10f, Screen.height - 560f));
                float height = Mathf.Min(560f, Screen.height - top - 16f);
                Rect area = new Rect(Screen.width - width - 16f, top, width, height);

                GUI.depth = -5000;
                GUILayout.BeginArea(area, _panelStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Прогресс", _titleStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label("F8 — дальше   F9 — скрыть", _hintStyle);
                GUILayout.EndHorizontal();

                DrawCurrentLevel();

                if (!Loading.customLevel && Loading.currentEpisodeId >= 0 && Loading.currentEpisodeId < 4)
                {
                    DrawEpisode(Loading.currentEpisodeId);
                }

                DrawAchievements();
                GUILayout.EndArea();
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.contentColor = previousContentColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.depth = previousDepth;
            }
        }

        private void DrawCurrentLevel()
        {
            int total = ReadLevelValue(AllCrystalsField);
            int collected = ReadLevelValue(CollectedCrystalsField);
            int hiddenTotal = ReadLevelValue(AllHiddenCrystalsField);
            int hiddenCollected = ReadLevelValue(CollectedHiddenCrystalsField);

            GUILayout.Space(8f);
            GUILayout.Label(
                Loading.currentGameState == GameState.overworld ? "Карта эпизода" : "Текущий уровень",
                _sectionStyle);

            if (Loading.currentGameState == GameState.overworld)
            {
                GUILayout.Label($"Эпизод: {Loading.currentEpisodeId + 1}", _labelStyle);
                return;
            }

            if (Loading.customLevel)
            {
                string levelName = Loading.customLevelMeta != null &&
                    !string.IsNullOrWhiteSpace(Loading.customLevelMeta.name)
                        ? Loading.customLevelMeta.name
                        : "пользовательский уровень";
                GUILayout.Label($"Карта: {levelName}", _labelStyle);
            }
            else if (Loading.currentMapId >= 0)
            {
                GUILayout.Label(
                    $"Эпизод {Loading.currentEpisodeId + 1}, уровень {Loading.currentMapId + 1}",
                    _labelStyle);
            }

            GUILayout.Label($"Кристаллы: {collected}/{total}", _labelStyle);
            GUILayout.Label($"Тайные кристаллы: {hiddenCollected}/{hiddenTotal}", _labelStyle);
        }

        private void DrawEpisode(int episodeId)
        {
            EpisodeProfile profile = ProfileManager.activeProfile;
            if (profile == null)
            {
                return;
            }

            EnsureEpisodeTotals(episodeId);
            List<int> unfinished = new List<int>();
            for (int level = 0; level < LevelsPerEpisode; level++)
            {
                if (!profile.IsLevelFinished(level))
                {
                    unfinished.Add(level + 1);
                }
            }

            int savedCrystals = ProfileManager.playerProfile != null
                ? ProfileManager.playerProfile.mostCrystalsCollected[episodeId]
                : profile.GetAllCrystalsCollected();
            int savedHidden = ProfileManager.playerProfile != null
                ? ProfileManager.playerProfile.mostSecretCrystalsCollected[episodeId]
                : profile.GetSecretCrystalsFound();

            GUILayout.Space(10f);
            GUILayout.Label($"Эпизод {episodeId + 1}", _sectionStyle);
            GUILayout.Label(
                $"Пройдено уровней: {profile.GetLevelsCompleted()}/{LevelsPerEpisode}",
                _labelStyle);
            GUILayout.Label(
                $"Кристаллы: {savedCrystals}/{_episodeCrystals[episodeId]}",
                _labelStyle);
            GUILayout.Label(
                $"Тайные кристаллы: {savedHidden}/{_episodeHiddenCrystals[episodeId]}",
                _labelStyle);
            GUILayout.Label(
                unfinished.Count == 0
                    ? "Все основные уровни пройдены."
                    : $"Не пройдены: {string.Join(", ", unfinished)}",
                _smallStyle);
        }

        private void DrawAchievements()
        {
            AchievementsBank bank = Achievements.GetBank();
            AchievementsProfile profile = ProfileManager.achievementsProfile;
            if (bank == null || bank.achievements == null || profile == null)
            {
                return;
            }

            List<AchievementInfo> incomplete = bank.achievements
                .Where(info => info != null && !IsAchievementComplete(profile, info.id))
                .ToList();
            int completed = bank.achievements.Count(info =>
                info != null && IsAchievementComplete(profile, info.id));
            int total = bank.achievements.Count(info => info != null);

            GUILayout.Space(10f);
            GUILayout.Label("Достижения", _sectionStyle);
            GUILayout.Label($"Получено: {completed}/{total}", _labelStyle);

            int count = Mathf.Min(_achievementsToList.Value, incomplete.Count);
            if (count == 0)
            {
                if (incomplete.Count == 0)
                {
                    GUILayout.Label("Получены все достижения.", _smallStyle);
                }
                return;
            }

            int pageCount = Mathf.Max(1, Mathf.CeilToInt((float)incomplete.Count / count));
            _achievementPage %= pageCount;
            int startIndex = _achievementPage * count;
            int endIndex = Mathf.Min(startIndex + count, incomplete.Count);
            GUILayout.Label(
                pageCount > 1
                    ? $"Неполученные — страница {_achievementPage + 1}/{pageCount}:"
                    : "Неполученные:",
                _smallStyle);
            for (int index = startIndex; index < endIndex; index++)
            {
                AchievementInfo info = incomplete[index];
                AchievementData data = FindAchievementData(profile, info.id);
                string progress = info.statisticGoal > 1 && data != null
                    ? $" — {data.counter}/{info.statisticGoal}"
                    : string.Empty;
                GUILayout.Label($"• {Translate(info.name)}{progress}", _smallStyle);
            }
        }

        private void EnsureEpisodeTotals(int episodeId)
        {
            if (_episodeTotalsLoaded[episodeId] || EpisodeCrystalTotalsMethod == null)
            {
                return;
            }

            try
            {
                object[] arguments = { episodeId, 0, 0 };
                EpisodeCrystalTotalsMethod.Invoke(null, arguments);
                _episodeCrystals[episodeId] = (int)arguments[1];
                _episodeHiddenCrystals[episodeId] = (int)arguments[2];
                _episodeTotalsLoaded[episodeId] = true;
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not calculate episode crystal totals: {exception.Message}");
            }
        }

        private static int ReadLevelValue(FieldInfo field)
        {
            return field != null && Level.instance != null
                ? (int)field.GetValue(Level.instance)
                : 0;
        }

        private static AchievementData FindAchievementData(
            AchievementsProfile profile,
            string achievementId)
        {
            if (profile?.achievements == null)
            {
                return null;
            }

            return profile.achievements.FirstOrDefault(data =>
                data != null && string.Equals(data.uid, achievementId, StringComparison.Ordinal));
        }

        private static bool IsAchievementComplete(
            AchievementsProfile profile,
            string achievementId)
        {
            return FindAchievementData(profile, achievementId)?.achieved ?? false;
        }

        private void LoadTranslations()
        {
            try
            {
                string directory = Path.GetDirectoryName(Info.Location);
                string path = Path.Combine(directory ?? string.Empty, "achievements.tsv");
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('\t');
                    if (separator <= 0 || separator >= line.Length - 1)
                    {
                        continue;
                    }

                    _translations[Unescape(line.Substring(0, separator))] =
                        Unescape(line.Substring(separator + 1));
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"Could not load achievement translations: {exception.Message}");
            }
        }

        private string Translate(string value)
        {
            return value != null && _translations.TryGetValue(value, out string translated)
                ? translated
                : value ?? string.Empty;
        }

        private static string Unescape(string value)
        {
            return value
                .Replace("\\t", "\t")
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\\\", "\\");
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Russo One", "Segoe UI", "Arial", "Tahoma" },
                20);

            _panelBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Completion Tracker Background",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _panelBackground.SetPixel(0, 0, new Color(0.025f, 0.035f, 0.06f, 0.92f));
            _panelBackground.Apply(false, true);

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(18, 18, 14, 16),
                normal = { background = _panelBackground },
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                font = _font,
                fontSize = 20,
                wordWrap = true,
                normal = { textColor = Color.white },
            };
            _titleStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 25,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.78f, 0.92f, 0.35f) },
            };
            _sectionStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.78f, 0.3f) },
            };
            _smallStyle = new GUIStyle(_labelStyle)
            {
                fontSize = 17,
                normal = { textColor = new Color(0.9f, 0.92f, 0.96f) },
            };
            _hintStyle = new GUIStyle(_smallStyle)
            {
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(0.65f, 0.7f, 0.8f) },
            };
        }

        private void OnDestroy()
        {
            if (_panelBackground != null)
            {
                Destroy(_panelBackground);
            }
            if (_font != null)
            {
                Destroy(_font);
            }
        }
    }
}
