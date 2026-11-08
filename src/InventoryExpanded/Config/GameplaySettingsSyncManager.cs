using System.Globalization;
using Backpack.Cheats;
#if IL2CPP
using Il2CppFishNet;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
#elif MONO
using FishNet;
using ScheduleOne.Networking;
using Steamworks;
#endif
using UnityEngine;

namespace Backpack.Config;

/// <summary>
/// Synchronizes the host's frozen gameplay-cheat policy through the game's
/// active Steam lobby service. It introduces no FishNet RPC and never writes
/// guest preferences.
/// </summary>
internal static class GameplaySettingsSyncManager
{
    private const string MetadataKey = "BAIQ_GameplayPolicy_v2";
    private const float IdentityWarmupSeconds = 10f;
    private const float ActivePollIntervalSeconds = 0.25f;
    private const float RecoveryPollIntervalSeconds = 1f;
    private const float HostPolicyRecheckIntervalSeconds = 5f;
    private static readonly string ModVersion = typeof(BackpackMod).Assembly.GetName().Version?.ToString() ?? string.Empty;
    private static string _policyNonce;
    private static GameplaySettingsSessionState _state = GameplaySettingsPolicy.Local(GameplaySettingsSnapshot.Vanilla);
    private static SteamLobbyService _activeSteamLobbyService;
    private static ulong _lobbySessionGeneration;
    private static ulong _trackedSessionGeneration;
    private static ulong _trackedOwnerId;
    private static bool _trackingLobby;
    private static bool _sessionSuspended;
    private static float _noLobbySince = -1f;
    private static bool _hostPolicyInitialized;
    private static bool _hostPolicyPublished;
    private static bool _policyChangedDuringSession;
    private static GameplaySettingsSessionState _frozenHostState;
    private static GameplaySettingsSessionState _frozenGuestState;
    private static float _identityUnavailableSince = -1f;
    private static bool _identityUnavailableWarned;
    private static float _nextUpdateAt;
    private static float _nextPublishAt;
    private static float _nextHostPolicyCheckAt;
    private static string _lastStatus;
    private static string _lastRoleSignature;
    private static string _lastIdentitySignature;
    private static string _lastObservedGuestPolicyPayload;

    internal static bool CanUseBaiqInventoryFeatures
    {
        get
        {
            if (Time.unscaledTime >= _nextUpdateAt)
                Update(force: true);
            return _state.IsReady;
        }
    }

    internal static GameplaySettingsMode Mode => _state.Mode;
    internal static SteamLobbyService ActiveLobbyService => _activeSteamLobbyService;

