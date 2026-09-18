using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;

namespace ScheduleOneNavigator
{
    // Gathers the player's currently active delivery deals (Contracts) and
    // figures out which ones can actually be fulfilled with what's in the
    // player's inventory right now, plus a simple route ordering through the
    // feasible ones.
    public class DealInfo
    {
        public Customer Customer;
        public Contract Contract;
        public Vector3 DeliveryPosition;
        public string CustomerName;
        public string ProductSummary;
        public float Payment;
        public bool Feasible;
        // True = customer is actively standing by, ready for handover right
        // now. False = the contract is accepted/claimed and has a delivery
        // location, but the customer isn't there yet - e.g. a deal the
        // Dealcraft mod (github.com/Milofax/Dealcraft) auto-claimed and
        // scheduled into a future time window. Added 2026-09-19 because the
        // tablet previously only showed AwaitingDelivery==true deals, so
        // anything scheduled-but-not-yet-due was invisible even though it's
        // a real, active contract.
        public bool AwaitingDelivery;
    }

    public static class DealPlanner
    {
        static bool loggedEmptyDiag;

        // Mirrors how nugzzMinimap's RadarController finds pending-delivery
        // contracts: iterate the player's own unlocked customers rather than
        // the global Contract.Contracts list, since a Contract's own
        // "Customer" field is a NetworkObject (not a Customer component) and
        // Customer.CurrentContract already gives us the link directly, plus a
        // convenient IsAwaitingDelivery flag and (via .NPC.FullName) a
        // display name.
        public static List<DealInfo> GatherActiveDeals()
        {
            var result = new List<DealInfo>();
            var unlocked = Customer.UnlockedCustomers;
            if (unlocked == null)
                return result;

            Dictionary<string, int> inventoryByProductId;
            try
            {
                inventoryByProductId = GetInventoryByProductId();
            }
            catch (Exception ex)
            {
                LogOnce("GetInventoryByProductId", ex);
                return result;
            }

            int total = 0, awaiting = 0, hasContract = 0, noDealer = 0, hasStandPoint = 0;
            foreach (Customer customer in unlocked)
            {
                total++;
                // Every property/field read below crosses into Il2Cpp native
                // code - wrapped per-customer so one bad entry (or a property
                // that throws instead of returning null/false in some game
                // state we didn't anticipate) logs exactly where and skips
                // just that customer, instead of aborting the whole refresh
                // silently every single cycle (which is what happened before
                // this was added - the list looked merely "empty" but was
                // actually crashing before it could report anything).
                try
                {
                    if (customer == null)
                        continue;
                    // Both awaiting-delivery (customer standing by right now)
                    // and merely-accepted-and-scheduled contracts are
                    // gathered - see the AwaitingDelivery field comment on
                    // DealInfo.
                    bool awaitingDelivery = customer.IsAwaitingDelivery;
                    if (awaitingDelivery)
                        awaiting++;

                    Contract contract = customer.CurrentContract;
                    if (contract == null)
                        continue;
                    hasContract++;

                    // Mirrors nugzzMinimap's RadarController filter exactly -
                    // Dealer != null means an NPC/hired dealer already has this
                    // contract assigned to handle automatically, so it's not
                    // something the player themselves needs to walk over and
                    // deliver. An earlier version of this also required
                    // contract.State == EQuestState.Active, which nugzz's
                    // (working) filter does not check and which turned out to
                    // exclude every real deal in testing - removed.
                    if (contract.Dealer != null)
                        continue;
                    noDealer++;

                    Transform standPoint = contract.DeliveryLocation != null ? contract.DeliveryLocation.CustomerStandPoint : null;
                    if (standPoint == null)
                        continue;
                    hasStandPoint++;

                    // Split out of one object-initializer (which evaluates
                    // every value before any exception can be attributed to a
                    // specific one) into separate steps with their own stage
                    // tag, so a crash here points at exactly which piece is
                    // the problem instead of "somewhere in DealInfo".
                    Vector3 deliveryPos = standPoint.position;
                    string customerName = customer.NPC != null ? customer.NPC.FullName : "?";
                    string productSummary = SummarizeProductList(contract);
                    float payment = contract.Payment;
                    bool feasible = ComputeFeasible(contract, inventoryByProductId);

                    result.Add(new DealInfo
                    {
                        Customer = customer,
                        Contract = contract,
                        DeliveryPosition = deliveryPos,
                        CustomerName = customerName,
                        ProductSummary = productSummary,
                        Payment = payment,
                        Feasible = feasible,
                        AwaitingDelivery = awaitingDelivery,
                    });
                }
                catch (Exception ex)
                {
                    string name = "?";
                    try { name = customer?.NPC?.FullName ?? customer?.name ?? "?"; } catch { }
                    LogOnce($"customer loop (customer={name})", ex);
                }
            }

            if (result.Count == 0 && total > 0 && !loggedEmptyDiag)
            {
                loggedEmptyDiag = true;
                MelonLogger.Msg($"[Minimap] DealPlanner: no deals gathered - unlockedCustomers={total}, " +
                    $"isAwaitingDelivery={awaiting}, hasCurrentContract={hasContract}, dealerNull={noDealer}, hasStandPoint={hasStandPoint}.");
            }
            else if (result.Count > 0)
            {
                loggedEmptyDiag = false; // allow the diagnostic again if it later goes back to empty
            }

            return result;
        }

