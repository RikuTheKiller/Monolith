using System.Linq;
using Content.Server._NF.Bank;
using System.Numerics;
using Content.Server.Advertise;
using Content.Server.Advertise.Components;
using Content.Server.Cargo.Systems;
//using Content.Server.Emp; // Frontier: Upstream - #28984
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared._NF.Bank.Components; // Frontier
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.Emp;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines;
using Content.Shared.Wall;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Content.Server.Administration.Logs; // Frontier
using Content.Shared.Database; // Frontier
using Content.Shared._NF.Bank.BUI; // Frontier
using Content.Server._NF.Contraband.Systems; // Frontier
using Content.Shared.Stacks; // Frontier
using Content.Server.Stack;
using Content.Server._Mono.VendingMachine;
using Robust.Shared.Containers; // Frontier

namespace Content.Server.VendingMachines
{
    public sealed class VendingMachineSystem : SharedVendingMachineSystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;
        [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly SpeakOnUIClosedSystem _speakOnUIClosed = default!;
        [Dependency] private readonly SharedPointLightSystem _light = default!;
        [Dependency] private readonly EmagSystem _emag = default!;

        [Dependency] private readonly IPrototypeManager _prototypeManager = default!; // Frontier
        [Dependency] private readonly SharedAudioSystem _audioSystem = default!; // Frontier
        [Dependency] private readonly BankSystem _bankSystem = default!; // Frontier
        [Dependency] private readonly PopupSystem _popupSystem = default!; // Frontier
        [Dependency] private readonly IAdminLogManager _adminLogger = default!; // Frontier
        [Dependency] private readonly ContrabandTurnInSystem _contraband = default!; // Frontier
        [Dependency] private readonly StackSystem _stack = default!; // Frontier
        [Dependency] private readonly VendingMachinePurchaseSystem _vendingPurchase = default!; // Mono

        private const float WallVendEjectDistanceFromWall = 1f;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<VendingMachineComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<VendingMachineComponent, BreakageEventArgs>(OnBreak);
            SubscribeLocalEvent<VendingMachineComponent, DamageChangedEvent>(OnDamageChanged);
            SubscribeLocalEvent<VendingMachineComponent, PriceCalculationEvent>(OnVendingPrice);
            //SubscribeLocalEvent<VendingMachineComponent, EmpPulseEvent>(OnEmpPulse); // Frontier: Upstream - #28984
            SubscribeLocalEvent<VendingMachineComponent, EntInsertedIntoContainerMessage>(OnEntityInserted); // Frontier
            SubscribeLocalEvent<VendingMachineComponent, EntRemovedFromContainerMessage>(OnEntityRemoved); // Frontier

            SubscribeLocalEvent<VendingMachineComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);

            Subs.BuiEvents<VendingMachineComponent>(VendingMachineUiKey.Key, subs =>
            {
                subs.Event<VendingMachineEjectMessage>(OnInventoryEjectMessage);
            });

            SubscribeLocalEvent<VendingMachineComponent, VendingMachineSelfDispenseEvent>(OnSelfDispense);

            SubscribeLocalEvent<VendingMachineComponent, RestockDoAfterEvent>(OnDoAfter);

