using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;

namespace IndoorNav.Services;

/// <summary>
/// Manages local push notifications for emergency (ЧС) alerts.
/// Only sends evacuation notifications — no other notifications are ever sent.
/// When ЧС is active: sends notifications every 3 s (app in background) or 5 s (app in foreground).
/// Uses iOS UNTimeIntervalNotificationTrigger for background notifications that survive app kill.
/// </summary>
public class NotificationService
{
    private const string PermissionPreferenceKey = "notification_permission_requested";
    private const string EmergencySpamKey = "emergency_spam_active";
    private const string EmergencyBuildingKey = "emergency_spam_building";

    private CancellationTokenSource? _spamCts;
    private string? _currentBuildingName;
    private bool _isAppInForeground = true;

    /// <summary>Whether the user has already been asked for notification permission (regardless of response).</summary>
    public bool WasPermissionRequested => Preferences.Default.Get(PermissionPreferenceKey, false);

    /// <summary>Whether the user has granted notification permission.</summary>
    public bool HasPermission { get; private set; }

    /// <summary>Whether emergency spam is currently active.</summary>
    public bool IsEmergencySpamActive => Preferences.Default.Get(EmergencySpamKey, false);

    // ── App lifecycle tracking ─────────────────────────────────────────────

    /// <summary>Call when the app moves to the foreground.</summary>
    public void OnAppForegrounded()
    {
        _isAppInForeground = true;
        // When coming back to foreground, cancel iOS scheduled triggers
        // and switch to in-app loop for more responsive notifications
        CancelIosScheduledNotifications();
        if (IsEmergencySpamActive && _spamCts == null)
            RestartSpamFromPersistedState();
    }

    /// <summary>Call when the app moves to the background.</summary>
    public void OnAppBackgrounded()
    {
        _isAppInForeground = false;
        // Stop in-app loop and schedule iOS repeating notifications
        // that work even when the app is suspended/killed
        if (_spamCts != null)
        {
            _spamCts?.Cancel();
            _spamCts?.Dispose();
            _spamCts = null;
        }
        if (IsEmergencySpamActive)
            ScheduleIosRepeatingNotification(_currentBuildingName);
    }

    // ── Permission ─────────────────────────────────────────────────────────