    internal static void Update(bool force = false)
    {
        if (!force && Time.unscaledTime < _nextUpdateAt)
            return;
        _nextUpdateAt = Time.unscaledTime + ActivePollIntervalSeconds;

        try
        {
            var lobby = Lobby.Instance;
            if (lobby == null)
            {
                HandleLobbyInstanceUnavailable();
                return;
            }

            if (!lobby.IsInLobby)
            {
                HandleLobbyUnavailable();
                return;
            }

            var sessionGeneration = GetSessionGeneration();
            var localSteamId = SteamUser.GetSteamID().m_SteamID;
            var lobbyService = _activeSteamLobbyService;
            if (lobbyService == null || !lobbyService.IsInLobby)
            {
                var serviceState = lobbyService == null ? "activeSteamLobbyService" : "activeServiceMembership";
                LogIdentityUnavailable(
                    sessionGeneration,
                    0,
                    localSteamId,
                    lobby.IsHost,
                    InstanceFinder.IsServer,
                    InstanceFinder.IsClient,
                    lobbyService != null,
                    false);
                HandleIdentityUnavailable(sessionGeneration, 0, localSteamId, serviceState);
                return;
            }

            // f12 does not expose Lobby.GetLobbyData. Capture the native game
            // service instance from its Initialize/create/enter callbacks and
            // use its public metadata API, which is scoped to that service's
            // actual _lobbyID. Cross-check its owner against the Lobby wrapper.
            var sessionOwner = lobbyService.GetSessionConnectionIdentifier();
            var lobbyOwner = lobby.GetSessionConnectionIdentifier();
            var ownerSourceMatches = string.Equals(sessionOwner, lobbyOwner, StringComparison.Ordinal);
            var ownerId = ulong.TryParse(sessionOwner, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedOwner)
                ? parsedOwner
                : 0UL;
            if (ownerId == 0 || localSteamId == 0 || !ownerSourceMatches)
            {
                LogIdentityUnavailable(
                    sessionGeneration,
                    ownerId,
                    localSteamId,
                    lobby.IsHost,
                    InstanceFinder.IsServer,
                    InstanceFinder.IsClient,
                    true,
                    ownerSourceMatches);
                HandleIdentityUnavailable(
                    sessionGeneration,
                    ownerSourceMatches ? ownerId : 0,
                    localSteamId,
                    ownerSourceMatches ? null : "ownerSourceDisagreement");
                return;
            }

            _lastIdentitySignature = null;
            if (_identityUnavailableSince >= 0f)
            {
                Logger.Info(
                    $"[GameplayPolicy] Active lobby owner identity recovered; owner={Tag(ownerId)}, " +
                    $"local={Tag(localSteamId)} after={Time.unscaledTime - _identityUnavailableSince:F1}s.");
                _identityUnavailableSince = -1f;
                _identityUnavailableWarned = false;
                _lastStatus = null;
            }

            if (!_trackingLobby
                || _trackedSessionGeneration != sessionGeneration
                || _trackedOwnerId != ownerId)
                BeginTracking(sessionGeneration, ownerId);

            var isFishNetServer = InstanceFinder.IsServer;
            var isFishNetClient = InstanceFinder.IsClient;
            var lobbySaysHost = lobby.IsHost;
            var localIsSteamOwner = localSteamId == ownerId;
            LogRoleState(sessionGeneration, ownerId, localSteamId, lobbySaysHost, isFishNetServer, isFishNetClient, localIsSteamOwner);

            if (_sessionSuspended)
                ResumeSuspendedSession();

            if (lobbySaysHost && localIsSteamOwner && isFishNetServer)
            {
                UpdateHostPolicy(lobbyService, sessionGeneration, ownerId);
                return;
            }

            if (!lobbySaysHost && !localIsSteamOwner && !isFishNetServer && isFishNetClient)
            {
                if (_state.Mode == GameplaySettingsMode.Incompatible && !_policyChangedDuringSession)
                {
                    _lastStatus = null;
                    SetState(GameplaySettingsPolicy.BeginGuest(sessionGeneration, ownerId), "guest connection restored; host policy re-verification required");
                }
                UpdateGuestPolicy(lobbyService, sessionGeneration, ownerId);
                return;
            }

            EnterIncompatible(
                sessionGeneration,
                ownerId,
                "Schedule I host role, FishNet role, and the active Steam lobby owner do not agree");
        }
        catch (Exception exception)
        {
            EnterIncompatible(_trackedSessionGeneration, _trackedOwnerId,
                $"settings transport is unavailable ({exception.GetType().Name})");
        }
    }

    private static ulong GetSessionGeneration()
    {
        if (_lobbySessionGeneration == 0)
            _lobbySessionGeneration = 1;
        return _lobbySessionGeneration;
    }

    internal static void OnNativeLobbyServiceInitialized(SteamLobbyService service)
    {
        if (service == null)
            return;
        _activeSteamLobbyService = service;
        Logger.Diagnostic("[GameplayPolicy] Captured Schedule I SteamLobbyService from its native Initialize boundary.");
    }

    internal static void OnNativeLobbySessionStarted(SteamLobbyService service, string reason)
    {
        if (service != null)
            _activeSteamLobbyService = service;
        AdvanceSessionGeneration();
        _identityUnavailableSince = -1f;
        _identityUnavailableWarned = false;
        _lastRoleSignature = null;
        _lastIdentitySignature = null;
        _lastObservedGuestPolicyPayload = null;
        _lastStatus = null;
        _nextUpdateAt = 0f;

        if (_trackingLobby && _state.IsReady)
            SetState(
                GameplaySettingsPolicy.Incompatible(_trackedSessionGeneration, _trackedOwnerId),
                $"native Steam lobby entry observed ({reason}); verifying the new active session");

        Logger.Diagnostic($"[GameplayPolicy] Native Steam lobby session started; generation={GetSessionGeneration()}, event={reason}.");
    }

