using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace CrystalCavesHD.Russian
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RussianLocalizationPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.crystalcaveshd.russian";
        public const string PluginName = "Crystal Caves HD — русский перевод";
        public const string PluginVersion = "0.2.11";

        private static readonly Regex HasCyrillicText =
            new Regex("[А-Яа-яЁё]", RegexOptions.Compiled);

        private Harmony _harmony;
        private string _pluginDirectory;

        private void Awake()
        {
            _pluginDirectory = Path.GetDirectoryName(Info.Location);
            TranslationService.Initialize(Logger, _pluginDirectory);
            FontService.Initialize(Logger, _pluginDirectory);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(RussianLocalizationPlugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;

            StartCoroutine(TranslateAtEndOfFrame());
            StartCoroutine(RetryFontInitialization());
            StartCoroutine(RunOptInDiagnostics());
            Logger.LogInfo($"Loaded {TranslationService.Count} Russian translations.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name}");
            StartCoroutine(TranslateAtEndOfFrame());
        }

        private IEnumerator TranslateAtEndOfFrame()
        {
            yield return null;
            foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                TranslateLabel(label);
            }

            foreach (Text label in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (label != null)
                {
                    label.text = TranslationService.Translate(label.text);
                }
            }

        }

        private IEnumerator RetryFontInitialization()
        {
            for (int attempt = 0; attempt < 10 && !FontService.IsReady; attempt++)
            {
                yield return new WaitForSecondsRealtime(1f);
                foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    TranslateLabel(label);
                }
            }
        }

        private IEnumerator RunOptInDiagnostics()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            bool capture = arguments.Any(argument =>
                string.Equals(argument, "-cchd-ru-test-capture", StringComparison.OrdinalIgnoreCase));
            int testScene = -1;
            const string scenePrefix = "-cchd-ru-test-scene=";
            foreach (string argument in arguments)
            {
                if (argument.StartsWith(scenePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(argument.Substring(scenePrefix.Length), out testScene);
                }
            }

            if (!capture && testScene < 0)
            {
                yield break;
            }

            yield return new WaitForSecondsRealtime(5f);
            if (capture)
            {
                string path = Path.Combine(_pluginDirectory, "runtime-main-menu.png");
                ScreenCapture.CaptureScreenshot(path);
                Logger.LogInfo($"Diagnostic screenshot requested: {path}");
                WriteDiagnosticLabelReport("startup");
                yield return new WaitForSecondsRealtime(2f);
            }

            if (testScene >= 0)
            {
                Logger.LogInfo($"Diagnostic scene load requested: {testScene}");
                SceneManager.LoadScene(testScene);
                yield return new WaitForSecondsRealtime(5f);
                WriteDiagnosticLabelReport($"scene-{testScene}");
                if (capture)
                {
                    string path = Path.Combine(_pluginDirectory, $"runtime-scene-{testScene}.png");
                    ScreenCapture.CaptureScreenshot(path);
                    Logger.LogInfo($"Diagnostic screenshot requested: {path}");
                }
            }
        }

        private void WriteDiagnosticLabelReport(string suffix)
        {
            string path = Path.Combine(_pluginDirectory, $"runtime-{suffix}-labels.tsv");
            var output = new StringBuilder(
                "component\tobject\ttext\tfont\trussian_fallback\tfont_size\trect_width\trect_height\t" +
                "preferred_width\tpreferred_height\tline_count\r\n");
            int count = 0;
            foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (label == null || label.gameObject == null || !label.gameObject.scene.isLoaded ||
                    string.IsNullOrWhiteSpace(label.text))
                {
                    continue;
                }

                try
                {
                string text = label.text
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
                string objectName = label.gameObject.name.Replace("\t", " ");
                string fontName = label.font != null ? label.font.name.Replace("\t", " ") : "";
                RectTransform rectTransform = label.rectTransform;
                Rect rect = rectTransform != null ? rectTransform.rect : default(Rect);
                int lineCount = label.textInfo != null ? label.textInfo.lineCount : 0;
                output.Append(label.GetType().Name).Append('\t')
                    .Append(objectName).Append('\t')
                    .Append(text).Append('\t')
                    .Append(fontName).Append('\t')
                    .Append(FontService.HasRussianFallback(label) ? "yes" : "no")
                    .Append('\t').Append(label.fontSize.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\t').Append(rect.width.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\t').Append(rect.height.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\t').Append(label.preferredWidth.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\t').Append(label.preferredHeight.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append('\t').Append(lineCount)
                    .Append("\r\n");
                count++;
                }
                catch (Exception exception)
                {
                    Logger.LogDebug($"Diagnostic label skipped: {exception.Message}");
                }
            }
            File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
            Logger.LogInfo($"Diagnostic label report written ({count} labels): {path}");
        }

        internal static void TranslateLabel(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            FontService.AttachFallback(label);
            string translated = TranslationService.Translate(label.text);
            if (!string.Equals(translated, label.text, StringComparison.Ordinal))
            {
                label.text = translated;
            }

            NormalizeLocalizedLabel(label);
        }

        internal static void NormalizeLocalizedLabel(TMP_Text label)
        {
            if (label == null)
            {
                return;
            }

            string objectName = label.gameObject != null ? label.gameObject.name : string.Empty;
            string compactText = null;
            switch (objectName)
            {
                case "lblTitle": compactText = "Главное меню"; break;
                case "lblMainLevel": compactText = "Карта"; break;
                case "lblQuitGame":
                    // The same object name is used both by the pause-menu command
                    // and by the actual shutdown option in the confirmation window.
                    compactText = string.Equals(label.text, "Выход", StringComparison.Ordinal)
                        ? "Выход"
                        : "Выйти из игры";
                    break;
            }

            if (compactText != null)
            {
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                if (!string.Equals(compactText, "Выход", StringComparison.Ordinal))
                {
                    // All three destination choices use the same visual size.
                    label.fontSize = 28f;
                }
                if (!string.Equals(label.text, compactText, StringComparison.Ordinal))
                {
                    label.text = compactText;
                }
            }

            if (string.Equals(objectName, "LabelMain", StringComparison.Ordinal) &&
                label.rectTransform != null &&
                label.rectTransform.rect.height >= 400f &&
                !string.IsNullOrEmpty(label.text) &&
                HasCyrillicText.IsMatch(label.text))
            {
                // The large Episode 4 story panels start flush against their upper
                // edge in the original layout. Give every large page a safe inset.
                Vector4 margins = label.margin;
                margins.y = Math.Max(margins.y, 20f);
                label.margin = margins;
            }

            if (string.Equals(objectName, "Title", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(label.text) &&
                label.text.StartsWith("Бортовой журнал", StringComparison.Ordinal))
            {
                // Russian log headings are substantially wider than the originals.
                // Keep all five stardate headings on one line inside the black strip.
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                label.fontSize = 30f;
            }

            if (label.gameObject != null &&
                string.Equals(label.gameObject.scene.name, "MainMenu", StringComparison.Ordinal))
            {
                switch (objectName)
                {
                    case "lblNewGame":
                    case "lblLoadGame":
                    case "lblCustomLevels":
                    case "lblSteamWorkshop":
                    case "lblOptions":
                    case "lblAchievements":
                    case "lblHighscores":
                    case "lblStatistics":
                    case "lblEditor":
                    case "lblQuit":
                        // Keep the complete main-menu list visually uniform while
                        // adding a little breathing room above and below it.
                        label.enableWordWrapping = false;
                        label.fontSize = 32f;
                        break;
                }
            }

            if (string.Equals(objectName, "SelectorLabel", StringComparison.Ordinal) &&
                string.Equals(label.text, "По умолчанию", StringComparison.Ordinal))
            {
                // The Russian default-theme value is wider than the selector slot
                // between its two arrow sprites.
                label.enableWordWrapping = false;
                label.fontSize = 32f;
            }

            if (!string.IsNullOrEmpty(label.text) &&
                (label.fontStyle & FontStyles.SmallCaps) != 0 &&
                HasCyrillicText.IsMatch(label.text))
            {
                // TMP 2.x corrupts Cyrillic fallback glyph placement whenever Small Caps
                // is enabled. Remove only that bit and preserve Bold and every other style.
                label.fontStyle &= ~FontStyles.SmallCaps;
            }
        }
    }

    internal static class TranslationService
    {
        private static readonly Dictionary<string, string> Translations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LooseTranslations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Missing =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly object LogLock = new object();
        private static readonly Regex HasLatin = new Regex("[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex HasCyrillic = new Regex("[А-Яа-яЁё]", RegexOptions.Compiled);
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex NonPlayerDiagnostic = new Regex(
            "^(?:v\\d|Steam - |Status: HasAppLicense)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FruitBonus = new Regex(
            "^Congratulations, all fruit picked!\\nExtra (.+)$",
            RegexOptions.Compiled);
        private static readonly Regex PerfectHealth = new Regex(
            "^Perfect health! (.+)$",
            RegexOptions.Compiled);
        private static readonly Regex MaximumReached = new Regex(
            "^Max number of '(.+)' reached \\((.+)\\)!$",
            RegexOptions.Compiled);
        private static readonly Regex EntryCount = new Regex(
            "^Entries: (\\d+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PageNumber = new Regex(
            "^<?\\s*Page:?\\s*(\\d+(?:/\\d+)?)\\s*>?$",
            RegexOptions.Compiled);
        private static readonly Regex AutosaveEpisode = new Regex(
            "^Autosave - Episode (\\d+)$",
            RegexOptions.Compiled);
        private static readonly Regex AutosaveDate = new Regex(
            "^Autosave (.+)$",
            RegexOptions.Compiled);
        private static readonly Regex EditorCategory = new Regex(
            "^(Terrain|Grid|Tiles|Pickups|Enemies|Objects|Navigation)( \\(\\d+\\))?$",
            RegexOptions.Compiled);

        private static ManualLogSource _logger;
        private static string _missingPath;

        internal static int Count => Translations.Count;

        internal static void Initialize(ManualLogSource logger, string pluginDirectory)
        {
            _logger = logger;
            string translationPath = Path.Combine(pluginDirectory, "translations.tsv");
            _missingPath = Path.Combine(pluginDirectory, "missing-strings.tsv");

            if (!File.Exists(translationPath))
            {
                logger.LogWarning($"Translation dictionary not found: {translationPath}");
                return;
            }

            int lineNumber = 0;
            foreach (string rawLine in File.ReadAllLines(translationPath, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#"))
                {
                    continue;
                }

                int separator = rawLine.IndexOf('\t');
                if (separator < 0)
                {
                    logger.LogWarning($"Invalid translation line {lineNumber}: tab separator is missing.");
                    continue;
                }

                string source = Unescape(rawLine.Substring(0, separator));
                string target = Unescape(rawLine.Substring(separator + 1));
                if (source.Length == 0 || target.Length == 0)
                {
                    continue;
                }

                string normalizedSource = Normalize(source);
                Translations[normalizedSource] = target;
                string looseSource = LooseNormalize(normalizedSource);
                if (!LooseTranslations.ContainsKey(looseSource))
                {
                    LooseTranslations[looseSource] = target;
                }
            }
        }

        internal static string Translate(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string normalized = Normalize(input);
            if (Translations.TryGetValue(normalized, out string translated))
            {
                return translated;
            }

            string trimmed = normalized.Trim();
            if (!ReferenceEquals(trimmed, normalized) &&
                Translations.TryGetValue(trimmed, out translated))
            {
                int start = 0;
                while (start < normalized.Length && char.IsWhiteSpace(normalized[start]))
                {
                    start++;
                }

                int end = normalized.Length;
                while (end > start && char.IsWhiteSpace(normalized[end - 1]))
                {
                    end--;
                }

                return normalized.Substring(0, start) + translated + normalized.Substring(end);
            }

            if (LooseTranslations.TryGetValue(LooseNormalize(normalized), out translated))
            {
                return translated;
            }

            Match match = FruitBonus.Match(normalized);
            if (match.Success)
            {
                return "Поздравляем, собраны все фрукты!\nБонус " + match.Groups[1].Value;
            }

            match = PerfectHealth.Match(normalized);
            if (match.Success)
            {
                return "Идеальное здоровье! " + match.Groups[1].Value;
            }

            match = MaximumReached.Match(normalized);
            if (match.Success)
            {
                return "Достигнут максимум «" + Translate(match.Groups[1].Value) + "» (" +
                       match.Groups[2].Value + ")";
            }

            match = EntryCount.Match(normalized);
            if (match.Success)
            {
                return "ЗАПИСЕЙ: " + match.Groups[1].Value;
            }

            match = PageNumber.Match(normalized);
            if (match.Success)
            {
                return "Страница " + match.Groups[1].Value;
            }

            match = AutosaveEpisode.Match(normalized);
            if (match.Success)
            {
                return "Автосохранение — эпизод " + match.Groups[1].Value;
            }

            match = AutosaveDate.Match(normalized);
            if (match.Success)
            {
                return "Автосохранение: " + match.Groups[1].Value;
            }

            match = EditorCategory.Match(normalized);
            if (match.Success)
            {
                string category = Translate(match.Groups[1].Value);
                return category + match.Groups[2].Value;
            }

            RecordMissing(normalized);
            return input;
        }

        private static string Normalize(string value)
        {
            string normalized = value
                .Replace("\u200B", string.Empty)
                .Replace("\u0013", "-")
                .Replace("\u0019", "'")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd(' ', '\t');
            }
            normalized = string.Join("\n", lines).Trim();
            return Regex.Replace(normalized, "\\n{3,}", "\n\n");
        }

        private static string LooseNormalize(string value)
        {
            return Regex.Replace(Normalize(value), "\\s+", " ").Trim();
        }

        private static string Unescape(string value)
        {
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    result.Append(value[i]);
                    continue;
                }

                char escaped = value[++i];
                switch (escaped)
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case '\\': result.Append('\\'); break;
                    default:
                        result.Append('\\');
                        result.Append(escaped);
                        break;
                }
            }
            return result.ToString();
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void RecordMissing(string value)
        {
            string plain = RichTextTag.Replace(value, string.Empty);
            if (!HasLatin.IsMatch(plain) || HasCyrillic.IsMatch(plain) ||
                NonPlayerDiagnostic.IsMatch(plain) || value.Length > 2000)
            {
                return;
            }

            lock (LogLock)
            {
                if (!Missing.Add(value))
                {
                    return;
                }

                try
                {
                    File.AppendAllText(_missingPath, Escape(value) + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception exception)
                {
                    _logger?.LogDebug(exception);
                }
            }
        }
    }

    internal static class FontService
    {
        private const string RussianCharacters =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
            "абвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
            "«»—–…№×’“”";

        private static TMP_FontAsset _fallback;
        private static ManualLogSource _logger;
        private static int _nextRetryFrame;
        private static string _fontPath;

        internal static bool IsReady => _fallback != null;

        internal static void Initialize(ManualLogSource logger, string pluginDirectory)
        {
            _logger = logger;
            _fontPath = Path.Combine(pluginDirectory, "fonts", "RussoOne-Regular.ttf");
        }

        internal static void AttachFallback(TMP_Text label)
        {
            TMP_FontAsset fallback = GetFallback();
            if (fallback == null)
            {
                return;
            }

            if (label.font == null)
            {
                label.font = fallback;
                return;
            }

            List<TMP_FontAsset> fallbacks = label.font.fallbackFontAssetTable;
            if (fallbacks == null)
            {
                fallbacks = new List<TMP_FontAsset>();
                label.font.fallbackFontAssetTable = fallbacks;
            }

            if (!fallbacks.Contains(fallback))
            {
                fallbacks.Add(fallback);
            }
        }

        internal static bool HasRussianFallback(TMP_Text label)
        {
            return _fallback != null && label != null &&
                (label.font == _fallback ||
                 (label.font != null && label.font.fallbackFontAssetTable != null &&
                  label.font.fallbackFontAssetTable.Contains(_fallback)));
        }

        private static TMP_FontAsset GetFallback()
        {
            if (_fallback != null)
            {
                return _fallback;
            }

            // FontEngine cannot build a TMP atlas under Unity's -nographics device.
            if (string.Equals(SystemInfo.graphicsDeviceType.ToString(), "Null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (Time.frameCount < _nextRetryFrame)
            {
                return null;
            }

            try
            {
                if (!File.Exists(_fontPath))
                {
                    _logger?.LogError($"Bundled Cyrillic font not found: {_fontPath}");
                    return null;
                }

                _fallback = CreateStaticFontAsset(_fontPath);
                if (_fallback == null)
                {
                    _logger?.LogWarning("TMP could not create the Cyrillic fallback atlas yet; retrying later.");
                    _nextRetryFrame = Time.frameCount + 60;
                    return null;
                }
                _fallback.name = "Crystal Caves HD Russian Fallback";
                UnityEngine.Object.DontDestroyOnLoad(_fallback);
                _logger?.LogInfo("Cyrillic fallback font created successfully.");

                List<TMP_FontAsset> globalFallbacks = TMP_Settings.fallbackFontAssets;
                if (globalFallbacks != null && !globalFallbacks.Contains(_fallback))
                {
                    globalFallbacks.Add(_fallback);
                }
            }
            catch (Exception exception)
            {
                _logger?.LogError($"Failed to create Cyrillic fallback font: {exception}");
                _fallback = null;
                _nextRetryFrame = Time.frameCount + 60;
            }

            return _fallback;
        }

        private static TMP_FontAsset CreateStaticFontAsset(string fontPath)
        {
            const int pointSize = 48;
            const int padding = 5;
            const int atlasSize = 1024;
            const GlyphRenderMode renderMode = GlyphRenderMode.SDFAA;

            FontEngine.InitializeFontEngine();
            FontEngineError loadResult = FontEngine.LoadFontFace(fontPath, pointSize);
            if (loadResult != FontEngineError.Success)
            {
                _logger?.LogWarning($"FontEngine could not load {fontPath}: {loadResult}");
                return null;
            }

            var characterToGlyph = new Dictionary<uint, uint>();
            var glyphIndexes = new List<uint>();
            foreach (char character in RussianCharacters.Distinct())
            {
                uint unicode = character;
                if (!FontEngine.TryGetGlyphIndex(unicode, out uint glyphIndex) || glyphIndex == 0)
                {
                    _logger?.LogWarning($"Bundled font is missing U+{unicode:X4} ({character}).");
                    continue;
                }
                characterToGlyph[unicode] = glyphIndex;
                if (!glyphIndexes.Contains(glyphIndex))
                {
                    glyphIndexes.Add(glyphIndex);
                }
            }

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.Alpha8, false)
            {
                name = "Crystal Caves HD Russian Fallback Atlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            // A newly allocated Alpha8 texture is not guaranteed to contain zeroes.
            // Clear it explicitly so unused glyph rectangles cannot render as boxes.
            atlas.LoadRawTextureData(new byte[atlasSize * atlasSize]);
            atlas.Apply(false, false);
            var freeRects = new List<GlyphRect> { new GlyphRect(0, 0, atlasSize - 1, atlasSize - 1) };
            var usedRects = new List<GlyphRect>();

            MethodInfo addGlyphs = typeof(FontEngine)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .First(method =>
                    method.Name == "TryAddGlyphsToTexture" &&
                    method.GetParameters().Length == 8 &&
                    method.GetParameters()[0].ParameterType == typeof(List<uint>));
            object[] arguments =
            {
                glyphIndexes,
                padding,
                GlyphPackingMode.BestShortSideFit,
                freeRects,
                usedRects,
                renderMode,
                atlas,
                null,
            };
            bool rendered = (bool)addGlyphs.Invoke(null, arguments);
            Glyph[] renderedGlyphs = arguments[7] as Glyph[];
            if (!rendered || renderedGlyphs == null)
            {
                _logger?.LogWarning("FontEngine could not render the bundled Cyrillic glyphs.");
                UnityEngine.Object.Destroy(atlas);
                return null;
            }

            var glyphTable = renderedGlyphs.Where(glyph => glyph != null).ToList();
            var glyphLookup = glyphTable.ToDictionary(glyph => glyph.index);
            var characterTable = new List<TMP_Character>();
            TMP_FontAsset fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            foreach (KeyValuePair<uint, uint> pair in characterToGlyph)
            {
                if (glyphLookup.TryGetValue(pair.Value, out Glyph glyph))
                {
                    characterTable.Add(new TMP_Character(pair.Key, fontAsset, glyph));
                }
            }

            SetPrivateField(fontAsset, "m_Version", "1.1.0");
            SetPrivateField(fontAsset, "m_GlyphTable", glyphTable);
            SetPrivateField(fontAsset, "m_CharacterTable", characterTable);
            SetPrivateField(fontAsset, "m_AtlasWidth", atlasSize);
            SetPrivateField(fontAsset, "m_AtlasHeight", atlasSize);
            SetPrivateField(fontAsset, "m_AtlasPadding", padding);
            SetPrivateField(fontAsset, "m_AtlasRenderMode", renderMode);
            SetPrivateField(fontAsset, "m_FreeGlyphRects", freeRects);
            SetPrivateField(fontAsset, "m_UsedGlyphRects", usedRects);
            FaceInfo generatedFace = FontEngine.GetFaceInfo();
            // Russo One uses a 0.70-em cap height, while the game's Lalezar TMP
            // asset uses a 0.57-em cap height. Keep the rendered glyphs but
            // expose Lalezar-compatible metrics so fallback characters have the
            // same visual size, baseline and wrapping as the original Latin text.
            const int compatiblePointSize = 59;
            generatedFace.pointSize = compatiblePointSize;
            generatedFace.scale = 1f;
            generatedFace.lineHeight = compatiblePointSize * 1.567f;
            generatedFace.ascentLine = compatiblePointSize * 0.979f;
            generatedFace.capLine = compatiblePointSize * 0.57f;
            generatedFace.meanLine = compatiblePointSize * 0.46f;
            generatedFace.baseline = 0f;
            generatedFace.descentLine = compatiblePointSize * -0.588f;
            generatedFace.superscriptOffset = generatedFace.ascentLine;
            generatedFace.superscriptSize = 0.5f;
            generatedFace.subscriptOffset = generatedFace.descentLine;
            generatedFace.subscriptSize = 0.5f;
            generatedFace.underlineOffset = compatiblePointSize * -0.1f;
            generatedFace.underlineThickness = compatiblePointSize * 0.05f;
            generatedFace.strikethroughOffset = compatiblePointSize * 0.184f;
            generatedFace.strikethroughThickness = compatiblePointSize * 0.05f;
            generatedFace.tabWidth = compatiblePointSize * 0.15f;
            fontAsset.faceInfo = generatedFace;
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            fontAsset.atlasTextures = new[] { atlas };
            fontAsset.isMultiAtlasTexturesEnabled = false;

            Shader shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (shader == null)
            {
                _logger?.LogWarning("TextMeshPro distance-field shader was not found.");
                UnityEngine.Object.Destroy(atlas);
                UnityEngine.Object.Destroy(fontAsset);
                return null;
            }
            var material = new Material(shader) { name = "Crystal Caves HD Russian Fallback Material" };
            material.SetTexture(Shader.PropertyToID("_MainTex"), atlas);
            material.SetFloat(Shader.PropertyToID("_TextureWidth"), atlasSize);
            material.SetFloat(Shader.PropertyToID("_TextureHeight"), atlasSize);
            material.SetFloat(Shader.PropertyToID("_GradientScale"), padding + 1);
            material.SetFloat(Shader.PropertyToID("_WeightNormal"), fontAsset.normalStyle);
            material.SetFloat(Shader.PropertyToID("_WeightBold"), fontAsset.boldStyle);
            fontAsset.material = material;
            fontAsset.ReadFontAssetDefinition();

            UnityEngine.Object.DontDestroyOnLoad(atlas);
            UnityEngine.Object.DontDestroyOnLoad(material);
            return fontAsset;
        }

        private static void SetPrivateField(TMP_FontAsset target, string fieldName, object value)
        {
            FieldInfo field = AccessTools.Field(typeof(TMP_FontAsset), fieldName);
            if (field == null)
            {
                throw new MissingFieldException(typeof(TMP_FontAsset).FullName, fieldName);
            }
            field.SetValue(target, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
    internal static class TmpTextSetterPatch
    {
        private static void Prefix(ref string __0)
        {
            __0 = TranslationService.Translate(__0);
        }

        private static void Postfix(TMP_Text __instance)
        {
            RussianLocalizationPlugin.NormalizeLocalizedLabel(__instance);
        }
    }

    [HarmonyPatch]
    internal static class TmpSetTextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(TMP_Text)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == "SetText")
                .Where(method => method.GetParameters().Length > 0)
                .Where(method => method.GetParameters()[0].ParameterType == typeof(string));
        }

        private static void Prefix(ref string __0)
        {
            __0 = TranslationService.Translate(__0);
        }

        private static void Postfix(TMP_Text __instance)
        {
            RussianLocalizationPlugin.NormalizeLocalizedLabel(__instance);
        }
    }

    [HarmonyPatch]
    internal static class TypewriterDisplayTextPatch
    {
        private static MethodBase TargetMethod()
        {
            Type textDisplayer = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("TextDisplayer", false))
                .FirstOrDefault(type => type != null);
            return AccessTools.Method(textDisplayer, "DisplayText", new[] { typeof(string) });
        }

        private static void Prefix(ref string __0)
        {
            // Translate the complete entry before TextDisplayer reveals it letter by letter.
            __0 = TranslationService.Translate(__0);
        }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    internal static class TmpUiOnEnablePatch
    {
        private static void Postfix(TextMeshProUGUI __instance)
        {
            RussianLocalizationPlugin.TranslateLabel(__instance);
        }
    }

    [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
    internal static class TmpWorldOnEnablePatch
    {
        private static void Postfix(TextMeshPro __instance)
        {
            RussianLocalizationPlugin.TranslateLabel(__instance);
        }
    }

    [HarmonyPatch(typeof(Text), "text", MethodType.Setter)]
    internal static class LegacyTextSetterPatch
    {
        private static void Prefix(ref string __0)
        {
            __0 = TranslationService.Translate(__0);
        }
    }
}