        static readonly HashSet<string> loggedExceptionStages = new HashSet<string>();

        static void LogOnce(string stage, Exception ex)
        {
            if (!loggedExceptionStages.Add(stage))
                return;
            MelonLogger.Error($"[Minimap] DealPlanner: exception at [{stage}]: {ex}");
        }

        static int lastLoggedInventoryCount = -1;

        // Cheap sanity check logged whenever the recognized-product count
        // changes, so "why isn't X feasible" can be answered from the log
        // (does the mod see the product at all?) instead of guessing.
        //
        // The full per-slot breakdown (raw type/id/quantity) used to only
        // log once, ever, right at the first GatherActiveDeals() call of the
        // session (see loggedFullBreakdown history below) - a single
        // snapshot that can miss the exact moment a suspect item (e.g. a
        // freshly packaged jar) actually enters the inventory, which is
        // precisely the moment needed to settle a "not recognized" report.
        // Now re-run alongside every count change instead, so whatever
        // changed is visible in the same log capture as the new total.
        static void LogInventoryCountOnce(Dictionary<string, int> byId, PlayerInventory inv)
        {
            int total = 0;
            foreach (var kv in byId)
                total += kv.Value;
            if (total == lastLoggedInventoryCount)
                return;
            lastLoggedInventoryCount = total;
            var parts = new List<string>();
            foreach (var kv in byId)
                parts.Add($"{kv.Value}x {kv.Key}");
            MelonLogger.Msg($"[Minimap] DealPlanner: recognized product items in inventory: {total} ({string.Join(", ", parts)}).");

            LogFullBreakdown("backpack", inv.GetAllInventorySlots());
            LogFullBreakdown("hotbar", inv.hotbarSlots);
            LogFullBreakdown("equippable", inv.equippableSlots);
        }