    internal static void OnNativeLobbySessionEnded(string reason)
    {
        _activeSteamLobbyService = null;
        AdvanceSessionGeneration();
        if (_trackingLobby || _state.Mode != GameplaySettingsMode.Local)
            EndSession($"native Steam lobby leave observed ({reason})");
        _nextUpdateAt = 0f;
        Logger.Diagnostic($"[GameplayPolicy] Native Steam lobby session ended; generation={GetSessionGeneration()}, event={reason}.");
    }

    private static void AdvanceSessionGeneration()
    {
        unchecked
        {
            _lobbySessionGeneration++;
            if (_lobbySessionGeneration == 0)
                _lobbySessionGeneration = 1;
        }
    }

    private static void HandleIdentityUnavailable(
        ulong sessionGeneration,
        ulong ownerId,
        ulong localSteamId,
        string additionalMissing = null)
    {
        var now = Time.unscaledTime;
        if (_identityUnavailableSince < 0f)
            _identityUnavailableSince = now;

        var missing = string.Join(",", new[]
        {
            ownerId == 0 ? "sessionOwner" : null,
            localSteamId == 0 ? "localSteamId" : null,
            additionalMissing
        }.Where(value => value != null));
        var elapsed = now - _identityUnavailableSince;
        var reason = $"active Steam lobby owner identity pending (missing={missing})";

        if (elapsed < IdentityWarmupSeconds && !_trackingLobby)
        {
            SetState(GameplaySettingsPolicy.LobbyIdentityPending(sessionGeneration, ownerId), reason);
            SetStatusOnce(reason + "; retrying without enabling BAIQ inventory extras");
            _nextUpdateAt = now + ActivePollIntervalSeconds;
            return;
        }

        EnterIncompatible(sessionGeneration, ownerId, reason + (elapsed >= IdentityWarmupSeconds ? " after the startup wait" : string.Empty));
        _nextUpdateAt = now + (elapsed >= IdentityWarmupSeconds ? RecoveryPollIntervalSeconds : ActivePollIntervalSeconds);
        if (elapsed >= IdentityWarmupSeconds && !_identityUnavailableWarned)
        {
            _identityUnavailableWarned = true;
            Logger.Warning(
                $"[GameplayPolicy] Active Steam lobby owner identity is still incomplete after {IdentityWarmupSeconds:F0}s " +
                $"(sessionOwner={(ownerId == 0 ? "missing" : "available")}, " +
                $"localSteamId={(localSteamId == 0 ? "missing" : "available")}); " +
                "the policy remains fail-closed and recovery polling continues.");
        }
    }

    private static void LogRoleState(
        ulong sessionGeneration,
        ulong ownerId,
        ulong localSteamId,
        bool lobbySaysHost,
        bool isFishNetServer,
        bool isFishNetClient,
        bool localIsSteamOwner)
    {
        var signature = $"{sessionGeneration}|{ownerId}|{localSteamId}|{lobbySaysHost}|{isFishNetServer}|{isFishNetClient}|{localIsSteamOwner}";
        if (string.Equals(_lastRoleSignature, signature, StringComparison.Ordinal))
            return;

        _lastRoleSignature = signature;
        Logger.Info(
            $"[GameplayPolicy] Active lobby role observed; generation={sessionGeneration}, owner={Tag(ownerId)}, local={Tag(localSteamId)}, " +
            $"scheduleHost={lobbySaysHost}, fishNetServer={isFishNetServer}, fishNetClient={isFishNetClient}, " +
            $"localIsSteamOwner={localIsSteamOwner}.");
    }

