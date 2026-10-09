using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Emoting;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Shared._Mono.Symbiote;

public abstract partial class SharedSymbioteSystem
{
    private void InitializeBonding()
    {
        SubscribeLocalEvent<SymbioteComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<SymbioteComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SymbioteComponent, SymbioteEatDoAfterEvent>(OnEatDoAfter);
        SubscribeLocalEvent<SymbioteComponent, SymbioteDebugBondActionEvent>(OnDebugBondAction);
        SubscribeLocalEvent<SymbioteComponent, SymbioteLeaveHostActionEvent>(OnLeaveHostAction);
        SubscribeLocalEvent<SymbioteComponent, EntGotRemovedFromContainerMessage>(OnRemovedFromContainer);
        SubscribeLocalEvent<SymbioteComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<SymbioteComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<SymbioteComponent, EmoteAttemptEvent>(OnEmoteAttempt);
    }

    /// <summary>
    /// Whether the symbiote has a host. If it doesn't, shows a popup saying so.
    /// </summary>
    protected bool CheckHasHost(Entity<SymbioteComponent> ent, out EntityUid host)
    {
        if (ent.Comp.Host is { } symbioteHost)
        {
            host = symbioteHost;
            return true;
        }

        host = default;
        _popup.PopupClient(Loc.GetString("symbiote-no-host"), ent, ent);
        return false;
    }

    #region Eating

    private void OnUseInHand(Entity<SymbioteComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartEating(ent, args.User, args.User);
    }

    private void OnAfterInteract(Entity<SymbioteComponent> ent, ref AfterInteractEvent args)
    {
        // Like any other food, only things with a body are worth trying to feed it to
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<BodyComponent>(target))
            return;

