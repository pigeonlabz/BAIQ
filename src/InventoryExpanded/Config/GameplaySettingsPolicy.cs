using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Backpack.Config;

internal readonly record struct GameplaySettingsSnapshot(
    int StackSizeMultiplier,
    bool DryingRacks,
    bool MixingStations,
    bool PlantYield,
    bool PackagingStations)
{
    internal static GameplaySettingsSnapshot Vanilla => new(1, false, false, false, false);

    internal bool IsSupported => StackSizeMultiplier is 1 or 2 or 4 or 8;
}

internal enum GameplaySettingsMode
{
    Local,
    LobbyIdentityPending,
    HostPending,
    Host,
    GuestPending,
    GuestReady,
    Incompatible
}

internal readonly record struct GameplaySettingsSessionState(
    GameplaySettingsMode Mode,
    ulong SessionGeneration,
    ulong OwnerId,
    GameplaySettingsSnapshot EffectiveSettings,
    string AcceptedEnvelope)
{
    internal bool IsReady => Mode is GameplaySettingsMode.Local or GameplaySettingsMode.Host or GameplaySettingsMode.GuestReady;
}

/// <summary>
/// Pure payload and session-state rules for host-controlled gameplay settings.
/// Steam lobby metadata is authenticated by the game's active lobby service;
/// the envelope binds to the current Steam owner and a host-generated nonce.
/// </summary>
internal static class GameplaySettingsPolicy
{
    private const string Schema = "2";

    internal static GameplaySettingsSessionState Local(GameplaySettingsSnapshot settings) =>
        new(GameplaySettingsMode.Local, 0, 0, Normalize(settings), null);

    internal static GameplaySettingsSessionState BeginHost(
        ulong sessionGeneration,
        ulong ownerId,
        GameplaySettingsSnapshot settings,
        string envelope)
    {
        if (sessionGeneration == 0 || ownerId == 0 || !settings.IsSupported || string.IsNullOrWhiteSpace(envelope))
            return Incompatible(sessionGeneration, ownerId);

        return new(GameplaySettingsMode.Host, sessionGeneration, ownerId, settings, envelope);
    }

    internal static GameplaySettingsSessionState BeginGuest(ulong sessionGeneration, ulong ownerId)
    {
        if (sessionGeneration == 0 || ownerId == 0)
            return Incompatible(sessionGeneration, ownerId);

        return new(GameplaySettingsMode.GuestPending, sessionGeneration, ownerId, GameplaySettingsSnapshot.Vanilla, null);
    }

    internal static GameplaySettingsSessionState LobbyIdentityPending(ulong sessionGeneration, ulong ownerId) =>
        new(GameplaySettingsMode.LobbyIdentityPending, sessionGeneration, ownerId, GameplaySettingsSnapshot.Vanilla, null);

    internal static GameplaySettingsSessionState HostPending(ulong sessionGeneration, ulong ownerId) =>
        new(GameplaySettingsMode.HostPending, sessionGeneration, ownerId, GameplaySettingsSnapshot.Vanilla, null);

    internal static GameplaySettingsSessionState Incompatible(ulong sessionGeneration, ulong ownerId) =>
        new(GameplaySettingsMode.Incompatible, sessionGeneration, ownerId, GameplaySettingsSnapshot.Vanilla, null);

    internal static GameplaySettingsSessionState EndSession(GameplaySettingsSnapshot localSettings) => Local(localSettings);

    internal static string CreateEnvelope(
        string modVersion,
        ulong ownerId,
        string nonce,
        GameplaySettingsSnapshot settings)
    {
        if (string.IsNullOrWhiteSpace(modVersion)
            || ownerId == 0
            || !Guid.TryParseExact(nonce, "N", out _)
            || !settings.IsSupported)
            return null;

        return string.Join("|", new[]
        {
            Schema,
            modVersion,
            CreateOwnerBinding(ownerId),
            nonce,
            settings.StackSizeMultiplier.ToString(CultureInfo.InvariantCulture),
            Flag(settings.DryingRacks),
            Flag(settings.MixingStations),
            Flag(settings.PlantYield),
            Flag(settings.PackagingStations)
        });
    }

    internal static bool TryParseEnvelope(
        string payload,
        string expectedModVersion,
        ulong ownerId,
        out string nonce,
        out GameplaySettingsSnapshot settings)
    {
        nonce = null;
        settings = GameplaySettingsSnapshot.Vanilla;
        if (string.IsNullOrWhiteSpace(payload)
            || string.IsNullOrWhiteSpace(expectedModVersion)
            || ownerId == 0)
            return false;

        var parts = payload.Split('|');
        if (parts.Length != 9
            || !string.Equals(parts[0], Schema, StringComparison.Ordinal)
            || !string.Equals(parts[1], expectedModVersion, StringComparison.Ordinal)
            || !string.Equals(parts[2], CreateOwnerBinding(ownerId), StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[3], "N", out _)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var multiplier)
            || multiplier is not (1 or 2 or 4 or 8)
            || !TryParseFlag(parts[5], out var drying)
            || !TryParseFlag(parts[6], out var mixing)
            || !TryParseFlag(parts[7], out var plants)
            || !TryParseFlag(parts[8], out var packaging))
            return false;

        nonce = parts[3];
        settings = new GameplaySettingsSnapshot(multiplier, drying, mixing, plants, packaging);
        return true;
    }

    internal static bool TryAcceptGuestEnvelope(
        GameplaySettingsSessionState current,
        string payload,
        string expectedModVersion,
        out GameplaySettingsSessionState accepted)
    {
        accepted = current;
        if (current.Mode is not (GameplaySettingsMode.GuestPending or GameplaySettingsMode.GuestReady)
            || !TryParseEnvelope(payload, expectedModVersion, current.OwnerId, out _, out var settings))
            return false;

        if (current.Mode == GameplaySettingsMode.GuestReady)
            return string.Equals(current.AcceptedEnvelope, payload, StringComparison.Ordinal);

        accepted = new GameplaySettingsSessionState(
            GameplaySettingsMode.GuestReady,
            current.SessionGeneration,
            current.OwnerId,
            settings,
            payload);
        return true;
    }

    internal static string CreateOwnerBinding(ulong ownerId)
    {
        var bytes = Encoding.UTF8.GetBytes(ownerId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 12));
    }

    private static GameplaySettingsSnapshot Normalize(GameplaySettingsSnapshot settings) =>
        settings.IsSupported ? settings : GameplaySettingsSnapshot.Vanilla;

    private static string Flag(bool value) => value ? "1" : "0";

    private static bool TryParseFlag(string value, out bool result)
    {
        result = value switch
        {
            "0" => false,
            "1" => true,
            _ => false
        };
        return value is "0" or "1";
    }
}