    private static void LogIdentityUnavailable(
        ulong sessionGeneration,
        ulong ownerId,
        ulong localSteamId,
        bool lobbySaysHost,
        bool isFishNetServer,
        bool isFishNetClient,
        bool serviceAvailable,
        bool ownerSourceMatches)
    {
        var signature = $"{sessionGeneration}|{ownerId == 0}|{localSteamId == 0}|{lobbySaysHost}|{isFishNetServer}|{isFishNetClient}|{serviceAvailable}|{ownerSourceMatches}";
        if (string.Equals(_lastIdentitySignature, signature, StringComparison.Ordinal))
            return;

        _lastIdentitySignature = signature;
        Logger.Info(
            $"[GameplayPolicy] Active Steam lobby observed; generation={sessionGeneration}, membership=active, " +
            $"owner={(ownerId == 0 ? "missing" : "available")}, localSteamId={(localSteamId == 0 ? "missing" : "available")}, " +
            $"activeService={(serviceAvailable ? "available" : "missing")}, ownerSourceMatch={ownerSourceMatches}, " +
            $"scheduleHost={lobbySaysHost}, fishNetServer={isFishNetServer}, fishNetClient={isFishNetClient}; " +
            "inventory extras remain paused until owner and role checks agree.");
    }

    internal static void ReapplyEffectivePolicy(GameplaySettingsSnapshot localSettings)
    {
        if (_state.Mode == GameplaySettingsMode.Local)
            SetState(GameplaySettingsPolicy.Local(localSettings), "local preferences changed");
        else
            ApplyState(_state, "local preference edit; active session policy remains frozen");
    }

    internal static void EndSession(string reason)
    {
        if (!_trackingLobby && _state.Mode == GameplaySettingsMode.Local)
            return;

        _trackingLobby = false;
        _activeSteamLobbyService = null;
        _sessionSuspended = false;
        _noLobbySince = -1f;
        _trackedSessionGeneration = 0;
        _trackedOwnerId = 0;
        _hostPolicyInitialized = false;
        _hostPolicyPublished = false;
        _policyChangedDuringSession = false;
        _frozenHostState = default;
        _frozenGuestState = default;
        _identityUnavailableSince = -1f;
        _identityUnavailableWarned = false;
        _policyNonce = Guid.NewGuid().ToString("N");
        _lastRoleSignature = null;
        _lastIdentitySignature = null;
        _lastObservedGuestPolicyPayload = null;
        _lastStatus = null;
        SetState(GameplaySettingsPolicy.EndSession(Configuration.Instance.GetLocalGameplaySettings()), reason);
        Logger.Info($"[GameplayPolicy] Session ended; local gameplay preferences restored ({reason}).");
    }

    private static void BeginTracking(ulong sessionGeneration, ulong ownerId)
    {
        _trackingLobby = true;
        _sessionSuspended = false;
        _noLobbySince = -1f;
        _trackedSessionGeneration = sessionGeneration;
        _trackedOwnerId = ownerId;
        _hostPolicyInitialized = false;
        _hostPolicyPublished = false;
        _policyChangedDuringSession = false;
        _frozenHostState = default;
        _frozenGuestState = default;
        _policyNonce = Guid.NewGuid().ToString("N");
        _nextPublishAt = 0f;
        _nextHostPolicyCheckAt = 0f;
        _lastRoleSignature = null;
        _lastStatus = null;
        SetState(GameplaySettingsPolicy.BeginGuest(sessionGeneration, ownerId), "waiting for authenticated host policy");
        Logger.Info(
            $"[GameplayPolicy] Active lobby session changed; generation={sessionGeneration}, owner={Tag(ownerId)}, " +
            "effective=Vanilla/production-off until the current Steam owner policy is verified.");
    }

