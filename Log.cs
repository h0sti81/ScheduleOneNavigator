using MelonLoader;

namespace ScheduleOneNavigator
{
    // Diagnostic output (path stats, click/tab diagnostics, inventory dumps)
    // goes through Debug and is off by default so normal players get a
    // quiet log. Enable via UserData/MelonPreferences.cfg:
    //   [ScheduleOneNavigator]
    //   VerboseLogging = true
    // Warnings and errors always go straight to MelonLogger.
    static class Log
    {
        static MelonPreferences_Entry<bool> verbose;

        public static void Init()
        {
            var category = MelonPreferences.CreateCategory("ScheduleOneNavigator", "ScheduleOne Navigator");
            verbose = category.CreateEntry("VerboseLogging", false, "Verbose logging",
                "Write detailed pathfinding/UI diagnostics to the MelonLoader log (for bug reports).");
        }

        public static bool Verbose => verbose != null && verbose.Value;

        public static void Debug(string message)
        {
            if (Verbose)
                MelonLogger.Msg(message);
        }
    }
}