    /// <summary>
    /// Request system notification permission.
    /// Returns true if permission was granted.
    /// </summary>
    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            var result = await LocalNotificationCenter.Current.RequestNotificationPermission();
            HasPermission = result;
        }
        catch
        {
            HasPermission = false;
        }

        Preferences.Default.Set(PermissionPreferenceKey, true);
        return HasPermission;
    }

    /// <summary>
    /// Check current notification permission status without prompting the user.
    /// </summary>
    public async Task<bool> CheckPermissionAsync()
    {
        try
        {
            var enabled = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            HasPermission = enabled;
        }
        catch
        {
            HasPermission = false;
        }
        return HasPermission;
    }

    // ── Emergency notification spam loop ───────────────────────────────────

    /// <summary>
    /// Start sending emergency notifications repeatedly.
    /// Interval: 3 s when app is in background, 5 s when in foreground.
    /// Persists state so notifications resume after app restart.
    /// </summary>
    public void StartEmergencySpam(string? buildingName = null)
    {
        if (!HasPermission) return;

        // Don't start a second loop if one is already running
        if (_spamCts != null && !_spamCts.IsCancellationRequested) return;

        _currentBuildingName = buildingName;

        // Persist spam state for restart
        Preferences.Default.Set(EmergencySpamKey, true);
        Preferences.Default.Set(EmergencyBuildingKey, buildingName ?? string.Empty);

        _spamCts?.Dispose();
        _spamCts = new CancellationTokenSource();
        var ct = _spamCts.Token;

        // Send first notification immediately
        _ = SendEmergencyNotificationAsync(_currentBuildingName);

        Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 3 s in background, 5 s in foreground
                    var interval = _isAppInForeground ? 5 : 3;
                    await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                    await SendEmergencyNotificationAsync(_currentBuildingName);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    /// <summary>Stop the emergency notification spam loop.</summary>
    public void StopEmergencySpam()
    {
        _spamCts?.Cancel();
        _spamCts?.Dispose();
        _spamCts = null;

        // Clear persisted state
        Preferences.Default.Set(EmergencySpamKey, false);
        Preferences.Default.Remove(EmergencyBuildingKey);

        // Cancel any scheduled iOS notifications
        CancelIosScheduledNotifications();
    }

    /// <summary>
    /// Restart spam loop from persisted state (called on app launch/resume).
    /// </summary>
    public void RestartSpamFromPersistedState()
    {
        if (!IsEmergencySpamActive) return;

        var buildingName = Preferences.Default.Get(EmergencyBuildingKey, string.Empty);
        _currentBuildingName = string.IsNullOrEmpty(buildingName) ? null : buildingName;
        StartEmergencySpam(_currentBuildingName);
    }

    /// <summary>
    /// Send a single emergency evacuation local notification.
    /// </summary>
    public async Task SendEmergencyNotificationAsync(string? buildingName = null)
    {
        if (!HasPermission) return;

        try
        {
            var title = "🚨 Чрезвычайная ситуация";
            var body = string.IsNullOrEmpty(buildingName)
                ? "Немедленно покиньте здание! Следуйте по маршруту эвакуации."
                : $"Немедленно покиньте корпус «{buildingName}»! Следуйте по маршруту эвакуации.";

            var notification = new NotificationRequest
            {
                NotificationId = 1001,
                Title = title,
                Description = body,
                ReturningData = "emergency",
            };

            await LocalNotificationCenter.Current.Show(notification);
        }
        catch
        {
            // best-effort
        }
    }

    // ── iOS native repeating notifications (work when app is killed) ────────

#if IOS || MACCATALYST
    private void ScheduleIosRepeatingNotification(string? buildingName)
    {
        try
        {
            var title = "🚨 Чрезвычайная ситуация";
            var body = string.IsNullOrEmpty(buildingName)
                ? "Немедленно покиньте здание! Следуйте по маршруту эвакуации."
                : $"Немедленно покиньте корпус «{buildingName}»! Следуйте по маршруту эвакуации.";

            var content = new UserNotifications.UNMutableNotificationContent();
            content.Title = title;
            content.Body = body;
            content.Sound = UserNotifications.UNNotificationSound.DefaultCriticalSound;
            content.UserInfo = new Foundation.NSDictionary("returningData", new Foundation.NSString("emergency"));

            // Repeat every 3 seconds while in background
            var trigger = UserNotifications.UNTimeIntervalNotificationTrigger.CreateTrigger(3, true);

            var request = UserNotifications.UNNotificationRequest.FromIdentifier("emergency_spam", content, trigger);
            UserNotifications.UNUserNotificationCenter.Current.AddNotificationRequest(request, null);
        }
        catch { /* best-effort */ }
    }

    private void CancelIosScheduledNotifications()
    {
        try
        {
            UserNotifications.UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
        }
        catch { /* best-effort */ }
    }
#else
    private void ScheduleIosRepeatingNotification(string? buildingName) { }
    private void CancelIosScheduledNotifications() { }
#endif

    // ── Initialization ─────────────────────────────────────────────────────

    /// <summary>
    /// Initialize the notification service (call once on app startup).
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // If user previously granted permission, keep HasPermission in sync
            if (WasPermissionRequested)
            {
                await CheckPermissionAsync();
            }

            // Handle notification tap (when user taps the notification while app is in background)
            LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;
        }
        catch
        {
            // best-effort
        }
    }

    private static void OnNotificationTapped(NotificationActionEventArgs e)
    {
        if (e.IsTapped && e.Request?.ReturningData == "emergency")
        {
            // The app will come to foreground; the EmergencyService polling
            // will already have set the emergency state, so the overlay will show.
        }
    }
}
