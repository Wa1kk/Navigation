namespace IndoorNav.Services;

/// <summary>
/// Static hub that routes deep-link node requests from the OS (camera scan /
/// external URL) and from in-app QR scanning to whoever is listening.
/// </summary>
public static class DeepLinkService
{
    // ── Events ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the calling thread when a node ID has been resolved
    /// (via deep link or in-app scan).  Subscribers should marshal to the UI
    /// thread as needed.
    /// </summary>
    public static event Action<string>? NodeRequested;

    // ── API ─────────────────────────────────────────────────────────────────────

    /// <summary>Fire the event for the given node ID.</summary>
    public static void RequestNode(string nodeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
            NodeRequested?.Invoke(nodeId.Trim('/'));
    }

    /// <summary>
    /// Try to extract a node ID from a URI / raw string.
    /// Accepts:
    ///   • <c>indoornav://node/{id}</c>  — deep-link / camera-scan URI
    ///   • <c>indoornav-node:{id}</c>    — legacy format (still in old QR codes)
    ///   • bare GUID string
    /// Returns <c>null</c> when the input is unrecognised.
    /// </summary>
    public static string? ParseUri(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        // Modern: indoornav://node/{guid}
        if (s.StartsWith("indoornav://node/", StringComparison.OrdinalIgnoreCase))
            return s["indoornav://node/".Length..].TrimEnd('/');

        // Legacy: indoornav-node:{guid}
        if (s.StartsWith("indoornav-node:", StringComparison.OrdinalIgnoreCase))
            return s["indoornav-node:".Length..].Trim();

        // Bare GUID
        if (Guid.TryParse(s, out _)) return s;

        return null;
    }
}