    private static void UpdateHostPolicy(SteamLobbyService lobbyService, ulong sessionGeneration, ulong ownerId)
    {
        if (!_hostPolicyInitialized)
        {
            var localSettings = Configuration.Instance.GetLocalGameplaySettings();
            var envelope = GameplaySettingsPolicy.CreateEnvelope(
                ModVersion,
                ownerId,
                _policyNonce,
                localSettings);
            if (envelope == null)
            {
                EnterIncompatible(sessionGeneration, ownerId, "local gameplay settings could not be normalized");
                return;
            }

            _hostPolicyInitialized = true;
            _frozenHostState = GameplaySettingsPolicy.BeginHost(sessionGeneration, ownerId, localSettings, envelope);
            SetState(GameplaySettingsPolicy.HostPending(sessionGeneration, ownerId), "host policy frozen; waiting for owner-authenticated publication");
            Logger.Warning(
                $"[GameplayPolicy] Host policy frozen for this Steam lobby; generation={sessionGeneration}, owner={Tag(ownerId)}, " +
                $"target={Format(localSettings)}. Host gameplay remains safely paused until Steam metadata is published and read back; " +
                "unmodded or mismatched clients are not synchronized and remain an unsupported inventory-policy pairing.");
        }

        if (_hostPolicyPublished)
        {
            if (_state.Mode == GameplaySettingsMode.Host && Time.unscaledTime < _nextHostPolicyCheckAt)
                return;

            var current = lobbyService.GetLobbyData(MetadataKey);
            if (string.Equals(current, _frozenHostState.AcceptedEnvelope, StringComparison.Ordinal))
            {
                _nextHostPolicyCheckAt = Time.unscaledTime + HostPolicyRecheckIntervalSeconds;
                if (_state.Mode != GameplaySettingsMode.Host)
                    SetState(_frozenHostState, "host role and published policy re-verified");
                return;
            }

            _hostPolicyPublished = false;
            _nextPublishAt = Time.unscaledTime;
            SetState(GameplaySettingsPolicy.HostPending(sessionGeneration, ownerId), "published host policy is absent or changed; republishing the frozen policy");
            SetStatusOnce("published host policy was not present on readback; host inventory features remain paused while the frozen policy is republished");
        }

        if (Time.unscaledTime < _nextPublishAt)
            return;

        _nextPublishAt = Time.unscaledTime + 2f;
        // The game's active lobby service targets its private, current _lobbyID.
        // The Steam API accepts writes only from that lobby's owner.
        lobbyService.SetLobbyData(MetadataKey, _frozenHostState.AcceptedEnvelope);
        var readback = lobbyService.GetLobbyData(MetadataKey);
        if (string.Equals(readback, _frozenHostState.AcceptedEnvelope, StringComparison.Ordinal))
        {
            _hostPolicyPublished = true;
            _nextHostPolicyCheckAt = Time.unscaledTime + HostPolicyRecheckIntervalSeconds;
            _lastStatus = null;
            SetState(_frozenHostState, "host policy published by current owner and exact readback verified");
            Logger.Info(
                $"[GameplayPolicy] Host policy publication verified; generation={sessionGeneration}, owner={Tag(ownerId)}, " +
                $"readback=exact, effective={Format(_frozenHostState.EffectiveSettings)}.");
        }
        else
        {
            SetState(GameplaySettingsPolicy.HostPending(sessionGeneration, ownerId), "host policy publication/readback failed; retrying fail-closed");
            SetStatusOnce("Steam accepted the policy call but exact readback did not match; host and guest BAIQ inventory extras remain paused");
            Logger.Diagnostic($"[GameplayPolicy] Host policy publication/readback remains unverified; retry=2s, generation={sessionGeneration}, owner={Tag(ownerId)}.");
        }
    }

