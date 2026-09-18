using HarmonyLib;
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
        static void Postfix(MapApp __instance, bool open)
        {
            FullMapView.Instance?.SetVisible(open);
            if (__instance._screen != null)
                __instance._screen.gameObject.SetActive(false);
        }
    }
}