        args.Handled = TryStartEating(ent, args.User, target);
    }

    /// <summary>
    /// Starts <paramref name="user"/> feeding the symbiote to <paramref name="target"/>, who may be the user themselves.
    /// </summary>
    public bool TryStartEating(Entity<SymbioteComponent> ent, EntityUid user, EntityUid target)
    {
        if (!CanBeEaten(ent, user, target))
            return false;

        var feeding = user != target;
        var doAfterArgs = new DoAfterArgs(EntityManager, user, feeding ? ent.Comp.FeedDelay : ent.Comp.EatDelay, new SymbioteEatDoAfterEvent(), ent, target, ent)
        {
            BreakOnMove = feeding,
            BreakOnDamage = true,
            MovementThreshold = 0.01f,
            DistanceThreshold = 1.5f,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return false;

        var userIdentity = Identity.Entity(user, EntityManager);

        if (!feeding)
        {
            _popup.PopupPredicted(Loc.GetString("symbiote-eat-start"),
                Loc.GetString("symbiote-eat-start-others", ("user", userIdentity)),
                user,
                user);

            return true;
        }

        var targetIdentity = Identity.Entity(target, EntityManager);
        var othersFilter = Filter.PvsExcept(user, entityManager: EntityManager).RemovePlayerByAttachedEntity(target);

        _popup.PopupClient(Loc.GetString("symbiote-feed-start", ("target", targetIdentity)), user, user);
        _popup.PopupEntity(Loc.GetString("symbiote-feed-start-target", ("user", userIdentity)), target, target, PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("symbiote-feed-start-others", ("user", userIdentity), ("target", targetIdentity)), target, othersFilter, true);

        _adminLog.Add(LogType.ForceFeed, LogImpact.Medium, $"{ToPrettyString(user):user} is feeding {ToPrettyString(ent):symbiote} to {ToPrettyString(target):target}");

        return true;
    }

    private bool CanBeEaten(Entity<SymbioteComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.Host != null)
            return false;

        if (_mobState.IsDead(ent))
        {
            _popup.PopupClient(Loc.GetString("symbiote-eat-dead"), user, user);
            return false;
        }

        if (!CheckHost(target, user))
            return false;

        // Blocked the same way as any other food
        return !IsMouthBlocked(target, user);
    }

    /// <summary>
    /// Checks whether the target can be a host, telling the user why not if it can't.
    /// </summary>
    private bool CheckHost(EntityUid target, EntityUid user)
    {
        var targetIdentity = Identity.Entity(target, EntityManager);

        if (GetSharedHostProblem(target) is { } sharedProblem)
        {
            _popup.PopupClient(Loc.GetString(sharedProblem, ("target", targetIdentity)), user, user);
            return false;
        }

        // Only the server knows about these, the client assumes they pass
        if (GetBodyHostProblem(target) is { } bodyProblem)
        {
            _popup.PopupEntity(Loc.GetString(bodyProblem, ("target", targetIdentity)), user, user);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the target can be a host: a humanoid with a bloodstream and a head, without a symbiote.
    /// Always passes the bloodstream and head checks on the client.
    /// </summary>
    public bool IsValidHost(EntityUid target)
    {
        return GetHostProblem(target) == null;
    }

    /// <summary>
    /// Gets the reason the target can't be a host, or null if it can.
    /// If there are several, the first one checked wins.
    /// </summary>
    /// <param name="target">The potential host.</param>
    /// <param name="currentHost">Whether the target is already this symbiote's host, so already having one is fine.</param>
    public LocId? GetHostProblem(EntityUid target, bool currentHost = false)
    {
        return GetSharedHostProblem(target, currentHost) ?? GetBodyHostProblem(target);
    }

    private LocId? GetSharedHostProblem(EntityUid target, bool currentHost = false)
    {
        if (!HasComp<HumanoidAppearanceComponent>(target))
            return "symbiote-host-problem-not-humanoid";

        if (!currentHost && HasComp<SymbioteHostComponent>(target))
            return "symbiote-host-problem-has-symbiote";

        return null;
    }

    /// <summary>
    /// Server-only host checks, like having a bloodstream and a head.
    /// </summary>
    protected virtual LocId? GetBodyHostProblem(EntityUid target)
    {
        return null;
    }

    /// <summary>
    /// Whether something covers the target's mouth. Server-only, so the client assumes it isn't.
    /// </summary>
    protected virtual bool IsMouthBlocked(EntityUid target, EntityUid popupUser)
    {
        return false;
    }

    #endregion

    #region Bonding

    private void OnEatDoAfter(Entity<SymbioteComponent> ent, ref SymbioteEatDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } target)
            return;

        if (!CanBeEaten(ent, args.User, target))
            return;

        args.Handled = Bond(ent, target, popup: true, user: args.User);
    }

    private void OnDebugBondAction(Entity<SymbioteComponent> ent, ref SymbioteDebugBondActionEvent args)
    {
        if (args.Handled || ent.Comp.Host != null || !CheckHost(args.Target, ent))
            return;

        args.Handled = Bond(ent, args.Target, popup: true, user: ent);
    }

    /// <summary>
    /// Puts the symbiote inside the target's skull, without any checks.
    /// </summary>
    /// <param name="ent">The symbiote.</param>
    /// <param name="target">The new host.</param>
    /// <param name="popup">Whether to tell the symbiote and host about it, which only makes sense for a new bond.</param>
    /// <param name="user">Whoever caused the bonding and is predicting it, if anyone.</param>
    public bool Bond(Entity<SymbioteComponent> ent, EntityUid target, bool popup, EntityUid? user = null)
    {
        var hostComp = EnsureComp<SymbioteHostComponent>(target);
        var container = _container.EnsureContainer<ContainerSlot>(target, SymbioteHostComponent.ContainerId);

        if (!_container.Insert(ent.Owner, container))
        {
            RemComp<SymbioteHostComponent>(target);
            return false;
        }

        hostComp.Symbiote = ent;
        ent.Comp.Host = target;
        Dirty(target, hostComp);
        Dirty(ent);

        if (ent.Comp.AddOnBond != null)
            EntityManager.AddComponents(ent, ent.Comp.AddOnBond);

        if (ent.Comp.RemoveOnBond != null)
            EntityManager.RemoveComponents(ent, ent.Comp.RemoveOnBond);

        OnBonded(ent, target);

        if (popup)
        {
            PopupPredictedBy(Loc.GetString("symbiote-bond", ("target", Identity.Entity(target, EntityManager))), ent, user);
            PopupPredictedBy(Loc.GetString("symbiote-bond-target"), target, user);
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent):symbiote} bonded with {ToPrettyString(target):host}");

        return true;
    }

    /// <summary>
    /// Server-only effects of bonding, like syncing the symbiote's temperature to its host's.
    /// </summary>
    protected virtual void OnBonded(Entity<SymbioteComponent> ent, EntityUid host)
    {
    }

    /// <summary>
    /// Shows a popup to the recipient once: from their own client if they're predicting it, otherwise from the server.
    /// </summary>
    private void PopupPredictedBy(string message, EntityUid recipient, EntityUid? user)
    {
        if (recipient == user)
            _popup.PopupClient(message, recipient, recipient, PopupType.Medium);
        else
            _popup.PopupEntity(message, recipient, recipient, PopupType.Medium);
    }

    #endregion

    #region Leaving

    private void OnLeaveHostAction(Entity<SymbioteComponent> ent, ref SymbioteLeaveHostActionEvent args)
    {
        if (args.Handled || !CheckHasHost(ent, out var host) || !TryLeaveHost(ent))
            return;

        args.Handled = true;
        MakeHostVomit(host);
    }

    /// <summary>
    /// Makes the host vomit after a symbiote crawls out. Only does anything on the server.
    /// </summary>
    /// <summary>
    /// Called when the symbiote leaves its host, however it left, after its abilities have ended.
    /// </summary>
    /// <param name="ent">The symbiote.</param>
    /// <param name="host">The host it left.</param>
    protected virtual void OnLeftHost(Entity<SymbioteComponent> ent, EntityUid host)
    {
    }

    protected virtual void MakeHostVomit(EntityUid host)
    {
    }

    /// <summary>
    /// Takes the symbiote out of its host, into whatever the host is in if it fits.
    /// The rest of the cleanup happens when it leaves the container.
    /// </summary>
    public bool TryLeaveHost(Entity<SymbioteComponent> ent)
    {
        if (ent.Comp.Host is not { } host)
            return false;

        // Removing also puts it into whatever the host is in, like a crate, if that can hold it
        if (!_container.TryGetContainer(host, SymbioteHostComponent.ContainerId, out var container)
            || !_container.Remove(ent.Owner, container))
            return false;

        _popup.PopupClient(Loc.GetString("symbiote-leave", ("target", Identity.Entity(host, EntityManager))), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("symbiote-leave-target"), host, host, PopupType.Medium);

        return true;
    }

    private void OnRemovedFromContainer(Entity<SymbioteComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != SymbioteHostComponent.ContainerId || ent.Comp.Host is not { } host)
            return;

        // Abilities only work inside a host, however the symbiote left it
        DeactivateAllAbilities(ent);
        OnLeftHost(ent, host);

        // This also runs when the symbiote or its host gets deleted, since that takes the symbiote out of the container
        if (!TerminatingOrDeleted(host))
            RemComp<SymbioteHostComponent>(host);

        if (TerminatingOrDeleted(ent))
            return;

        ent.Comp.Host = null;
        Dirty(ent);

        if (ent.Comp.RemoveOnBond != null)
            EntityManager.AddComponents(ent, ent.Comp.RemoveOnBond);

        if (ent.Comp.AddOnBond != null)
            EntityManager.RemoveComponents(ent, ent.Comp.AddOnBond);

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(ent):symbiote} left {ToPrettyString(host):host}");
    }

    #endregion

    private void OnAttackAttempt(Entity<SymbioteComponent> ent, ref AttackAttemptEvent args)
    {
        // Abilities are the only way to fight from inside a host
        if (ent.Comp.Host != null)
            args.Cancel();
    }

    private void OnEmoteAttempt(Entity<SymbioteComponent> ent, ref EmoteAttemptEvent args)
    {
        // Nobody can see a symbiote inside someone's head, so it can't emote
        if (ent.Comp.Host != null)
            args.Cancel();
    }

    private void OnInteractionAttempt(Entity<SymbioteComponent> ent, ref InteractionAttemptEvent args)
    {
        // The symbiote can't touch its host, like by pulling or nudging them
        if (ent.Comp.Host is { } host && args.Target == host)
            args.Cancelled = true;
    }
}