    private static void UpdateGuestPolicy(SteamLobbyService lobbyService, ulong sessionGeneration, ulong ownerId)
    {
        if (_state.Mode == GameplaySettingsMode.Incompatible && _policyChangedDuringSession)
            return;

        if (_state.Mode is not (GameplaySettingsMode.GuestPending or GameplaySettingsMode.GuestReady))
            SetState(GameplaySettingsPolicy.BeginGuest(sessionGeneration, ownerId), "guest policy verification restarted");

        var payload = lobbyService.GetLobbyData(MetadataKey);
        if (string.IsNullOrWhiteSpace(payload))
        {
            if (_state.Mode == GameplaySettingsMode.GuestReady
                || (_frozenGuestState.Mode == GameplaySettingsMode.GuestReady
                    && _frozenGuestState.SessionGeneration == sessionGeneration
                    && _frozenGuestState.OwnerId == ownerId))
            {
                _policyChangedDuringSession = true;
                EnterIncompatible(sessionGeneration, ownerId, "verified host policy metadata disappeared during this lobby");
                return;
            }
            SetStatusOnce("waiting for host policy metadata; BAIQ Backpack, sort, and quick-move features are paused");
            return;
        }

        var newPayload = !string.Equals(_lastObservedGuestPolicyPayload, payload, StringComparison.Ordinal);
        if (newPayload)
        {
            _lastObservedGuestPolicyPayload = payload;
            Logger.Info(
                $"[GameplayPolicy] Guest policy metadata discovered; generation={sessionGeneration}, owner={Tag(ownerId)}, " +
                $"length={payload.Length}; schema, version, owner binding, nonce and values are being validated.");
        }

        if (_frozenGuestState.Mode == GameplaySettingsMode.GuestReady
            && _frozenGuestState.SessionGeneration == sessionGeneration
            && _frozenGuestState.OwnerId == ownerId
            && !string.Equals(_frozenGuestState.AcceptedEnvelope, payload, StringComparison.Ordinal))
        {
            _policyChangedDuringSession = true;
            if (newPayload)
                Logger.Diagnostic("[GameplayPolicy] Guest policy metadata rejected because the host changed its frozen settings during this session.");
            EnterIncompatible(sessionGeneration, ownerId,
                "host policy changed while gameplay policy identity was being re-verified; reconnect before applying a new policy");
            return;
        }

        if (!GameplaySettingsPolicy.TryAcceptGuestEnvelope(_state, payload, ModVersion, out var accepted))
        {
            if (newPayload)
                Logger.Diagnostic(
                    $"[GameplayPolicy] Guest policy metadata rejected by schema/version/owner/value checks; " +
                    $"generation={sessionGeneration}, owner={Tag(ownerId)}.");
            if (_state.Mode == GameplaySettingsMode.GuestReady || _frozenGuestState.Mode == GameplaySettingsMode.GuestReady)
            {
                _policyChangedDuringSession = true;
                EnterIncompatible(sessionGeneration, ownerId,
                    "host policy changed during this lobby; reconnect before applying a new policy");
            }
            else
            {
                SetStatusOnce(
                    "host policy is missing, malformed, unsupported, stale, or version-mismatched; " +
                    "effective=Vanilla/production-off and BAIQ inventory extras are paused");
            }
            return;
        }

        if (_state.Mode != GameplaySettingsMode.GuestReady)
        {
            _frozenGuestState = accepted;
            _lastStatus = null;
            SetState(accepted, "authenticated host gameplay policy accepted");
            Logger.Info(
                $"[GameplayPolicy] Host policy accepted; generation={sessionGeneration}, owner={Tag(ownerId)}, " +
                $"effective={Format(accepted.EffectiveSettings)}. Local preferences were not modified.");
        }
    }

    private static void EnterIncompatible(ulong sessionGeneration, ulong ownerId, string reason)
    {
        if (sessionGeneration != 0 && ownerId != 0
            && (!_trackingLobby || _trackedSessionGeneration != sessionGeneration || _trackedOwnerId != ownerId))
            BeginTracking(sessionGeneration, ownerId);

        var state = GameplaySettingsPolicy.Incompatible(sessionGeneration, ownerId);
        if (_state != state)
            SetState(state, reason);
        SetStatusOnce(reason + "; effective=Vanilla/production-off and BAIQ inventory extras are paused");
    }

    private static void HandleLobbyUnavailable()
    {
        if (!_trackingLobby)
        {
            if (_state.Mode != GameplaySettingsMode.Local)
                EndSession("not in a Steam lobby");
            return;
        }

        if (!_sessionSuspended)
        {
            _sessionSuspended = true;
            _noLobbySince = Time.unscaledTime;
            SetState(
                GameplaySettingsPolicy.Incompatible(_trackedSessionGeneration, _trackedOwnerId),
                "Steam lobby/network state became unavailable; policy is paused fail-closed");
            return;
        }

        if (_noLobbySince >= 0f && Time.unscaledTime - _noLobbySince >= 2f)
            EndSession("Steam lobby membership ended");
    }

