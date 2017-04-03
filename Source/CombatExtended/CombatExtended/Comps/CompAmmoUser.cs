﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace CombatExtended
{
    public class CompAmmoUser : CompRangedGizmoGiver
    {
        #region Fields

        private int curMagCountInt;
        private LocalTargetInfo storedTarget = null;
        private JobDef storedJobDef = null;
        private AmmoDef currentAmmoInt = null;
        public AmmoDef selectedAmmo;
        
        private Thing ammoToBeDeleted;

        public Building_TurretGunCE turret;         // Cross-linked from CE turret

        #endregion

        #region Properties

        public CompProperties_AmmoUser Props
        {
            get
            {
                return (CompProperties_AmmoUser)props;
            }
        }

        public int curMagCount
        {
            get
            {
                return curMagCountInt;
            }
        }
        public CompEquippable compEquippable
        {
            get { return parent.GetComp<CompEquippable>(); }
        }
        public Pawn wielder
        {
            get
            {
                if (compEquippable == null || compEquippable.PrimaryVerb == null)
                {
                    return null;
                }
                return compEquippable.PrimaryVerb.CasterPawn;
            }
        }
        public bool useAmmo
        {
            get
            {
                return ModSettings.enableAmmoSystem && Props.ammoSet != null;
            }
        }
        public bool hasAndUsesAmmoOrMagazine
        {
        	get
        	{
        		return !useAmmo || hasAmmoOrMagazine;
        	}
        }
        public bool hasAmmoOrMagazine
        {
        	get
        	{
        		return (hasMagazine && curMagCount > 0) || hasAmmo;
        	}
        }
        public bool canBeFiredNow
        {
        	get
        	{
        		return !useAmmo || ((hasMagazine && curMagCount > 0) || (!hasMagazine && hasAmmo));
        	}
        }
        public bool hasAmmo
        {
            get
            {
				return compInventory != null && compInventory.ammoList.Any(x => Props.ammoSet.ammoTypes.Any(a => a.ammo == x.def));
            }
        }
        public bool hasMagazine { get { return Props.magazineSize > 0; } }
        public AmmoDef currentAmmo
        {
            get
            {
                return useAmmo ? currentAmmoInt : null;
            }
        }
        public ThingDef CurAmmoProjectile => Props.ammoSet?.ammoTypes?.FirstOrDefault(x => x.ammo == currentAmmo).projectile;
        public CompInventory compInventory
        {
            get
            {
                return wielder.TryGetComp<CompInventory>();
            }
        }
        private IntVec3 position
        {
            get
            {
                if (wielder != null) return wielder.Position;
                else if (turret != null) return turret.Position;
                else return parent.Position;
            }
        }

        #endregion

        #region Methods

        public override void Initialize(CompProperties vprops)
        {
            base.Initialize(vprops);

            curMagCountInt = Props.spawnUnloaded && useAmmo ? 0 : Props.magazineSize;

            // Initialize ammo with default if none is set
            if (useAmmo)
            {
                if (Props.ammoSet.ammoTypes.NullOrEmpty())
                {
                    Log.Error(parent.Label + " has no available ammo types");
                }
                else
                {
                    if (currentAmmoInt == null)
                        currentAmmoInt = (AmmoDef)Props.ammoSet.ammoTypes[0].ammo;
                    if (selectedAmmo == null)
                        selectedAmmo = currentAmmoInt;
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.LookValue(ref curMagCountInt, "count", 0);
            Scribe_Defs.LookDef(ref currentAmmoInt, "currentAmmo");
            Scribe_Defs.LookDef(ref selectedAmmo, "selectedAmmo");
        }

        private void AssignJobToWielder(Job job)
        {
            if (wielder.drafter != null)
            {
                wielder.jobs.TryTakeOrderedJob(job);
            }
            else
            {
                ExternalPawnDrafter.TakeOrderedJob(wielder, job);
            }
        }

        public bool Notify_ShotFired()
        {
        	if (ammoToBeDeleted != null)
        	{
        		ammoToBeDeleted.Destroy();
        		ammoToBeDeleted = null;
                compInventory.UpdateInventory();
	            if (!hasAmmoOrMagazine)
	            {
	            	return false;
	            }
        	}
            return true;
        }
        
        public bool Notify_PostShotFired()
        {
            if (!hasAmmoOrMagazine)
            {
                DoOutOfAmmoAction();
                return false;
            }
            return true;
        }
        
        /// <summary>
        /// Reduces ammo count and updates inventory if necessary, call this whenever ammo is consumed by the gun (e.g. firing a shot, clearing a jam)
        /// </summary>
        public bool TryReduceAmmoCount()
        {
            if (wielder == null && turret == null)
            {
                Log.Error(parent.ToString() + " tried reducing its ammo count without a wielder");
            }

            // Mag-less weapons feed directly from inventory
            if (!hasMagazine)
            {
                if (useAmmo)
                {
                    if (!TryFindAmmoInInventory(out ammoToBeDeleted))
                    {
                        return false;
                    }
					
                    if (ammoToBeDeleted.stackCount > 1)
                        ammoToBeDeleted = ammoToBeDeleted.SplitOff(1);
                }
                return true;
            }
            // If magazine is empty, return false
            if (curMagCountInt <= 0)
            {
                curMagCountInt = 0;
                return false;
            }
            // Reduce ammo count and update inventory
            curMagCountInt--;
            if (compInventory != null)
            {
                compInventory.UpdateInventory();
            }
            if (curMagCountInt < 0) TryStartReload();
            return true;
        }

        public void TryStartReload(bool unload = false)
        {
            if (!hasMagazine)
            {
                return;
            }
            if (wielder == null && turret == null)
            	return;

            if (useAmmo)
            {
                // Add remaining ammo back to inventory
	            if (curMagCountInt > 0)
	            {
	                Thing ammoThing = ThingMaker.MakeThing(currentAmmoInt);
	                ammoThing.stackCount = curMagCountInt;
	                curMagCountInt = 0;
	
	                if (compInventory != null)
	                {
	                    compInventory.container.TryAdd(ammoThing, ammoThing.stackCount);
	                }
	                else
	                {
	                    Thing outThing;
	                    GenThing.TryDropAndSetForbidden(ammoThing, position, Find.VisibleMap, ThingPlaceMode.Near, out outThing, turret.Faction != Faction.OfPlayer);
	                }
	            }
                
                if (unload) return;

                // Check for ammo
                if (wielder != null && !hasAmmo)
                {
                    DoOutOfAmmoAction();
                    return;
                }
            }

            // Issue reload job
            if (wielder != null)
            {
                Job reloadJob = new Job(CE_JobDefOf.ReloadWeapon, wielder, parent)
                {
                    playerForced = true
                };

                // Store the current job so we can reassign it later
                if (wielder.Faction == Faction.OfPlayer
                    && wielder.CurJob != null
                       && (wielder.CurJob.def == JobDefOf.AttackStatic || wielder.CurJob.def == JobDefOf.Goto || wielder.CurJob.def == JobDefOf.Hunt))
                {
                    if (wielder.CurJob.targetA.HasThing) storedTarget = new LocalTargetInfo(wielder.CurJob.targetA.Thing);
                    else storedTarget = new LocalTargetInfo(wielder.CurJob.targetA.Cell);
                    storedJobDef = wielder.CurJob.def;
                }
                else
                {
                    storedTarget = null;
                    storedJobDef = null;
                }
                AssignJobToWielder(reloadJob);
            }
        }
        
        private void DoOutOfAmmoAction()
        {
            if (Props.throwMote)
            {
                MoteMaker.ThrowText(position.ToVector3Shifted(), Find.VisibleMap, "CE_OutOfAmmo".Translate() + "!");
            }
            if (wielder != null && compInventory != null && (wielder.CurJob == null || wielder.CurJob.def != JobDefOf.Hunt)) compInventory.SwitchToNextViableWeapon();
        }

        public void LoadAmmo(Thing ammo = null)
        {
            if (wielder == null && turret == null)
            {
                Log.Error(parent.ToString() + " tried loading ammo with no owner");
                return;
            }

            int newMagCount;
            if (useAmmo)
            {
                Thing ammoThing;
                bool ammoFromInventory = false;
                if (ammo == null)
                {
                    if (!TryFindAmmoInInventory(out ammoThing))
                    {
                        DoOutOfAmmoAction();
                        return;
                    }
                    ammoFromInventory = true;
                }
                else
                {
                    ammoThing = ammo;
                }
                currentAmmoInt = (AmmoDef)ammoThing.def;
                if (Props.magazineSize < ammoThing.stackCount)
                {
                    newMagCount = Props.magazineSize;
                    ammoThing.stackCount -= Props.magazineSize;
                    if (compInventory != null) compInventory.UpdateInventory();
                }
                else
                {
                    newMagCount = ammoThing.stackCount;
                    if (ammoFromInventory)
                    {
                        compInventory.container.Remove(ammoThing);
                    }
                    else if (!ammoThing.Destroyed)
                    {
                        ammoThing.Destroy();
                    }
                }
            }
            else
            {
                newMagCount = Props.magazineSize;
            }
            curMagCountInt = newMagCount;
            if (turret != null) turret.isReloading = false;
            if (parent.def.soundInteract != null) parent.def.soundInteract.PlayOneShot(new TargetInfo(position,  Find.VisibleMap, false));
            if (Props.throwMote) MoteMaker.ThrowText(position.ToVector3Shifted(), Find.VisibleMap, "CE_ReloadedMote".Translate());
        }

        private bool TryFindAmmoInInventory(out Thing ammoThing)
        {
            ammoThing = null;
            if (compInventory == null)
            {
                return false;
            }

            // Try finding suitable ammoThing for currently set ammo first
            ammoThing = compInventory.ammoList.Find(thing => thing.def == selectedAmmo);
            if (ammoThing != null)
            {
                return true;
            }

            // Try finding ammo from different type
            foreach (AmmoLink link in Props.ammoSet.ammoTypes)
            {
                ammoThing = compInventory.ammoList.Find(thing => thing.def == link.ammo);
                if (ammoThing != null)
                {
                    selectedAmmo = (AmmoDef)link.ammo;
                    return true;
                }
            }
            return false;
        }

        public void TryContinuePreviousJob()
        {
            //If a job is stored, assign it
            if (storedTarget != null && storedJobDef != null)
            {
                AssignJobToWielder(new Job(storedJobDef, storedTarget));

                //Clear out stored job after assignment
                storedTarget = null;
                storedJobDef = null;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            GizmoAmmoStatus ammoStatusGizmo = new GizmoAmmoStatus { compAmmo = this };
            yield return ammoStatusGizmo;

            if ((wielder != null && wielder.Faction == Faction.OfPlayer) || (turret != null && turret.Faction == Faction.OfPlayer))
            {
                Action action = null;
                if (wielder != null) action = delegate { TryStartReload(); };
                else if (turret != null && turret.GetMannableComp() != null) action = turret.OrderReload;

                // Check for teaching opportunities
                string tag;
                if(turret == null)
                {
                    if (hasMagazine) tag = "CE_Reload"; // Teach reloading weapons with magazines
                    else tag = "CE_ReloadNoMag";    // Teach about mag-less weapons
                }
                else
                {
                    if (turret.GetMannableComp() == null) tag = "CE_ReloadAuto";  // Teach about auto-turrets
                    else tag = "CE_ReloadManned";    // Teach about reloading manned turrets
                }
                LessonAutoActivator.TeachOpportunity(ConceptDef.Named(tag), turret, OpportunityType.GoodToKnow);

                Command_Reload reloadCommandGizmo = new Command_Reload
                {
                    compAmmo = this,
                    action = action,
                    defaultLabel = hasMagazine ? "CE_ReloadLabel".Translate() : "",
                    defaultDesc = "CE_ReloadDesc".Translate(),
                    icon = currentAmmo == null ? ContentFinder<Texture2D>.Get("UI/Buttons/Reload", true) : Def_Extensions.IconTexture(selectedAmmo),
                    tutorTag = tag
                };
                yield return reloadCommandGizmo;
            }
        }

		public override string TransformLabel(string label)
		{
            string ammoSet = useAmmo && ModSettings.showCaliberOnGuns ? " (" + Props.ammoSet.LabelCap + ") " : "";
            return  label + ammoSet;
		}

        public override string GetDescriptionPart()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("CE_MagazineSize".Translate() + ": " + GenText.ToStringByStyle(Props.magazineSize, ToStringStyle.Integer));
            stringBuilder.AppendLine("CE_ReloadTime".Translate() + ": " + GenText.ToStringByStyle((Props.reloadTicks / 60), ToStringStyle.Integer) + " s");
            if (useAmmo)
            {
                // Append various ammo stats
                stringBuilder.AppendLine("CE_AmmoSet".Translate() + ": " + Props.ammoSet.LabelCap + "\n");
                foreach(var cur in Props.ammoSet.ammoTypes)
                {
                    string label = string.IsNullOrEmpty(cur.ammo.ammoClass.LabelCapShort) ? cur.ammo.ammoClass.LabelCap : cur.ammo.ammoClass.LabelCapShort;
                    stringBuilder.AppendLine(label + ":\n" + cur.projectile.GetProjectileReadout());
                }
            }
            return stringBuilder.ToString();
        }

        #endregion
    }
}