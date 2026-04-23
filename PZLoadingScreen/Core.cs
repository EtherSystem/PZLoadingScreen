[assembly: MelonInfo(typeof(PZLoadingScreen.Core), "PZLoadingScreen", "1.0.0", "EtherSystem", null)]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace PZLoadingScreen
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoadingIntro.ResetAll();
            Log.Msg("Initialized.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;

            string lower = sceneName.ToLowerInvariant();
            if (lower.Contains("boot") || lower.Contains("menu"))
            {
                LoadingIntro.ResetAll();
                LoadingIntro.AllowNextSequence();
                Log.Msg($"Armed intro on scene load: {sceneName}");
            }
        }
    }

    internal static class Log
    {
        private static readonly Dictionary<string, float> _lastLogTimes = new();

        internal static void Msg(string message)
        {
            MelonLogger.Msg(message);
        }

        internal static void MsgThrottled(string key, string message, float minIntervalSeconds)
        {
            float now = Time.realtimeSinceStartup;
            if (_lastLogTimes.TryGetValue(key, out float lastTime) && now - lastTime < minIntervalSeconds) return;

            _lastLogTimes[key] = now;
            MelonLogger.Msg(message);
        }
    }

    internal static class LoadingIntro
    {
        private static readonly string[] Lines =
        {
            "These are the end-times",
            "There was no hope of Survival",
            "This is how you fade into the Long Dark"
        };

        private static Panel_Loading? _panel;
        private static UILabel? _introLabel;
        private static UILabel? _continueLabel;
        private static UILabel? _nativeContinueLabel;
        private static GameObject? _continueCloneObject;

        private static int _generation;
        private static bool _running;
        private static bool _releaseRequested;
        private static bool _acceptContinueInput;
        private static bool _allowNextSequence;
        private static bool _continuePromptVisible;
        private static bool _continuePromptBlinking;

        private static bool _savedState;
        private static string _savedIntroText = string.Empty;
        private static float _savedIntroAlpha;
        private static string _savedNativeContinueText = string.Empty;
        private static float _savedNativeContinueAlpha;
        private static float _savedPanelContinueAlpha;
        private static float _savedSceneAlpha;
        private static float _savedGameModeAlpha;
        private static float _savedHintAlpha;
        private static float _savedHintTitleAlpha;
        private static Color _savedBackgroundColor;
        private static float _savedBackgroundTextureAlpha;

        internal static void AllowNextSequence()
        {
            _allowNextSequence = true;
        }

        internal static void ResetAll()
        {
            _generation++;
            RestorePanelState();
            DestroyContinueClone();

            _panel = null;
            _introLabel = null;
            _continueLabel = null;
            _nativeContinueLabel = null;
            _running = false;
            _releaseRequested = false;
            _acceptContinueInput = false;
            _allowNextSequence = false;
            _continuePromptVisible = false;
            _continuePromptBlinking = false;
        }

        internal static bool ShouldForceHold(Panel_Loading panel)
        {
            if (_panel == null || panel == null) return false;

            return ReferenceEquals(_panel, panel) && _running && !_releaseRequested;
        }

        internal static bool IsManagedPanel(Panel_Loading panel)
        {
            if (_panel == null || panel == null) return false;

            return ReferenceEquals(_panel, panel) && _running;
        }

        internal static bool IsBlockingNativeInputWindow()
        {
            if (!_running || _acceptContinueInput || _releaseRequested || _panel == null) return false;

            return SafeCall(() => _panel.HasFinishedLoading(), false);
        }

        internal static bool ShouldSuppressPanelLoadingUpdate(Panel_Loading panel)
        {
            if (_panel == null || panel == null) return false;

            return ReferenceEquals(_panel, panel) && IsBlockingNativeInputWindow();
        }

        internal static void LogBlockedEarlyInputIfAny()
        {
            if (!IsBlockingNativeInputWindow()) return;
            if (!IsAnyInputDown()) return;

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Log.MsgThrottled("BlockedEarlyInput", $"Blocked early input in scene {sceneName}", 0.20f);
        }

        internal static void TryBegin(Panel_Loading panel)
        {
            if (panel == null) return;
            if (_running) return;
            if (panel.m_LoadingMainMenu) return;
            if (!_allowNextSequence) return;

            _allowNextSequence = false;
            _panel = panel;
            _running = true;
            _releaseRequested = false;
            _acceptContinueInput = false;
            _continuePromptVisible = false;
            _continuePromptBlinking = false;
            _generation++;

            ForceHoldState(panel);
            PreparePanelState(panel);

            Log.Msg("Intro sequence started.");
            MelonCoroutines.Start(RunSequence(_generation, panel));
        }

        internal static void NotifyPanelDisabled(Panel_Loading panel)
        {
            if (_panel == null || panel == null) return;
            if (!ReferenceEquals(_panel, panel)) return;
            ResetAll();
        }

        internal static void ForceHoldState(Panel_Loading panel)
        {
            if (panel == null) return;

            panel.m_ShowQuoteAfterLoad = false;
            panel.m_HoldScreenAfterLoad = true;
            panel.m_HoldFadeAfterLoad = false;
            panel.m_ContinueToGame = false;
            panel.ForceMinHoldTime(0.25f);
        }

        private static void HideVanillaLoadingUI(Panel_Loading panel)
        {
            if (panel == null) return;

            if (_releaseRequested && _introLabel != null)
            {
                _introLabel.text = string.Empty;
                _introLabel.alpha = 0f;
            }

            if (panel.m_CustomLoadScreenLabel != null && !ReferenceEquals(panel.m_CustomLoadScreenLabel, _introLabel))
            {
                panel.m_CustomLoadScreenLabel.text = string.Empty;
                panel.m_CustomLoadScreenLabel.alpha = 0f;
            }

            if (panel.m_SceneLabel != null && !ReferenceEquals(panel.m_SceneLabel, _introLabel))
            {
                panel.m_SceneLabel.text = string.Empty;
                panel.m_SceneLabel.alpha = 0f;
            }

            if (panel.m_GameModeLabel != null && !ReferenceEquals(panel.m_GameModeLabel, _introLabel))
            {
                panel.m_GameModeLabel.text = string.Empty;
                panel.m_GameModeLabel.alpha = 0f;
            }

            if (panel.m_Label_Hint != null)
            {
                panel.m_Label_Hint.text = string.Empty;
                panel.m_Label_Hint.alpha = 0f;
            }

            if (panel.m_Label_HintTitle != null)
            {
                panel.m_Label_HintTitle.text = string.Empty;
                panel.m_Label_HintTitle.alpha = 0f;
            }

            HideNativeContinueLabel();

            if (panel.m_Background != null)
            {
                panel.m_Background.color = new Color(0f, 0f, 0f, 1f);
                panel.m_Background.alpha = 1f;
            }

            panel.m_BackgroundTextureAlpha = 1f;
        }

        internal static void EnforceNativePanelState(Panel_Loading panel)
        {
            if (!IsManagedPanel(panel)) return;

            if (!_releaseRequested) ForceHoldState(panel);

            HideVanillaLoadingUI(panel);

            if (!_releaseRequested)
            {
                if (_continueCloneObject != null && !_continueCloneObject.activeSelf) _continueCloneObject.SetActive(true);
                if (_continueLabel != null) _continueLabel.enabled = true;
            }
        }

        private static IEnumerator RunSequence(int generation, Panel_Loading panel)
        {
            SetAlpha(_introLabel, 0f);
            SetAlpha(_continueLabel, 0f);
            _acceptContinueInput = false;

            if (!IsValid(generation, panel))
            {
                ResetAll();
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.10f);

            for (int i = 0; i < Lines.Length; i++)
            {
                if (_introLabel == null)
                {
                    ResetAll();
                    yield break;
                }

                _introLabel.text = Lines[i];
                yield return Fade(_introLabel, 0f, 1f, 1.0f);

                if (!IsValid(generation, panel))
                {
                    ResetAll();
                    yield break;
                }

                yield return UpdateContinuePromptDuringSequence(generation, panel);

                if (_releaseRequested) yield break;

                float visibleTime = i < Lines.Length - 1 ? 1.5f : 1.5f;
                yield return WaitForSecondsOrUntilLoaded(generation, panel, visibleTime);

                if (!IsValid(generation, panel))
                {
                    ResetAll();
                    yield break;
                }

                if (_releaseRequested) yield break;

                yield return UpdateContinuePromptDuringSequence(generation, panel);

                if (_releaseRequested) yield break;

                if (i < Lines.Length - 1) yield return Fade(_introLabel, 1f, 0f, 1f);

                if (!IsValid(generation, panel))
                {
                    ResetAll();
                    yield break;
                }

                if (_releaseRequested) yield break;
            }

            while (IsValid(generation, panel))
            {
                EnforceNativePanelState(panel);

                if (panel.HasFinishedLoading())
                {
                    yield return ShowContinuePromptAndWait(generation, panel);
                    yield break;
                }

                yield return null;
            }

            ResetAll();
        }

        private static IEnumerator WaitForSecondsOrUntilLoaded(int generation, Panel_Loading panel, float seconds)
        {
            float elapsed = 0f;

            while (elapsed < seconds && IsValid(generation, panel))
            {
                EnforceNativePanelState(panel);

                if (panel.HasFinishedLoading())
                {
                    yield return UpdateContinuePromptDuringSequence(generation, panel);

                    if (_releaseRequested) yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator UpdateContinuePromptDuringSequence(int generation, Panel_Loading panel)
        {
            if (!IsValid(generation, panel)) yield break;
            if (_releaseRequested) yield break;
            if (!panel.HasFinishedLoading()) yield break;
            if (_continueLabel == null) yield break;

            if (_continueCloneObject != null && !_continueCloneObject.activeSelf) _continueCloneObject.SetActive(true);

            _continueLabel.enabled = true;
            _continueLabel.text = "Click to continue";

            if (!_continuePromptVisible)
            {
                _continuePromptVisible = true;

                if (_continueLabel.alpha <= 0f)
                {
                    yield return Fade(_continueLabel, 0f, 1f, 0.35f);
                    Log.Msg("Continue prompt visible.");
                }
            }

            if (!_acceptContinueInput && !IsContinueInputHeld()) _acceptContinueInput = true;

            if (_acceptContinueInput && !_continuePromptBlinking)
            {
                _continuePromptBlinking = true;
                MelonCoroutines.Start(BlinkPrompt(generation, _continueLabel));
            }

            if (_acceptContinueInput && IsContinueInputDown())
            {
                Log.Msg("Continue input accepted.");
                ReleaseToGame(panel);
            }
        }

        private static IEnumerator ShowContinuePromptAndWait(int generation, Panel_Loading panel)
        {
            if (_continueLabel == null)
            {
                ResetAll();
                yield break;
            }

            if (_continueCloneObject != null && !_continueCloneObject.activeSelf) _continueCloneObject.SetActive(true);

            _continueLabel.enabled = true;
            _continueLabel.text = "Click to continue";
            _continuePromptVisible = true;

            if (_continueLabel.alpha <= 0f)
            {
                yield return Fade(_continueLabel, 0f, 1f, 0.35f);
                Log.Msg("Continue prompt visible.");
            }

            while (IsValid(generation, panel) && IsContinueInputHeld())
            {
                EnforceNativePanelState(panel);
                yield return null;
            }

            _acceptContinueInput = true;

            if (!_continuePromptBlinking)
            {
                _continuePromptBlinking = true;
                MelonCoroutines.Start(BlinkPrompt(generation, _continueLabel));
            }

            while (IsValid(generation, panel))
            {
                EnforceNativePanelState(panel);

                if (_acceptContinueInput && IsContinueInputDown())
                {
                    Log.Msg("Continue input accepted.");
                    ReleaseToGame(panel);
                    yield break;
                }

                yield return null;
            }

            if (!IsValid(generation, panel))
            {
                ResetAll();
                yield break;
            }
        }

        private static void ReleaseToGame(Panel_Loading panel)
        {
            if (_releaseRequested) return;

            _releaseRequested = true;
            _acceptContinueInput = false;
            _continuePromptBlinking = false;

            panel.m_ShowQuoteAfterLoad = false;
            panel.m_HoldScreenAfterLoad = false;
            panel.m_HoldFadeAfterLoad = false;
            panel.m_ContinueToGame = true;
            panel.ResetMinHoldTime();

            SetAlpha(_introLabel, 0f);
            SetAlpha(_continueLabel, 0f);

            _continueCloneObject?.SetActive(false);

            Log.Msg("Releasing loading panel.");
        }

        private static bool IsValid(int generation, Panel_Loading panel)
        {
            if (generation != _generation) return false;
            if (_panel == null || panel == null) return false;

            return ReferenceEquals(_panel, panel);
        }

        private static bool IsContinueInputHeld()
        {
            return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.anyKey;
        }

        private static bool IsContinueInputDown()
        {
            return Input.GetMouseButtonDown(0) || Input.anyKeyDown;
        }

        private static bool IsAnyInputDown()
        {
            return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2) || Input.anyKeyDown;
        }

        private static UILabel? GetIntroLabel(Panel_Loading panel)
        {
            if (panel.m_CustomLoadScreenLabel != null) return panel.m_CustomLoadScreenLabel;
            if (panel.m_SceneLabel != null) return panel.m_SceneLabel;
            if (panel.m_GameModeLabel != null) return panel.m_GameModeLabel;
            return null;
        }

        private static void PreparePanelState(Panel_Loading panel)
        {
            RestorePanelState();
            DestroyContinueClone();

            _introLabel = GetIntroLabel(panel);
            _nativeContinueLabel = panel.m_ContinueLabel;

            if (_introLabel == null)
            {
                MelonLogger.Warning("PZLoadingScreen: no intro UILabel found on Panel_Loading.");
                return;
            }

            if (_nativeContinueLabel == null)
            {
                MelonLogger.Warning("PZLoadingScreen: no native continue UILabel found on Panel_Loading.");
                return;
            }

            _savedState = true;

            _savedIntroText = _introLabel.text;
            _savedIntroAlpha = _introLabel.alpha;

            _savedNativeContinueText = _nativeContinueLabel.text;
            _savedNativeContinueAlpha = _nativeContinueLabel.alpha;
            _savedPanelContinueAlpha = panel.m_LabelContinueAlpha;

            if (panel.m_SceneLabel != null) _savedSceneAlpha = panel.m_SceneLabel.alpha;
            if (panel.m_GameModeLabel != null) _savedGameModeAlpha = panel.m_GameModeLabel.alpha;
            if (panel.m_Label_Hint != null) _savedHintAlpha = panel.m_Label_Hint.alpha;
            if (panel.m_Label_HintTitle != null) _savedHintTitleAlpha = panel.m_Label_HintTitle.alpha;
            if (panel.m_Background != null) _savedBackgroundColor = panel.m_Background.color;

            _savedBackgroundTextureAlpha = panel.m_BackgroundTextureAlpha;

            if (panel.m_Background != null)
            {
                panel.m_Background.color = new Color(0f, 0f, 0f, 1f);
                panel.m_Background.alpha = 1f;
            }

            panel.m_BackgroundTextureAlpha = 1f;

            HideLabel(panel.m_SceneLabel, _introLabel, null);
            HideLabel(panel.m_GameModeLabel, _introLabel, null);
            HideLabel(panel.m_Label_Hint, _introLabel, null);
            HideLabel(panel.m_Label_HintTitle, _introLabel, null);

            _introLabel.text = string.Empty;
            _introLabel.alpha = 0f;

            _continueCloneObject = UnityEngine.Object.Instantiate(_nativeContinueLabel.gameObject);
            _continueCloneObject.name = "PZLoadingScreenContinueClone";
            _continueCloneObject.transform.SetParent(_nativeContinueLabel.transform.parent, false);
            _continueCloneObject.transform.localPosition = _nativeContinueLabel.transform.localPosition;
            _continueCloneObject.transform.localRotation = _nativeContinueLabel.transform.localRotation;
            _continueCloneObject.transform.localScale = _nativeContinueLabel.transform.localScale;
            _continueCloneObject.SetActive(true);

            _continueLabel = _continueCloneObject.GetComponent<UILabel>();
            if (_continueLabel == null)
            {
                MelonLogger.Warning("PZLoadingScreen: failed to get UILabel from continue clone.");
                return;
            }

            _continueLabel.enabled = true;
            _continueLabel.depth = _nativeContinueLabel.depth + 10;
            _continueLabel.text = "Click to continue";
            _continueLabel.alpha = 0f;

            HideNativeContinueLabel();
        }

        private static void RestorePanelState()
        {
            if (!_savedState || _panel == null) return;

            if (_introLabel != null)
            {
                _introLabel.text = _savedIntroText;
                _introLabel.alpha = _savedIntroAlpha;
            }

            if (_nativeContinueLabel != null)
            {
                _nativeContinueLabel.text = _savedNativeContinueText;
                _nativeContinueLabel.alpha = _savedNativeContinueAlpha;
            }

            _panel.m_LabelContinueAlpha = _savedPanelContinueAlpha;

            if (_panel.m_SceneLabel != null) _panel.m_SceneLabel.alpha = _savedSceneAlpha;
            if (_panel.m_GameModeLabel != null) _panel.m_GameModeLabel.alpha = _savedGameModeAlpha;
            if (_panel.m_Label_Hint != null) _panel.m_Label_Hint.alpha = _savedHintAlpha;
            if (_panel.m_Label_HintTitle != null) _panel.m_Label_HintTitle.alpha = _savedHintTitleAlpha;
            if (_panel.m_Background != null) _panel.m_Background.color = _savedBackgroundColor;

            _panel.m_BackgroundTextureAlpha = _savedBackgroundTextureAlpha;

            _savedState = false;
        }

        private static void DestroyContinueClone()
        {
            if (_continueCloneObject != null)
            {
                UnityEngine.Object.Destroy(_continueCloneObject);
                _continueCloneObject = null;
            }

            _continueLabel = null;
        }

        private static void HideNativeContinueLabel()
        {
            if (_nativeContinueLabel == null) return;

            _nativeContinueLabel.text = string.Empty;
            _nativeContinueLabel.alpha = 0f;

            if (_panel != null) _panel.m_LabelContinueAlpha = 0f;
        }

        private static void HideLabel(UILabel? label, UILabel? introLabel, UILabel? continueLabel)
        {
            if (label == null) return;
            if (ReferenceEquals(label, introLabel)) return;
            if (ReferenceEquals(label, continueLabel)) return;

            label.alpha = 0f;
        }

        private static void SetAlpha(UILabel? label, float alpha)
        {
            if (label == null) return;

            label.alpha = alpha;
        }

        private static IEnumerator Fade(UILabel label, float from, float to, float duration)
        {
            if (label == null) yield break;

            float t = 0f;

            while (t < duration)
            {
                if (label == null) yield break;

                t += Time.unscaledDeltaTime;
                label.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }

            if (label != null) label.alpha = to;
        }

        private static IEnumerator BlinkPrompt(int generation, UILabel label)
        {
            while (generation == _generation && label != null && !_releaseRequested)
            {
                yield return Fade(label, 1f, 0.35f, 0.8f);
                yield return Fade(label, 0.35f, 1f, 0.8f);
            }
        }

        private static T SafeCall<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.Enable))]
    internal static class Patch_Panel_Loading_Enable
    {
        private static void Postfix(Panel_Loading __instance, bool enable)
        {
            if (__instance == null) return;

            if (enable) LoadingIntro.TryBegin(__instance);
            else LoadingIntro.NotifyPanelDisabled(__instance);
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.Update))]
    internal static class Patch_Panel_Loading_Update
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance == null) return true;

            LoadingIntro.EnforceNativePanelState(__instance);

            if (LoadingIntro.ShouldSuppressPanelLoadingUpdate(__instance))
            {
                LoadingIntro.LogBlockedEarlyInputIfAny();
                return false;
            }

            return true;
        }

        private static void Postfix(Panel_Loading __instance)
        {
            if (__instance == null) return;

            if (LoadingIntro.IsManagedPanel(__instance)) LoadingIntro.EnforceNativePanelState(__instance);
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.HasFinishedHolding))]
    internal static class Patch_Panel_Loading_HasFinishedHolding
    {
        private static bool Prefix(Panel_Loading __instance, ref bool __result)
        {
            if (__instance != null && LoadingIntro.ShouldForceHold(__instance))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.OnQuoteFinished))]
    internal static class Patch_Panel_Loading_OnQuoteFinished
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.SetRandomQuoteActive))]
    internal static class Patch_Panel_Loading_SetRandomQuoteActive
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.SetLoadingScreenText))]
    internal static class Patch_Panel_Loading_SetLoadingScreenText
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(InterfaceManager), nameof(InterfaceManager.ShouldImmediatelyExitOverlay))]
    internal static class Patch_InterfaceManager_ShouldImmediatelyExitOverlay
    {
        private static bool Prefix(ref bool __result)
        {
            if (LoadingIntro.IsBlockingNativeInputWindow())
            {
                LoadingIntro.LogBlockedEarlyInputIfAny();
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(InterfaceManager), nameof(InterfaceManager.ShouldImmediatelyExitOverlayDuringStruggle))]
    internal static class Patch_InterfaceManager_ShouldImmediatelyExitOverlayDuringStruggle
    {
        private static bool Prefix(ref bool __result)
        {
            if (LoadingIntro.IsBlockingNativeInputWindow())
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.QueueHintLabel))]
    internal static class Patch_Panel_Loading_QueueHintLabel
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.ShowHintLabel))]
    internal static class Patch_Panel_Loading_ShowHintLabel
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.OnHintFinished))]
    internal static class Patch_Panel_Loading_OnHintFinished
    {
        private static bool Prefix(Panel_Loading __instance)
        {
            if (__instance != null && LoadingIntro.IsManagedPanel(__instance)) return false;

            return true;
        }
    }
}