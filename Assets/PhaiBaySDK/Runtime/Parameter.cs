public static partial class PhaiBay
{
    /// <summary>Event parameter, same idea as Firebase.Analytics.Parameter.</summary>
    public sealed class Parameter
    {
        public readonly string Name;
        public readonly object Value;

        public Parameter(string name, string value) { Name = name; Value = value; }
        public Parameter(string name, long value) { Name = name; Value = value; }
        public Parameter(string name, int value) { Name = name; Value = (long)value; }
        public Parameter(string name, double value) { Name = name; Value = value; }
        public Parameter(string name, float value) { Name = name; Value = (double)value; }
        public Parameter(string name, bool value) { Name = name; Value = value; }
    }

    /// <summary>Common event names (optional helpers, like FirebaseAnalytics.EventXxx).</summary>
    public static class Events
    {
        public const string FirstOpen = "first_open";             // auto (autoTrackEvents)
        public const string SessionStart = "session_start";       // auto (autoTrackEvents)
        public const string UserEngagement = "user_engagement";   // auto (autoTrackEvents)
        public const string LevelStart = "level_start";
        public const string LevelEnd = "level_end";
        public const string LevelUp = "level_up";
        public const string Purchase = "purchase";
        public const string AdImpression = "ad_impression";
        public const string EarnVirtualCurrency = "earn_virtual_currency";
        public const string SpendVirtualCurrency = "spend_virtual_currency";
        public const string TutorialBegin = "tutorial_begin";
        public const string TutorialComplete = "tutorial_complete";
    }

    /// <summary>Common parameter names.</summary>
    public static class Params
    {
        public const string Level = "level";
        public const string LevelName = "level_name";
        public const string Success = "success";
        public const string Value = "value";
        public const string Currency = "currency";
        public const string ItemId = "item_id";
        public const string AdPlatform = "ad_platform";
        public const string AdFormat = "ad_format";
        public const string AdUnitName = "ad_unit_name";
        public const string EngagementTimeMsec = "engagement_time_msec";
    }
}