    private static void HandleLobbyInstanceUnavailable()
    {
        if (!_trackingLobby)
            return;

        if (!_sessionSuspended)
        {
            _sessionSuspended = true;
            _noLobbySince = Time.unscaledTime;
            _lastStatus = null;
            SetState(
                GameplaySettingsPolicy.Incompatible(_trackedSessionGeneration, _trackedOwnerId),
                "Steam lobby wrapper is temporarily unavailable; policy paused while membership is rechecked");
        }
        SetStatusOnce("Steam lobby wrapper is unavailable; BAIQ inventory extras remain paused until lobby membership and policy recover");
    }

    internal static void OnMainSceneTransition(string reason)
    {
        if (!_trackingLobby || _sessionSuspended)
            return;

        _sessionSuspended = true;
        _noLobbySince = Time.unscaledTime;
        _lastStatus = null;
        SetState(
            GameplaySettingsPolicy.Incompatible(_trackedSessionGeneration, _trackedOwnerId),
            $"Main save scene transitioned ({reason}); re-verifying the current lobby policy");
    }

    private static void ResumeSuspendedSession()
    {
        _sessionSuspended = false;
        _noLobbySince = -1f;
        _lastStatus = null;
        if (_frozenHostState.Mode == GameplaySettingsMode.Host
            && _frozenHostState.SessionGeneration == _trackedSessionGeneration
            && _frozenHostState.OwnerId == _trackedOwnerId)
        {
            _hostPolicyPublished = false;
            _nextPublishAt = 0f;
            SetState(
                GameplaySettingsPolicy.HostPending(_trackedSessionGeneration, _trackedOwnerId),
                "host lobby resumed; frozen policy will be republished and read back before enabling inventory features");
        }
        else
        {
            SetState(
                GameplaySettingsPolicy.BeginGuest(_trackedSessionGeneration, _trackedOwnerId),
                "guest must re-verify host policy after a transport interruption");
        }
    }

    private static void SetState(GameplaySettingsSessionState state, string reason)
    {
        var previous = _state;
        var wasReady = _state.IsReady;
        var changed = _state != state;
        if (!changed)
            return;

        _state = state;
        ApplyState(state, reason);
        Logger.Info(
            $"[GameplayPolicy] Mode transition {previous.Mode}->{state.Mode}; ready={state.IsReady}, " +
            $"generation={state.SessionGeneration}, owner={Tag(state.OwnerId)}, reason={reason}.");
        if (wasReady != state.IsReady)
            BackpackMod.OnGameplayPolicyReadinessChanged(state.IsReady, reason);
    }

    private static void ApplyState(GameplaySettingsSessionState state, string reason)
    {
        Configuration.Instance.ApplyGameplaySettings(state.EffectiveSettings);
        Configuration.Instance.UpdateGameplaySettingsDescriptions(state.Mode, state.EffectiveSettings);
        Logger.Diagnostic($"[GameplayPolicy] Effective values applied ({reason}): {Format(state.EffectiveSettings)}; mode={state.Mode}.");
    }

    private static void SetStatusOnce(string message)
    {
        if (string.Equals(_lastStatus, message, StringComparison.Ordinal))
            return;
        _lastStatus = message;
        Logger.Warning($"[GameplayPolicy] {message}.");
    }

    private static string Format(GameplaySettingsSnapshot settings) =>
        $"stack={settings.StackSizeMultiplier}x,drying={Flag(settings.DryingRacks)}," +
        $"mixing={Flag(settings.MixingStations)},plants={Flag(settings.PlantYield)}," +
        $"packaging={Flag(settings.PackagingStations)}";

    private static string Flag(bool value) => value ? "on" : "off";

    private static string Tag(ulong identity)
    {
        if (identity == 0)
            return "missing";

        var digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(identity.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(digest.AsSpan(0, 4));
    }
}
