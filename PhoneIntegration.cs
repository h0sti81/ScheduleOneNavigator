using HarmonyLib;
using UnityEngine;
using MelonLoader;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Map;

namespace ScheduleOneNavigator
{
    // Hijacks the game's own Map app instead of registering a new one -
    // MapApp is always present and already does exactly what we replace
    // (FullMapView already reads MapApp.MainMapSprite for the map texture).
    // Registering a real new App<T> would need Il2Cpp class injection
    // (ClassInjector.RegisterTypeInIl2Cpp) for comparatively little gain.
    //
    // Postfix, not a Prefix-with-skip: letting MapApp.SetOpen run in full
    // means whatever native gameplay-input blocking the game applies while
    // a phone app is open (the whole point of this integration - see
    // FullMapView's SetVisible comments on the "player still attacks with
    // the tablet open" bug) fires for real, instead of being bypassed along
    // with the rest of the method. We only additionally show our own
    // overlay and hide the tiny native map screen so the two don't overlap.
    [HarmonyPatch(typeof(MapApp), nameof(MapApp.SetOpen))]
    static class MapAppSetOpenPatch
    {
        // Re-entrancy guard for the RequestCloseApp() call below - confirmed
        // live 2026-09-20 that vanilla right-click freezes the game solid
        // (no exception, log just stops dead) without this: RequestCloseApp()
        // closes the currently-active app (still MapApp at this point) by
        // calling MapApp.SetOpen(false) on it again, which re-enters this
        // same Prefix, which calls RequestCloseApp() again, forever. Set for
        // the duration of our own RequestCloseApp() call so that nested
        // re-entry is skipped instead of recursing.
        static bool closingViaRequestCloseApp;

        // Catches every SetOpen(false) that does NOT come from our own
        // Tick() M/Escape handling (chiefly vanilla right-click, which also
        // closes the app but never runs through FullMapView.Tick() at all -
        // confirmed live 2026-09-20 as the same blank/unloaded phone screen
        // bug Escape had). Must run BEFORE the original method, same
        // ordering FullMapView.Tick() already uses for Escape:
        // RequestCloseApp() needs the app to still look "active" to
        // correctly swap AppsCanvas/HomeScreen. Skipped when
        // FullMapView.closingViaOwnKeyHandling is set, since Tick() already
        // handles those paths itself with the right closeToPhoneHome value.
        static void Prefix(bool open)
        {
            if (open || FullMapView.closingViaOwnKeyHandling || closingViaRequestCloseApp)
                return;
            FullMapView.closeToPhoneHome = true;
            closingViaRequestCloseApp = true;
            try
            {
                Phone.Instance?.RequestCloseApp();
            }
            finally
            {
                closingViaRequestCloseApp = false;
            }
        }

        static void Postfix(MapApp __instance, bool open)
        {
            FullMapView.Instance?.SetVisible(open);
            if (__instance._screen != null)
                __instance._screen.gameObject.SetActive(false);
        }
    }

    // Cursor lock/visibility must track whether the PHONE is open, not
    // whether our own map view is - confirmed live 2026-09-20 that the
    // cursor stayed free forever after backing out of the map to the
    // phone's home screen (Escape/vanilla right-click) and then closing the
    // phone itself: that final close is a pure vanilla action that never
    // touches MapApp.SetOpen at all, so nothing in this mod ever ran to
    // restore it. Phone.SetIsOpen(bool) is the method FullMapView.SetVisible
    // itself already calls to mirror the real phone's open/closed state
    // (see FullMapView.cs) - patching it directly here, instead of
    // FullMapView's own open/close branches, is the same "hook the real
    // game mechanism instead of individual UI paths" fix that solved the
    // earlier attack-while-tablet-open bug (see FullMapView.cs SetVisible
    // comments).
    [HarmonyPatch(typeof(Phone), nameof(Phone.SetIsOpen))]
    static class PhoneSetIsOpenCursorPatch
    {
        static CursorLockMode savedLockState;
        static bool savedCursorVisible;

