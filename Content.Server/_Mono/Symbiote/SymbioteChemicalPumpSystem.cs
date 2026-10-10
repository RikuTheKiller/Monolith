using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._Mono.Symbiote;
using Content.Shared.Mind;
using Robust.Server.Player;

namespace Content.Server._Mono.Symbiote;

/// <summary>
/// Restores blood and brings ghosts back for the Chemical Pump, which only the server can do.
/// </summary>
public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    protected override void RestoreBlood(EntityUid host, float percentage)
    {
        if (TryComp<BloodstreamComponent>(host, out var bloodstream))
            _bloodstream.TryModifyBloodLevel(host, bloodstream.BloodMaxVolume * percentage / 100f, bloodstream);
    }

    protected override void OnRevived(EntityUid host)
    {
        // Let a ghosted host know they can come back, like a defibrillator does
        if (_mind.TryGetMind(host, out _, out var mind)
            && mind.CurrentEntity != host
            && _player.TryGetSessionById(mind.UserId, out var session))
        {
            _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _player), session);
        }
    }
}