        // Contract.DoesProductListMatchSpecified (the game's own method for
        // this) reliably threw from mod code in testing - first a
        // NullReferenceException when handed non-product items, then (after
        // filtering to products only) an ArgumentOutOfRangeException instead,
        // suggesting whatever internal algorithm it uses doesn't tolerate
        // being called this way at all. Rather than keep guessing at its
        // internals, this does the actual (simple) comparison ourselves:
        // sum held quantity per product ID, check it covers what each
        // ProductList entry asks for.
        static bool ComputeFeasible(Contract contract, Dictionary<string, int> inventoryByProductId)
        {
            var list = contract.ProductList;
            if (list == null || list.entries == null)
                return false;

            bool feasible = true;
            foreach (var entry in list.entries)
            {
                if (entry == null)
                    continue;
                string normalized = NormalizeProductId(entry.ProductID);
                inventoryByProductId.TryGetValue(normalized, out int have);
                if (have < entry.Quantity)
                {
                    feasible = false;
                    // Test 16 showed a freshly-packaged 5x Sour Diesel jar
                    // WAS correctly added to inventoryByProductId (log jumped
                    // +5 on pickup) - yet the user still perceived "not
                    // recognized" afterwards. That contradiction was never
                    // pinned down. Rather than adjust the matching logic
                    // again on a guess, log the exact raw requested ID next
                    // to every currently-held normalized ID the first time
                    // this specific shortfall is seen, so the next test
                    // shows directly whether it's an ID-string mismatch
                    // (e.g. a custom Mixing-Mania name), a genuine quantity
                    // shortfall, or something else entirely.
                    string mismatchKey = $"{entry.ProductID}|need{entry.Quantity}|have{have}";
                    if (loggedMismatches.Add(mismatchKey))
                    {
                        MelonLogger.Msg($"[Minimap] DealPlanner: shortfall - deal wants {entry.Quantity}x " +
                            $"'{entry.ProductID}' (normalized '{normalized}'), inventory has {have}. " +
                            $"Currently held product IDs: [{string.Join(", ", inventoryByProductId.Keys)}].");
                    }
                }
            }
            return feasible;
        }

        static readonly HashSet<string> loggedMismatches = new HashSet<string>();

        // Confirmed in testing: a held product's BaseItemInstance.ID can come
        // back in a different form than ProductList.Entry.ProductID for the
        // same product ("Sour Diesel" vs "sourdiesel") - looks like a display
        // name vs. a slug/internal-ID convention. Normalizing both sides
        // (lowercase, no spaces/punctuation) before comparing sidesteps that
        // without needing to know which of the two forms is "the" canonical
        // one.
        static string NormalizeProductId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            var sb = new System.Text.StringBuilder(id.Length);
            foreach (char c in id)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static string SummarizeProductList(Contract contract)
        {
            var list = contract.ProductList;
            if (list == null || list.entries == null)
                return "?";

            var parts = new List<string>();
            foreach (var entry in list.entries)
            {
                if (entry == null)
                    continue;
                parts.Add($"{entry.Quantity}x {entry.ProductID}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "?";
        }

        // Sums held quantity per product ID (BaseItemInstance.ID) across every
        // place products can sit - GetAllInventorySlots() turned out to only
        // cover backpack/storage slots (confirmed in testing: a product
        // visibly sitting in a hotbar slot, "14x" in a screenshot, was not
        // picked up), so hotbar and equippable slots are scanned separately
        // too. HotbarSlot : ItemSlot (confirmed via decompile), so the same
        // helper works for both.
        static Dictionary<string, int> GetInventoryByProductId()
        {
            var byId = new Dictionary<string, int>();
            if (!PlayerInventory.InstanceExists)
                return byId;

            var inv = PlayerInventory.Instance;

            // Test on 2026-09-17 showed backpack/hotbar/equippable holding the
            // exact same items at the exact same slot indices (e.g. 14x ogkush
            // in all three), inflating every count by 3x - GetAllInventorySlots()
            // apparently already includes hotbar/equippable slots, contradicting
            // the assumption behind adding them separately (see comment above).
            // Dedupe by native slot pointer instead of trusting either theory,
            // so this can't silently regress back to under- or over-counting.
            var seenSlots = new HashSet<IntPtr>();
            AddProductItems(inv.GetAllInventorySlots(), byId, seenSlots);
            AddProductItems(inv.hotbarSlots, byId, seenSlots);
            AddProductItems(inv.equippableSlots, byId, seenSlots);

            LogInventoryCountOnce(byId, inv);
            return byId;
        }

        // ROOT CAUSE (found 2026-09-17 via decompile, not guessing): a
        // packaged product (e.g. a jar) is ONE stack entry with
        // BaseItemInstance.Quantity = 1 ("1 jar"), not 5 - the actual
        // contained amount lives in ProductItemInstance's own
        // GetTotalAmount() override (there's also an `Amount` property;
        // GetTotalAmount() is the one BaseItemInstance itself exposes as
        // its general "how much of this do I have" API, so it's the safer
        // one to rely on generically). PackagingDefinition (the jar/baggie
        // type) has its own separate Quantity field for "units per
        // package", multiplied in somewhere inside GetTotalAmount()'s
        // native body. Explains exactly why a *loose* 5x Sour Diesel worked
        // (Quantity==GetTotalAmount() when there's no packaging) while a
        // *jarred* one silently under-counted as 1 - not a detection or
        // ID-normalization bug at all, a wrong-field bug.
        static void LogFullBreakdown<TSlot>(string label, Il2CppSystem.Collections.Generic.List<TSlot> slots)
            where TSlot : ItemSlot
        {
            if (slots == null)
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    MelonLogger.Msg($"[Minimap] DealPlanner: {label}[{i}] = null slot");
                    continue;
                }
                if (slot.ItemInstance == null)
                {
                    MelonLogger.Msg($"[Minimap] DealPlanner: {label}[{i}] empty");
                    continue;
                }

                var item = slot.ItemInstance;
                string typeName;
                try { typeName = item.GetIl2CppType().Name; } catch { typeName = "?"; }
                string id;
                try { id = item.ID; } catch { id = "<threw>"; }
                int qty;
                try { qty = item.Quantity; } catch { qty = -1; }
                var product = item.TryCast<ProductItemInstance>();
                int totalAmount = -1;
                if (product != null)
                {
                    try { totalAmount = product.GetTotalAmount(); } catch { totalAmount = -1; }
                }

                MelonLogger.Msg($"[Minimap] DealPlanner: {label}[{i}] type={typeName}, id={id}, quantity={qty}, " +
                    $"totalAmount={totalAmount}, isProductItemInstance={product != null}");
            }
        }