        // Guards against SetIsOpen(true) being called again while the phone
        // is already open (FullMapView.SetVisible does this unconditionally
        // on every map open, even when just switching apps without the
        // phone itself ever closing) - without this, a second such call
        // would re-save the already-free cursor state as the "original" one,
        // and the later restore would wrongly leave the cursor free instead
        // of returning to the true pre-phone state.
        static bool cursorSaved;

        // Re-entrancy guard for the FullMapView.Instance.SetVisible(false)
        // call below - its own "real close" branch calls
        // Phone.Instance.SetIsOpen(false) itself (to mirror the real
        // phone's state when WE initiate a close), which would otherwise
        // re-enter this same Postfix and recurse (same failure shape as the
        // right-click freeze MapAppSetOpenPatch already guards against).
        static bool closingFullMapView;

        // Parameter named __0 (Harmony's name-independent positional
        // convention), NOT "open" - confirmed live 2026-09-20 via the
        // diagnostic logging below that this whole patch silently failed to
        // register all session ("Failed to patch ... Parameter \"open\" not
        // found in method ... SetIsOpen(bool o)"): Harmony's Il2Cpp patcher
        // matches Postfix parameters by NAME against the original method's
        // actual (game-update-dependent) parameter name, which is "o" here,
        // not "open". __0 sidesteps this regardless of what the target
        // method calls its own parameter, and stays robust across future
        // game updates that might rename it again.
        static void Postfix(bool __0)
        {
            // Diagnostic logging (2026-09-20) - this Postfix previously had no
            // logging at all, making it impossible to tell from Latest.log whether
            // Phone.SetIsOpen(false) even fires on a given TAB press, or whether it
            // fires but the close-FullMapView branch below is skipped for some
            // reason. Added after a user report that TAB stopped closing the map -
            // this logging is what surfaced the real cause (the patch failing to
            // register at all, see the parameter-naming comment above).
            MelonLogger.Msg($"[Minimap] PhoneSetIsOpenCursorPatch: Postfix open={__0}, " +
                $"closingFullMapView={closingFullMapView}, FullMapView.Instance!=null={FullMapView.Instance != null}, " +
                $"FullMapView.Instance.Visible={(FullMapView.Instance != null ? FullMapView.Instance.Visible.ToString() : "n/a")}");

            if (closingFullMapView)
                return;

            if (__0)
            {
                if (!cursorSaved)
                {
                    savedLockState = Cursor.lockState;
                    savedCursorVisible = Cursor.visible;
                    cursorSaved = true;
                }
                // "Kick" (2026-09-20) - user reproducibly confirmed that a
                // cursor click offset (tab clicks, route-planning map
                // clicks) present since game launch disappears permanently
                // after a single manual Alt-Tab out of and back into the
                // game - a classic Wine/Proton cursor-position desync
                // signature (the OS/compositor<->Wine pointer coordinate
                // transform only gets recomputed on a focus change), not a
                // bug in this mod's own click math (independently confirmed
                // correct multiple times this session via hit=True logs and
                // sub-meter-accurate shop routing). Briefly forcing a
                // Locked->None transition here tries to trigger the same
                // grab/release cycle a real Alt-Tab causes, without
                // requiring the user to do it manually. Experimental - if
                // two same-call sets aren't enough to force a real cycle,
                // the next step is deferring the second set to a later
                // frame instead (see FullMapView.cs's pendingMClose for the
                // established one-frame-latch pattern to model that on).
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (cursorSaved)
            {
                Cursor.lockState = savedLockState;
                Cursor.visible = savedCursorVisible;
                cursorSaved = false;
            }

            // TAB is vanilla's own hotkey for closing the whole phone, but
            // it never goes through MapApp.SetOpen at all - confirmed live
            // 2026-09-20 that FullMapView's own overlay stayed stuck open
            // after TAB closed the underlying vanilla phone. Phone.SetIsOpen
            // is the one call every close path (TAB included) funnels
            // through, so close our overlay here too, whenever it's still
            // showing.
            if (!__0 && FullMapView.Instance != null && FullMapView.Instance.Visible)
            {
                closingFullMapView = true;
                try
                {
                    FullMapView.Instance.SetVisible(false);
                }
                finally
                {
                    closingFullMapView = false;
                }
            }
        }
    }
}