            SubscribeLocalEvent<VendingMachineRestockComponent, PriceCalculationEvent>(OnPriceCalculation);
        }

        private void OnVendingPrice(EntityUid uid, VendingMachineComponent component, ref PriceCalculationEvent args)
        {
            var price = 0.0;

            foreach (var entry in component.Inventory.Values)
            {
                if (!PrototypeManager.TryIndex<EntityPrototype>(entry.ID, out var proto))
                {
                    Log.Error($"Unable to find entity prototype {entry.ID} on {ToPrettyString(uid)} vending.");
                    continue;
                }

                price += entry.Amount; //* _pricing.GetEstimatedPrice(proto); Frontier - This is used to price the worth of a vending machine with the inventory it has.
            }

            //args.Price += price; Frontier - This is used to price the worth of a vending machine with the inventory it has.
        }

        protected override void OnMapInit(EntityUid uid, VendingMachineComponent component, MapInitEvent args)
        {
            base.OnMapInit(uid, component, args);

            if (HasComp<ApcPowerReceiverComponent>(uid))
            {
                TryUpdateVisualState(uid, component);
            }
        }

        private void OnActivatableUIOpenAttempt(EntityUid uid, VendingMachineComponent component, ActivatableUIOpenAttemptEvent args)
        {
            if (component.Broken)
                args.Cancel();
        }

        private void OnInventoryEjectMessage(EntityUid uid, VendingMachineComponent component, VendingMachineEjectMessage args)
        {
            if (!this.IsPowered(uid, EntityManager))
                return;

            if (args.Actor is not { Valid: true } entity || Deleted(entity))
                return;

            if (component.Ejecting)
                return;

            AuthorizedVend(uid, entity, args.Type, args.ID, component);
        }

        private void OnPowerChanged(EntityUid uid, VendingMachineComponent component, ref PowerChangedEvent args)
        {
            TryUpdateVisualState(uid, component);
        }

        private void OnBreak(EntityUid uid, VendingMachineComponent vendComponent, BreakageEventArgs eventArgs)
        {
            vendComponent.Broken = true;
            TryUpdateVisualState(uid, vendComponent);
        }

        private void OnDamageChanged(EntityUid uid, VendingMachineComponent component, DamageChangedEvent args)
        {
            if (!args.DamageIncreased && component.Broken)
            {
                component.Broken = false;
                TryUpdateVisualState(uid, component);
                return;
            }

            if (component.Broken || component.DispenseOnHitCoolingDown ||
                component.DispenseOnHitChance == null || args.DamageDelta == null)
                return;

            if (args.DamageIncreased && args.DamageDelta.GetTotal() >= component.DispenseOnHitThreshold &&
                _random.Prob(component.DispenseOnHitChance.Value))
            {
                if (component.DispenseOnHitCooldown > 0f)
                    component.DispenseOnHitCoolingDown = true;
                EjectRandom(uid, throwItem: true, forceEject: true, component);
            }
        }

        private void OnSelfDispense(EntityUid uid, VendingMachineComponent component, VendingMachineSelfDispenseEvent args)
        {
            if (args.Handled)
                return;

            args.Handled = true;
            EjectRandom(uid, throwItem: true, forceEject: false, component);
        }

        private void OnDoAfter(EntityUid uid, VendingMachineComponent component, DoAfterEvent args)
        {
            if (args.Handled || args.Cancelled || args.Args.Used == null)
                return;

            if (!TryComp<VendingMachineRestockComponent>(args.Args.Used, out var restockComponent))
            {
                Log.Error($"{ToPrettyString(args.Args.User)} tried to restock {ToPrettyString(uid)} with {ToPrettyString(args.Args.Used.Value)} which did not have a VendingMachineRestockComponent.");
                return;
            }

            TryRestockInventory(uid, component);

            Popup.PopupEntity(Loc.GetString("vending-machine-restock-done", ("this", args.Args.Used), ("user", args.Args.User), ("target", uid)), args.Args.User, PopupType.Medium);

            Audio.PlayPvs(restockComponent.SoundRestockDone, uid, AudioParams.Default.WithVolume(-2f).WithVariation(0.2f));

            Del(args.Args.Used.Value);

            args.Handled = true;
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.CanShoot"/> property of the vending machine.
        /// </summary>
        public void SetShooting(EntityUid uid, bool canShoot, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.CanShoot = canShoot;
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.Contraband"/> property of the vending machine.
        /// </summary>
        public void SetContraband(EntityUid uid, bool contraband, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.Contraband = contraband;
            Dirty(uid, component);
        }

        public void Deny(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            if (vendComponent.Denying)
                return;

            vendComponent.Denying = true;
            Audio.PlayPvs(vendComponent.SoundDeny, uid, AudioParams.Default.WithVolume(-2f));
            TryUpdateVisualState(uid, vendComponent);
        }

        /// <summary>
        /// Checks if the user is authorized to use this vending machine
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="sender">Entity trying to use the vending machine</param>
        /// <param name="vendComponent"></param>
        public bool IsAuthorized(EntityUid uid, EntityUid sender, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return false;

            if (!TryComp<AccessReaderComponent>(uid, out var accessReader))
                return true;

            if (_accessReader.IsAllowed(sender, uid, accessReader))
                return true;

            Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-access-denied"), uid);
            Deny(uid, vendComponent);
            return false;
        }

        /// <summary>
        /// Tries to eject the provided item. Will do nothing if the vending machine is incapable of ejecting, already ejecting
        /// or the item doesn't exist in its inventory.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        /// <param name="throwItem">Whether the item should be thrown in a random direction after ejection</param>
        /// <param name="vendComponent"></param>
        public bool TryEjectVendorItem(EntityUid uid, InventoryType type, string itemId, bool throwItem, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return false;

            if (vendComponent.Ejecting || vendComponent.Broken || !this.IsPowered(uid, EntityManager))
            {
                return false;
            }

            var entry = GetEntry(uid, itemId, type, vendComponent);

            if (entry == null)
            {
                Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-invalid-item"), uid);
                Deny(uid, vendComponent);
                return false;
            }

            if (entry.Amount <= 0)
            {
                Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-out-of-stock"), uid);
                Deny(uid, vendComponent);
                return false;
            }

            if (string.IsNullOrEmpty(entry.ID))
                return false;

            if (!TryComp<TransformComponent>(vendComponent.Owner, out var transformComp))
                return false;

            // Start Ejecting, and prevent users from ordering while anim playing
            vendComponent.Ejecting = true;
            vendComponent.NextItemToEject = entry.ID;
            vendComponent.ThrowNextItem = throwItem;

            if (TryComp(uid, out SpeakOnUIClosedComponent? speakComponent))
                _speakOnUIClosed.TrySetFlag((uid, speakComponent));

            // Frontier: unlimited vending
            // Infinite supplies must stay infinite.
            if (entry.Amount != uint.MaxValue)
                entry.Amount--;
            // End Frontier

            Dirty(uid, vendComponent);
            TryUpdateVisualState(uid, vendComponent);
            Audio.PlayPvs(vendComponent.SoundVend, uid);
            return true;
        }

        // Frontier: custom vending check
        /// <summary>
        /// Checks whether the user is authorized to use the vending machine, then ejects the provided item if true
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="sender">Entity that is trying to use the vending machine</param>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        /// <param name="component"></param>
        public void AuthorizedVend(EntityUid uid, EntityUid sender, InventoryType type, string itemId, VendingMachineComponent component)
        {
            if (!_prototypeManager.TryIndex<EntityPrototype>(itemId, out var proto))
                return;

            var price = _pricing.GetEstimatedPrice(proto);
            // Somewhere deep in the code of pricing, a hardcoded 20 dollar value exists for anything without
            // a staticprice component for some god forsaken reason, and I cant find it or think of another way to
            // get an accurate price from a prototype with no staticprice comp.
            // this will undoubtably lead to vending machine exploits if I cant find wtf pricing system is doing.
            // also stacks, food, solutions, are handled poorly too f
            if (price == 0)
                price = 20;

            if (TryComp<MarketModifierComponent>(component.Owner, out var modifier))
                price *= modifier.Mod;

            var totalPrice = (int) price;

            // If any price has a vendor price, explicitly use its value - higher OR lower, over others.
            var priceVend = _pricing.GetEstimatedVendPrice(proto);
            if (priceVend > 0.0) // if vending price exists, overwrite it.
                totalPrice = (int) priceVend;

            if (IsAuthorized(uid, sender, component))
            {
                int bankBalance = 0;
                if (TryComp<BankAccountComponent>(sender, out var bank))
                    bankBalance = bank.Balance;

                int cashSlotBalance = 0;
                Entity<StackComponent>? cashEntity = null;
                if (component.CashSlotName != null
                    && component.CurrencyStackType != null
                    && ItemSlots.TryGetSlot(uid, component.CashSlotName, out var cashSlot)
                    && TryComp<StackComponent>(cashSlot?.ContainerSlot?.ContainedEntity, out var stackComp)
                    && stackComp!.StackTypeId == component.CurrencyStackType)
                {
                    cashSlotBalance = stackComp!.Count;
                    cashEntity = (cashSlot!.ContainerSlot!.ContainedEntity.Value, stackComp!);
                }

                if (totalPrice > bankBalance + cashSlotBalance)
                {
                    _popupSystem.PopupEntity(Loc.GetString("bank-insufficient-funds"), uid);
                    Deny(uid, component);
                    return;
                }

                bool paidFully = false;
                // Mono: Store the purchase price for tracking
                component.LastPurchasePrice = totalPrice;

                if (TryEjectVendorItem(uid, type, itemId, component.CanShoot, component))
                {
                    if (cashEntity != null)
                    {
                        var newCashSlotBalance = Math.Max(cashSlotBalance - totalPrice, 0);
                        _stack.SetCount(cashEntity.Value.Owner, newCashSlotBalance, cashEntity.Value.Comp);
                        component.CashSlotBalance = newCashSlotBalance;
                        paidFully = true; // Either we paid fully with cash, or we need to withdraw the remainder
                    }
                    if (totalPrice > cashSlotBalance)
                    {
                        paidFully = _bankSystem.TryBankWithdraw(sender, totalPrice - cashSlotBalance);
                    }

                    // If we paid completely, pay our station taxes
                    if (paidFully)
                    {
                        foreach (var (account, taxCoeff) in component.TaxAccounts)
                        {
                            if (!float.IsFinite(taxCoeff) || taxCoeff <= 0.0f)
                                continue;
                            var tax = (int)Math.Floor(totalPrice * taxCoeff);
                            _bankSystem.TrySectorDeposit(account, tax, LedgerEntryType.VendorTax);
                        }
                    }

                    // Something was ejected, update the vending component's state
                    Dirty(uid, component);

                    _adminLogger.Add(LogType.Action, LogImpact.Low,
                        $"{ToPrettyString(sender):user} bought from [vendingMachine:{ToPrettyString(uid!)}, product:{proto.Name}, cost:{totalPrice},  with ${cashSlotBalance} in the cash slot and ${bankBalance} in the bank.");
                }
            }
            // End Frontier
        }

        /// <summary>
        /// Tries to update the visuals of the component based on its current state.
        /// </summary>
        public void TryUpdateVisualState(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            var finalState = VendingMachineVisualState.Normal;
            if (vendComponent.Broken)
            {
                finalState = VendingMachineVisualState.Broken;
            }
            else if (vendComponent.Ejecting)
            {
                finalState = VendingMachineVisualState.Eject;
            }
            else if (vendComponent.Denying)
            {
                finalState = VendingMachineVisualState.Deny;
            }
            else if (!this.IsPowered(uid, EntityManager))
            {
                finalState = VendingMachineVisualState.Off;
            }

            if (_light.TryGetLight(uid, out var pointlight))
            {
                var lightState = finalState != VendingMachineVisualState.Broken && finalState != VendingMachineVisualState.Off;
                _light.SetEnabled(uid, lightState, pointlight);
            }

            _appearanceSystem.SetData(uid, VendingMachineVisuals.VisualState, finalState);
        }

        /// <summary>
        /// Ejects a random item from the available stock. Will do nothing if the vending machine is empty.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="throwItem">Whether to throw the item in a random direction after dispensing it.</param>
        /// <param name="forceEject">Whether to skip the regular ejection checks and immediately dispense the item without animation.</param>
        /// <param name="vendComponent"></param>
        public void EjectRandom(EntityUid uid, bool throwItem, bool forceEject = false, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            if (!this.IsPowered(uid, EntityManager))
                return;

            if (vendComponent.Ejecting)
                return;

            if (vendComponent.EjectRandomCounter <= 0)
            {
                _audioSystem.PlayPvs(_audioSystem.GetSound(vendComponent.SoundDeny), uid);
                _popupSystem.PopupEntity(Loc.GetString("vending-machine-component-try-eject-access-abused"), uid, PopupType.MediumCaution);
                return;
            }

            var availableItems = GetAvailableInventory(uid, vendComponent);
            if (availableItems.Count <= 0)
                return;
            var item = _random.Pick(availableItems);

            if (forceEject)
            {
                vendComponent.NextItemToEject = item.ID;
                vendComponent.ThrowNextItem = throwItem;
                var entry = GetEntry(uid, item.ID, item.Type, vendComponent);
                if (entry != null)
                    entry.Amount--;
                EjectItem(uid, vendComponent, forceEject);
            }
            else
                TryEjectVendorItem(uid, item.Type, item.ID, throwItem, vendComponent);
            vendComponent.EjectRandomCounter -= 1;
        }

        public void AddCharges(EntityUid uid, int change, VendingMachineComponent? comp = null)
        {
            if (!Resolve(uid, ref comp, false))
                return;

            var old = comp.EjectRandomCounter;
            comp.EjectRandomCounter = Math.Clamp(comp.EjectRandomCounter + change, 0, comp.EjectRandomMax);
            if (comp.EjectRandomCounter != old)
                Dirty(uid, comp);
        }

        private void EjectItem(EntityUid uid, VendingMachineComponent? vendComponent = null, bool forceEject = false)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            // No need to update the visual state because we never changed it during a forced eject
            if (!forceEject)
                TryUpdateVisualState(uid, vendComponent);

            if (string.IsNullOrEmpty(vendComponent.NextItemToEject))
            {
                vendComponent.ThrowNextItem = false;
                return;
            }

            // Default spawn coordinates
            var spawnCoordinates = Transform(uid).Coordinates;

            //Make sure the wallvends spawn outside of the wall.

            if (TryComp<WallMountComponent>(uid, out var wallMountComponent))
            {

                var offset = wallMountComponent.Direction.ToWorldVec() * WallVendEjectDistanceFromWall;
                spawnCoordinates = spawnCoordinates.Offset(offset);
            }

            var ent = Spawn(vendComponent.NextItemToEject, spawnCoordinates);

            _contraband.ClearContrabandValue(ent); // Frontier

            // Mono: Track vending machine purchases for pricing modifications
            // Only track if this was a paid purchase (not a random eject or force eject)
            if (!forceEject && vendComponent.LastPurchasePrice.HasValue)
            {
                _vendingPurchase.MarkAsPurchased(ent, uid, vendComponent.LastPurchasePrice.Value);
                vendComponent.LastPurchasePrice = null; // Clear after use
            }

            if (vendComponent.ThrowNextItem)
            {
                var range = vendComponent.NonLimitedEjectRange;
                var direction = new Vector2(_random.NextFloat(-range, range), _random.NextFloat(-range, range));
                _throwingSystem.TryThrow(ent, direction, vendComponent.NonLimitedEjectForce);
            }

            vendComponent.NextItemToEject = null;
            vendComponent.ThrowNextItem = false;
        }

        private VendingMachineInventoryEntry? GetEntry(EntityUid uid, string entryId, InventoryType type, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return null;

            if (type == InventoryType.Emagged && _emag.CheckFlag(uid, EmagType.Interaction))
                return component.EmaggedInventory.GetValueOrDefault(entryId);

            if (type == InventoryType.Contraband && component.Contraband)
                return component.ContrabandInventory.GetValueOrDefault(entryId);

            return component.Inventory.GetValueOrDefault(entryId);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<VendingMachineComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.Ejecting)
                {
                    comp.EjectAccumulator += frameTime;
                    if (comp.EjectAccumulator >= comp.EjectDelay)
                    {
                        comp.EjectAccumulator = 0f;
                        comp.Ejecting = false;

                        EjectItem(uid, comp);
                    }
                }

                if (comp.Denying)
                {
                    comp.DenyAccumulator += frameTime;
                    if (comp.DenyAccumulator >= comp.DenyDelay)
                    {
                        comp.DenyAccumulator = 0f;
                        comp.Denying = false;

                        TryUpdateVisualState(uid, comp);
                    }
                }

                if (comp.DispenseOnHitCoolingDown)
                {
                    comp.DispenseOnHitAccumulator += frameTime;
                    if (comp.DispenseOnHitAccumulator >= comp.DispenseOnHitCooldown)
                    {
                        comp.DispenseOnHitAccumulator = 0f;
                        comp.DispenseOnHitCoolingDown = false;
                    }
                }

                // Added block for charges
                if (comp.EjectRandomCounter == comp.EjectRandomMax || _timing.CurTime < comp.EjectNextChargeTime)
                    continue;

                AddCharges(uid, 1, comp);
                comp.EjectNextChargeTime = _timing.CurTime + comp.EjectRechargeDuration;
                // Added block for charges
            }
            var disabled = EntityQueryEnumerator<EmpDisabledComponent, VendingMachineComponent>();
            while (disabled.MoveNext(out var uid, out _, out var comp))
            {
                if (comp.NextEmpEject < _timing.CurTime)
                {
                    EjectRandom(uid, true, false, comp);
                    comp.NextEmpEject += TimeSpan.FromSeconds(5 * comp.EjectDelay);
                }
            }
        }

        public void TryRestockInventory(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            RestockInventoryFromPrototype(uid, vendComponent);

            Dirty(uid, vendComponent);
            TryUpdateVisualState(uid, vendComponent);
        }

        private void OnPriceCalculation(EntityUid uid, VendingMachineRestockComponent component, ref PriceCalculationEvent args)
        {
            args.Price = 0; // This area of the code make it so the cargoblacklist gets ignored, this change was to resolve it.
            return;

            List<double> priceSets = new();
            // Find the most expensive inventory and use that as the highest price.
            foreach (var vendingInventory in component.CanRestock)
            {
                double total = 0;
                if (PrototypeManager.TryIndex(vendingInventory, out VendingMachineInventoryPrototype? inventoryPrototype))
                {
                    foreach (var (item, amount) in inventoryPrototype.StartingInventory)
                    {
                        if (PrototypeManager.TryIndex(item, out EntityPrototype? entity))
                            total += _pricing.GetEstimatedPrice(entity) * amount;
                    }
                }
                priceSets.Add(total);
            }

            args.Price += priceSets.Max();
        }

        //private void OnEmpPulse(EntityUid uid, VendingMachineComponent component, ref EmpPulseEvent args) // Frontier: Upstream - #28984
        //{
        //    if (!component.Broken && this.IsPowered(uid, EntityManager))
        //    {
        //        args.Affected = true;
        //        args.Disabled = true;
        //        component.NextEmpEject = _timing.CurTime;
        //    }
        //}

        // Frontier: cash slot logic
        private void OnEntityInserted(Entity<VendingMachineComponent> ent, ref EntInsertedIntoContainerMessage args)
        {
            if (ent.Comp.CashSlotName != null
            && ent.Comp.CurrencyStackType != null
            && ItemSlots.TryGetSlot(ent, ent.Comp.CashSlotName, out var slot)
            && TryComp<StackComponent>(slot?.ContainerSlot?.ContainedEntity, out var stack)
            && stack.StackTypeId == ent.Comp.CurrencyStackType)
            {
                ent.Comp.CashSlotBalance = stack.Count;
            }
            else
            {
                ent.Comp.CashSlotBalance = 0;
            }
            Dirty(ent, ent.Comp);
        }

        private void OnEntityRemoved(Entity<VendingMachineComponent> ent, ref EntRemovedFromContainerMessage args)
        {
            ent.Comp.CashSlotBalance = 0;
            Dirty(ent, ent.Comp);
        }
        // End Frontier: cash slot logic
    }
}
