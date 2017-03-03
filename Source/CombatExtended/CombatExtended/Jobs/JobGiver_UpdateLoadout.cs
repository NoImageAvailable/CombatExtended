﻿using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CombatExtended
{
    public class JobGiver_UpdateLoadout : ThinkNode_JobGiver
    {
        private enum ItemPriority : byte
        {
            None,
            Low,
            LowStock,
            Proximity
        }

        private const int proximitySearchRadius = 20;
        private const int maximumSearchRadius = 80;
        private const int ticksBeforeDropRaw = 40000;

        public override float GetPriority(Pawn pawn)
        {
            if (CheckForExcessItems(pawn))
            {
                return 9.2f;
            }
            ItemPriority priority;
            Thing unused;
            int i;
			Pawn carriedBy;
            LoadoutSlot slot = GetPrioritySlot(pawn, out priority, out unused, out i, out carriedBy);
            if (slot == null)
            {
                return 0f;
            }
            if (priority == ItemPriority.Low) return 3f;

            TimeAssignmentDef assignment = (pawn.timetable != null) ? pawn.timetable.CurrentAssignment : TimeAssignmentDefOf.Anything;
            if (assignment == TimeAssignmentDefOf.Sleep) return 3f;

            return 9.2f;
        }

        private LoadoutSlot GetPrioritySlot(Pawn pawn, out ItemPriority priority, out Thing closestThing, out int count, out Pawn carriedBy)
        {
            priority = ItemPriority.None;
            LoadoutSlot slot = null;
            closestThing = null;
            count = 0;
			carriedBy = null;

            CompInventory inventory = pawn.TryGetComp<CompInventory>();
            if (inventory != null && inventory.container != null)
            {
                Loadout loadout = pawn.GetLoadout();
                if (loadout != null && !loadout.Slots.NullOrEmpty())
                {
                    foreach(LoadoutSlot curSlot in loadout.Slots)
                    {
                        ItemPriority curPriority = ItemPriority.None;
                        Thing curThing = null;
                        int numCarried = inventory.container.TotalStackCountOfDef(curSlot.Def);

                        // Add currently equipped gun
                        if (pawn.equipment != null && pawn.equipment.Primary != null)
                        {
                            if (pawn.equipment.Primary.def == curSlot.Def) numCarried++;
                        }
                        System.Predicate<Thing> isFoodInPrison = (Thing t) => t.GetRoom().isPrisonCell && t.def.IsNutritionGivingIngestible && pawn.Faction.IsPlayer;
                        if (numCarried < curSlot.Count)
                        {
							// look for a thing near the pawn.
                            curThing = GenClosest.ClosestThingReachable(
                                pawn.Position,
                                pawn.Map,
                                curSlot.Def.Minifiable ? ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing) : ThingRequest.ForDef(curSlot.Def),
                                PathEndMode.ClosestTouch,
                                TraverseParms.For(pawn, Danger.None, TraverseMode.ByPawn),
                                proximitySearchRadius,
                                x => x.GetInnerIfMinified().def == curSlot.Def && !x.IsForbidden(pawn) && pawn.CanReserve(x) && !isFoodInPrison(x));
                            if (curThing != null) curPriority = ItemPriority.Proximity;
                            else
                            {
								// look for a thing basically anywhere on the map.
                                curThing = GenClosest.ClosestThingReachable(
                                    pawn.Position, 
                                    pawn.Map,
                                    curSlot.Def.Minifiable ? ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing) : ThingRequest.ForDef(curSlot.Def),
                                    PathEndMode.ClosestTouch,
                                    TraverseParms.For(pawn, Danger.None, TraverseMode.ByPawn),
                                    maximumSearchRadius,
                                    x => x.GetInnerIfMinified().def == curSlot.Def && !x.IsForbidden(pawn) && pawn.CanReserve(x) && !isFoodInPrison(x));
								if (curThing == null && pawn.Map != null)
								{
									// look for a thing inside caravan pack animals and prisoners.  EXCLUDE other colonists to avoid looping state.
									List<Pawn> carriers = pawn.Map.mapPawns.AllPawns.Where(
										p => (p.RaceProps.packAnimal && p.Faction == pawn.Faction) || (p.IsPrisoner && p.HostFaction == pawn.Faction)).ToList();
									foreach (Pawn carrier in carriers)
									{
										Thing thing = carrier.inventory.GetInnerContainer().FirstOrDefault(t => t.GetInnerIfMinified().def == curSlot.Def);
										if (thing != null)
										{
											curThing = thing;
											carriedBy = carrier;
											break;
										}
									}
									Log.Message(string.Concat("Carrier ", carriedBy, " has thing desired thing ", curThing.Label));
								}
                                if (curThing != null)
                                {
                                    if (!curSlot.Def.IsNutritionGivingIngestible && numCarried / curSlot.Count <= 0.5f) curPriority = ItemPriority.LowStock;
                                    else curPriority = ItemPriority.Low;
                                }
                            }
                        }
                        
                        if (curPriority > priority && curThing != null && inventory.CanFitInInventory(curThing, out count))
                        {
                            priority = curPriority;
                            slot = curSlot;
                            closestThing = curThing;
                        }
                        if (priority >= ItemPriority.LowStock)
                        {
                            break;
                        }
                    }
                }
            }

            return slot;
        }

        private bool CheckForExcessItems(Pawn pawn)
        {
            //if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.Tame) return false;
            CompInventory inventory = pawn.TryGetComp<CompInventory>();
            Loadout loadout = pawn.GetLoadout();
            if (inventory == null || inventory.container == null || loadout == null || loadout.Slots.NullOrEmpty())
            {
                return false;
            }
            if (inventory.container.Count > loadout.SlotCount + 1)
            {
                return true;
            }
            // Check to see if there is at least one loadout slot specifying currently equipped weapon
            ThingWithComps equipment = ((pawn.equipment == null) ? null : pawn.equipment.Primary) ?? null;
            if (equipment != null && !loadout.Slots.Any(slot => slot.Def == equipment.GetInnerIfMinified().def && slot.Count >= 1))
            {
                return true;
            }

            // Go through each item in the inventory and see if its part of our loadout
            bool allowDropRaw = Find.TickManager.TicksGame > pawn.mindState?.lastInventoryRawFoodUseTick + ticksBeforeDropRaw;
            foreach (Thing thing in inventory.container)
            {
                if(allowDropRaw || !thing.def.IsNutritionGivingIngestible || thing.def.ingestible.preferability > FoodPreferability.RawTasty)
                {
                    LoadoutSlot slot = loadout.Slots.FirstOrDefault(x => x.Def == thing.def);
                    if (slot == null)
                    {
                        return true;
                    }
                    int numContained = inventory.container.TotalStackCountOfDef(thing.def);

                    // Add currently equipped gun
                    if (pawn.equipment != null && pawn.equipment.Primary != null)
                    {
                        if (pawn.equipment.Primary.def == slot.Def)
                        {
                            numContained++;
                        }
                    }
                    if (slot.Count < numContained)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Get inventory
            CompInventory inventory = pawn.TryGetComp<CompInventory>();
            if (inventory == null) return null;

            Loadout loadout = pawn.GetLoadout();
            if (loadout != null)
            {
                // Find and drop excess items
                foreach (LoadoutSlot slot in loadout.Slots)
                {
                    int numContained = inventory.container.TotalStackCountOfDef(slot.Def);

                    // Add currently equipped gun
                    if (pawn.equipment != null && pawn.equipment.Primary != null)
                    {
                        if (pawn.equipment.Primary.def == slot.Def)
                        {
                            numContained++;
                        }
                    }
                    // Drop excess items
                    if(numContained > slot.Count)
                    {
                    	Thing thing = inventory.container.FirstOrDefault(x => x.GetInnerIfMinified().def == slot.Def);
                        if (thing != null)
                        {
                            Thing droppedThing;
                            if (inventory.container.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, numContained - slot.Count, out droppedThing))
                            {
                                if (droppedThing != null)
                                {
                                    return HaulAIUtility.HaulToStorageJob(pawn, droppedThing);
                                }
                                Log.Error(pawn + " tried dropping " + thing + " from loadout but resulting thing is null");
                            }
                        }
                    }
                }

                // Try drop currently equipped weapon
                if (pawn.equipment != null && pawn.equipment.Primary != null && !loadout.Slots.Any(slot => slot.Def == pawn.equipment.Primary.def && slot.Count >= 1))
                {
                    ThingWithComps droppedEq;
                    if (pawn.equipment.TryDropEquipment(pawn.equipment.Primary, out droppedEq, pawn.Position, false))
                    {
                        return HaulAIUtility.HaulToStorageJob(pawn, droppedEq);
                    }
                }

                // Find excess items in inventory that are not part of our loadout
                bool allowDropRaw = Find.TickManager.TicksGame > pawn.mindState?.lastInventoryRawFoodUseTick + ticksBeforeDropRaw;
                Thing thingToRemove = inventory.container.FirstOrDefault(t => 
                    (allowDropRaw || !t.def.IsNutritionGivingIngestible || t.def.ingestible.preferability > FoodPreferability.RawTasty)
                    && !loadout.Slots.Any(s => s.Def == t.GetInnerIfMinified().def));
                if (thingToRemove != null)
                {
                    Thing droppedThing;
                    if (inventory.container.TryDrop(thingToRemove, pawn.Position, pawn.Map, ThingPlaceMode.Near, thingToRemove.stackCount, out droppedThing))
                    {
                        return HaulAIUtility.HaulToStorageJob(pawn, droppedThing);
                    }
                    Log.Error(pawn + " tried dropping " + thingToRemove + " from inventory but resulting thing is null");
                }

                // Find missing items
                ItemPriority priority;
                Thing closestThing;
                int count;
				Pawn carriedBy;
				bool doEquip = false;
                LoadoutSlot prioritySlot = GetPrioritySlot(pawn, out priority, out closestThing, out count, out carriedBy);
                // moved logic to detect if should equip vs put in inventory here...
                if (closestThing != null)
                {
                    if (closestThing.TryGetComp<CompEquippable>() != null
                        && (pawn.health != null && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                        && (pawn.equipment == null || pawn.equipment.Primary == null || !loadout.Slots.Any(s => s.Def == pawn.equipment.Primary.def)))
                		doEquip = true;
	                if (carriedBy == null)
	                {
	                    // Equip gun if unarmed or current gun is not in loadout
	                    if (doEquip)
	                    {
	                        return new Job(JobDefOf.Equip, closestThing);
	                    }
	                    // Take items into inventory if needed
	                    int numContained = inventory.container.TotalStackCountOfDef(prioritySlot.Def);
	                    return new Job(JobDefOf.TakeInventory, closestThing) { count = Mathf.Min(closestThing.stackCount, prioritySlot.Count - numContained, count) };
	                } else
	                {
	                	return new Job(CE_JobDefOf.TakeFromOther, closestThing, carriedBy, doEquip ? pawn : null) {
	                		count = doEquip ? 1 : Mathf.Min(closestThing.stackCount, prioritySlot.Count - inventory.container.TotalStackCountOfDef(prioritySlot.Def), count)
	                	};
	                }
                }
            }
            return null;
        }
    }
}
