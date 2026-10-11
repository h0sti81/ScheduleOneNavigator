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
        // Normalized product id -> quantity this contract needs; used to plan
        // several deals against one shared inventory (see SelectDealsForInventory).
        public Dictionary<string, int> Requirements;
        // Game minutes until the contract expires, int.MaxValue if it doesn't.
        public int MinsUntilExpiry = int.MaxValue;

        // Urgency group in whole game hours (0 = expires within the hour);
        // planning serves lower groups first (user request: "prioritise the
        // deals that expire next").
        public int UrgencyHours => MinsUntilExpiry == int.MaxValue ? int.MaxValue : Mathf.Max(0, MinsUntilExpiry) / 60;
    }

    public static class DealPlanner
    {
        static int MinsUntilExpiry(Contract contract)
        {
            try { return contract.Expires ? contract.GetMinsUntilExpiry() : int.MaxValue; }
            catch { return int.MaxValue; }
        }

        // Inventory snapshot from the latest GatherActiveDeals (normalized
        // product id -> units held), for planning deals against it together.
        public static Dictionary<string, int> LastInventory = new Dictionary<string, int>();

        // "Feasible" is checked per deal against the whole inventory, so six
        // deals each needing <=5 units all looked fulfillable with 5 units
        // in the bag. This picks what the inventory covers together, short
        // walks over profit (user request): most urgent expiry group first,
        // and within it always the nearest deal - from the player, then from
        // the previous pick - that the remaining stock still covers.
        public static List<DealInfo> SelectDealsForInventory(List<DealInfo> candidates, Dictionary<string, int> inventory, Vector3 from)
        {
            var stock = new Dictionary<string, int>(inventory);
            var left = new List<DealInfo>(candidates);
            var picked = new List<DealInfo>();

            bool Fits(DealInfo d)
            {
                if (d.Requirements == null)
                    return false;
                foreach (var kv in d.Requirements)
                {
                    stock.TryGetValue(kv.Key, out int have);
                    if (kv.Value > have)
                        return false;
                }
                return true;
            }

            var groups = new List<int>();
            foreach (DealInfo d in candidates)
                if (!groups.Contains(d.UrgencyHours))
                    groups.Add(d.UrgencyHours);
            groups.Sort();

            Vector3 current = from;
            foreach (int group in groups)
            {
                while (true)
                {
                    DealInfo nearest = null;
                    float nearestDist = float.MaxValue;
                    foreach (DealInfo d in left)
                    {
                        if (d.UrgencyHours != group || !Fits(d))
                            continue;
                        float dist = Vector3.Distance(current, d.DeliveryPosition);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearest = d;
                        }
                    }
                    if (nearest == null)
                        break;

                    foreach (var kv in nearest.Requirements)
                        stock[kv.Key] -= kv.Value;
                    picked.Add(nearest);
                    left.Remove(nearest);
                    current = nearest.DeliveryPosition;
                }
            }
            return picked;
        }

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
                LastInventory = new Dictionary<string, int>(inventoryByProductId);
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
                    var requirements = new Dictionary<string, int>();
                    if (contract.ProductList != null && contract.ProductList.entries != null)
                        foreach (var entry in contract.ProductList.entries)
                        {
                            if (entry == null)
                                continue;
                            string id = NormalizeProductId(entry.ProductID);
                            requirements.TryGetValue(id, out int already);
                            requirements[id] = already + entry.Quantity;
                        }

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
                        Requirements = requirements,
                        MinsUntilExpiry = MinsUntilExpiry(contract),
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
                Log.Debug($"[Minimap] DealPlanner: no deals gathered - unlockedCustomers={total}, " +
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
            Log.Debug($"[Minimap] DealPlanner: recognized product items in inventory: {total} ({string.Join(", ", parts)}).");

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
                        Log.Debug($"[Minimap] DealPlanner: shortfall - deal wants {entry.Quantity}x " +
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
            if (!Log.Verbose)
                return;
            if (slots == null)
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    Log.Debug($"[Minimap] DealPlanner: {label}[{i}] = null slot");
                    continue;
                }
                if (slot.ItemInstance == null)
                {
                    Log.Debug($"[Minimap] DealPlanner: {label}[{i}] empty");
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

                Log.Debug($"[Minimap] DealPlanner: {label}[{i}] type={typeName}, id={id}, quantity={qty}, " +
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

        // Everything the "optimal route" needs, from scratch: current deals
        // and inventory, the subset the inventory covers together, and the
        // shortest order through it from `from`. Called on the button click
        // and again on every re-plan while a deal route is active, so
        // restocking, new or expired deals are picked up on their own.
        //
        // stickyContract: the deal currently being walked to. Selection is
        // nearest-first, so without this the target could flip to another
        // customer just because the walk passed closer to them; it stays
        // first as long as it's still ready and nothing more urgent showed up.
        public static List<DealInfo> PlanDealRoute(Vector3 from, out int readyCount, IntPtr stickyContract = default)
        {
            var ready = new List<DealInfo>();
            foreach (DealInfo d in GatherActiveDeals())
                if (d.Feasible && d.AwaitingDelivery)
                    ready.Add(d);
            readyCount = ready.Count;

            DealInfo sticky = stickyContract == IntPtr.Zero ? null
                : ready.Find(d => d.Contract != null && d.Contract.Pointer == stickyContract);
            if (sticky != null && ready.TrueForAll(d => d.UrgencyHours >= sticky.UrgencyHours))
            {
                var stock = new Dictionary<string, int>(LastInventory);
                foreach (var kv in sticky.Requirements)
                {
                    stock.TryGetValue(kv.Key, out int have);
                    stock[kv.Key] = have - kv.Value;
                }
                ready.Remove(sticky);
                var rest = ComputeUrgencyOrder(sticky.DeliveryPosition, SelectDealsForInventory(ready, stock, sticky.DeliveryPosition));
                rest.Insert(0, sticky);
                return rest;
            }
            return ComputeUrgencyOrder(from, SelectDealsForInventory(ready, LastInventory, from));
        }

        // Most urgent group first, shortest tour within each group, each
        // group starting where the previous one ended.
        public static List<DealInfo> ComputeUrgencyOrder(Vector3 start, List<DealInfo> deals)
        {
            var byGroup = new SortedDictionary<int, List<DealInfo>>();
            foreach (DealInfo d in deals)
            {
                if (!byGroup.TryGetValue(d.UrgencyHours, out var list))
                    byGroup[d.UrgencyHours] = list = new List<DealInfo>();
                list.Add(d);
            }
            var ordered = new List<DealInfo>(deals.Count);
            Vector3 current = start;
            foreach (var group in byGroup.Values)
            {
                var part = ComputeOptimalOrder(current, group);
                ordered.AddRange(part);
                current = part[part.Count - 1].DeliveryPosition;
            }
            return ordered;
        }

        // Shortest open tour (straight-line distance) starting at the
        // player. Exact for up to ExactOrderMaxStops deals (8! = 40320
        // orders, still instant); beyond that greedy nearest-neighbour
        // improved by 2-opt. The old pure nearest-neighbour could start
        // with the closest customer and then zig-zag across town.
        const int ExactOrderMaxStops = 8;

        public static List<DealInfo> ComputeOptimalOrder(Vector3 start, List<DealInfo> feasibleDeals)
        {
            int n = feasibleDeals.Count;
            if (n <= 1)
                return new List<DealInfo>(feasibleDeals);

            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
                pts[i] = feasibleDeals[i].DeliveryPosition;

            float TourLength(int[] order)
            {
                float len = Vector3.Distance(start, pts[order[0]]);
                for (int i = 1; i < order.Length; i++)
                    len += Vector3.Distance(pts[order[i - 1]], pts[order[i]]);
                return len;
            }

            int[] best;
            if (n <= ExactOrderMaxStops)
            {
                var perm = new int[n];
                for (int i = 0; i < n; i++)
                    perm[i] = i;
                best = (int[])perm.Clone();
                float bestLen = TourLength(best);
                // Heap's algorithm over all permutations.
                var c = new int[n];
                int k = 0;
                while (k < n)
                {
                    if (c[k] < k)
                    {
                        int a = k % 2 == 0 ? 0 : c[k];
                        (perm[a], perm[k]) = (perm[k], perm[a]);
                        float len = TourLength(perm);
                        if (len < bestLen)
                        {
                            bestLen = len;
                            best = (int[])perm.Clone();
                        }
                        c[k]++;
                        k = 0;
                    }
                    else
                    {
                        c[k] = 0;
                        k++;
                    }
                }
            }
            else
            {
                best = new int[n];
                var used = new bool[n];
                Vector3 current = start;
                for (int step = 0; step < n; step++)
                {
                    int bestIndex = -1;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (used[i])
                            continue;
                        float d = Vector3.Distance(current, pts[i]);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestIndex = i;
                        }
                    }
                    used[bestIndex] = true;
                    best[step] = bestIndex;
                    current = pts[bestIndex];
                }

                bool improved = true;
                while (improved)
                {
                    improved = false;
                    float bestLen = TourLength(best);
                    for (int i = 0; i < n - 1; i++)
                    {
                        for (int j = i + 1; j < n; j++)
                        {
                            System.Array.Reverse(best, i, j - i + 1);
                            float len = TourLength(best);
                            if (len < bestLen - 0.01f)
                            {
                                bestLen = len;
                                improved = true;
                            }
                            else
                            {
                                System.Array.Reverse(best, i, j - i + 1);
                            }
                        }
                    }
                }
            }

            var ordered = new List<DealInfo>(n);
            foreach (int i in best)
                ordered.Add(feasibleDeals[i]);
            return ordered;
        }
    }
}