        static void AddProductItems<TSlot>(Il2CppSystem.Collections.Generic.List<TSlot> slots, Dictionary<string, int> byId, HashSet<IntPtr> seenSlots)
            where TSlot : ItemSlot
        {
            if (slots == null)
                return;

            foreach (var slot in slots)
            {
                if (slot == null || slot.ItemInstance == null)
                    continue;

                if (!seenSlots.Add(slot.Pointer))
                    continue;

                // Only count actual drug products - the deal system only
                // cares about those, and calling the game's own product-match
                // method with non-product items (cash, tools, packaging) is
                // what crashed in earlier testing.
                var product = slot.ItemInstance.TryCast<ProductItemInstance>();
                if (product == null)
                    continue;

                string id = NormalizeProductId(product.ID);
                if (id.Length == 0)
                    continue;

                // Quantity is the stack count (how many packages), not the
                // held amount - a single jar of 5 is Quantity=1. Use
                // GetTotalAmount() (accounts for the packaging's own
                // per-unit multiplier) so packaged and loose product count
                // the same way. See the LogFullBreakdown comment above for
                // how this was found.
                int amount;
                try { amount = product.GetTotalAmount(); }
                catch { amount = product.Quantity; }

                byId.TryGetValue(id, out int existing);
                byId[id] = existing + amount;
            }
        }

        // Simple nearest-neighbor ordering starting from the player's current
        // position - not a full TSP solve, but for the handful of
        // simultaneous deals this game realistically has at once (typically
        // 2-5), greedy nearest-neighbor gets within a few percent of optimal
        // and is instant, no need for anything heavier.
        public static List<DealInfo> ComputeOptimalOrder(Vector3 start, List<DealInfo> feasibleDeals)
        {
            var remaining = new List<DealInfo>(feasibleDeals);
            var ordered = new List<DealInfo>(remaining.Count);
            Vector3 current = start;

            while (remaining.Count > 0)
            {
                int bestIndex = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float d = Vector3.Distance(current, remaining[i].DeliveryPosition);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIndex = i;
                    }
                }
                ordered.Add(remaining[bestIndex]);
                current = remaining[bestIndex].DeliveryPosition;
                remaining.RemoveAt(bestIndex);
            }

            return ordered;
        }
    }
}
