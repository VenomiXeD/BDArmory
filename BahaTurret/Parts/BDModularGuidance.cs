using System;
using System.Collections.Generic;
using KSP.UI.Screens;
using UniLinq;
using UnityEngine;

namespace BahaTurret
{
    public class BDModularGuidance : MissileBase
    {
        private bool _missileIgnited;
        private int _nextStage = (int) KSPActionGroup.Custom01;

        private PartModule _targetDecoupler;

        private Vessel _targetVessel;

        private Transform _velocityTransform;

        public Vessel LegacyTargetVessel;

        private float detonationRadius;

        private List<Part> vesselParts = new List<Part>();

        public TransformAxisVectors ForwardTransformAxis { get; set; }
        public TransformAxisVectors UpTransformAxis { get; set; }


        public float Mass {
            get { return this.vesselParts.Sum(p => p.mass); }
        }

        public enum TransformAxisVectors
        {
            UpPositive,
            UpNegative,
            ForwardPositive,
            ForwardNegative,
            RightPositive,
            RightNegative
        }
        private void RefreshGuidanceMode()
        {
            switch (_guidanceIndex)
            {
                case 1:
                    GuidanceMode = GuidanceModes.AAMPure;
                    GuidanceLabel = "AAM";
                    break;
                case 2:
                    GuidanceMode = GuidanceModes.AGM;
                    GuidanceLabel = "AGM/STS";
                    break;
                case 3:
                    GuidanceMode = GuidanceModes.Cruise;
                    GuidanceLabel = "Cruise";
                    break;
            }

            if (Fields["CruiseAltitude"] != null)
            {
                Fields["CruiseAltitude"].guiActive = _guidanceIndex == 3;
                Fields["CruiseAltitude"].guiActiveEditor = _guidanceIndex == 3;
            }


            Misc.RefreshAssociatedWindows(part);
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            CheckDetonationDistance();

            UpdateGuidance();

            CheckDelayedFired();

            CheckNextStage();

            if (isTimed && TimeIndex > detonationTime)
            {
                AutoDestruction();
            }
        }

        private void CheckDetonationDistance()
        {
            //Guard clauses
            if (!HasFired) return;
            if (!TargetAcquired) return;
            if (Vector3.Distance(vessel.CoM, SourceVessel.CoM) < 4 * detonationRadius) return;
            if (Vector3.Distance(vessel.CoM, TargetPosition) > 10 * detonationRadius) return;

            var effectiveTargetAcceleration = TargetVelocity - previousTargetVelocity;
            var effectiveMissileAcceleration = (float)vessel.srfSpeed * vessel.srf_velocity.normalized -
                                           previousMissileVelocity;

            var futureTargetPosition = TargetPosition + (TargetVelocity*Time.fixedDeltaTime) +
                                        0.5f*effectiveTargetAcceleration*Time.fixedDeltaTime*Time.fixedDeltaTime;
            var missileTargetPosition = vessel.CoM +
                                        (float) vessel.srfSpeed*vessel.srf_velocity.normalized*Time.fixedDeltaTime +
                                        0.5f*effectiveMissileAcceleration*Time.fixedDeltaTime*Time.fixedDeltaTime;
            float distance;
            if ((distance = Vector3.Distance(futureTargetPosition, missileTargetPosition)) <= detonationRadius)
            {
                Debug.Log("BDModularGuidance::CheckDetonationDistance - Proximity detonation activated Distance="+distance);
                Detonate();
            }
        }

        private void CheckNextStage()
        {
            if (HasFired && ShouldExecuteNextStage())
            {
                ExecuteNextStage();
            }
        }

        private void CheckDelayedFired()
        {
            if (HasFired && !_missileIgnited)
            {
                if (Time.time - timeFired > dropTime)
                {
                    MissileIgnition();
                }
            }
        }

        private void DisableRecursiveFlow(List<Part> children)
        {
            foreach (var child in children)
            {
                foreach (var resource in child.Resources)
                {
                    if (resource.flowState)
                    {
                        resource.flowState = false;
                    }
                }
                if (child.children.Count > 0)
                {
                    DisableRecursiveFlow(child.children);
                }
                 if(!this.vesselParts.Contains(child)) this.vesselParts.Add(child);
            }
        }

        private void EnableResourceFlow(List<Part> children)
        {
            foreach (var child in children)
            {
                foreach (var resource in child.Resources)
                {
                    if (!resource.flowState)
                    {
                        resource.flowState = true;
                    }
                }
                if (child.children.Count > 0)
                {
                    EnableResourceFlow(child.children);
                }
            }
        }

        private void DisableResourcesFlow()
        {
            if (_targetDecoupler != null)
            {
                if (_targetDecoupler.part.children.Count == 0) return;
                this.vesselParts.Clear();
                DisableRecursiveFlow(_targetDecoupler.part.children);

            }
        }
        private void MissileIgnition()
        {
            this.vesselParts.Clear();
            this.vesselParts = vessel.parts;
            EnableResourceFlow(vesselParts);
            var velocityObject = new GameObject("velObject");
            velocityObject.transform.position = vessel.transform.position;
            velocityObject.transform.parent = vessel.transform;
            _velocityTransform = velocityObject.transform;

            MissileState = MissileStates.Boost;

            ExecuteNextStage();

            _missileIgnited = true;
            RadarWarningReceiver.WarnMissileLaunch(MissileReferenceTransform.position, GetForwardTransform());
        }

        private bool ShouldExecuteNextStage()
        {
            var ret = true;
            //If the next stage is greater than the number defined of stages the missile is done
          
            foreach (
                var engine in
                    vessel.parts.Where(IsEngine).Select(x => x.FindModuleImplementing<ModuleEngines>()))
            {
                if (engine.EngineIgnited && !engine.getFlameoutState)
                {
                    ret = false;
                    break;
                }
            }

            if (ret)
            {
                //If the next stage is greater than the number defined of stages the missile is done
                if (_nextStage > 128*(StagesNumber + 1))
                {
                    MissileState = MissileStates.PostThrust;
                    return false;
                }

            }
            return ret;
        }

        public bool IsEngine(Part p)
        {
            for (var i = 0; i < p.Modules.Count; i++)
            {
                var m = p.Modules[i];
                if (m is ModuleEngines)
                    return true;
            }
            return false;
        }

        public override void OnStart(StartState state)
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;

            if (isTimed)
            {
                Fields["detonationTime"].guiActive = true;
                Fields["detonationTime"].guiActiveEditor = true;
            }
            else
            {
                Fields["detonationTime"].guiActive = false;
                Fields["detonationTime"].guiActiveEditor = false;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
                WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
                Fields["CruiseAltitude"].guiActiveEditor = true;
                Events["SwitchTargetingMode"].guiActiveEditor = true;
                Events["SwitchGuidanceMode"].guiActiveEditor = true;
            }
            else
            {
                Fields["CruiseAltitude"].guiActiveEditor = false;
                Events["SwitchTargetingMode"].guiActiveEditor = false;
                Events["SwitchGuidanceMode"].guiActiveEditor = false;
                SetMissileTransform();
                detonationRadius = DetonationDistance > 0 ? DetonationDistance : GetBlastRadius();
            }
            if (string.IsNullOrEmpty(GetShortName()))
            {
                shortName = "Unnamed";
            }
            part.force_activate();
            RefreshGuidanceMode();

            UpdateTargetingMode((TargetingModes) Enum.Parse(typeof(TargetingModes),_targetingLabel));

            _targetDecoupler = FindFirstDecoupler(part.parent, null);

            DisableResourcesFlow();

            weaponClass = WeaponClasses.Missile;
            WeaponName = GetShortName();
                      
            this.activeRadarRange = ActiveRadarRange;


            //TODO: BDModularGuidance should be configurable?
            lockedSensorFOV = 5;
            maxStaticLaunchRange = ActiveRadarRange*1.25f;
            minStaticLaunchRange = 500;
            radarLOAL = true;
        }

        private void UpdateTargetingMode(TargetingModes newTargetingMode)
        {
            if (newTargetingMode == TargetingModes.Radar)
            {
                Fields["ActiveRadarRange"].guiActive = true;
                Fields["ActiveRadarRange"].guiActiveEditor = true;
            }
            else
            {
                Fields["ActiveRadarRange"].guiActive = false;
                Fields["ActiveRadarRange"].guiActiveEditor = false;
            }
            this.TargetingMode = newTargetingMode;
            this._targetingLabel = newTargetingMode.ToString();

            Misc.RefreshAssociatedWindows(part);
        }

        private void OnDestroy()
        {
            WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
            WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
        }

        private void SetMissileTransform()
        {
            MissileReferenceTransform = part.transform;
            this.ForwardTransformAxis = (TransformAxisVectors) Enum.Parse(typeof(TransformAxisVectors), ForwardTransform);
            this.UpTransformAxis = (TransformAxisVectors)Enum.Parse(typeof(TransformAxisVectors), UpTransform);
        }      

        /// <summary>
        /// This method will obtain the expected transform forward that matches the expected BD missile transform
        /// </summary>
        //private TransformAxisVectors CalculateTransform(Vector3 referenceVector)
        //{
        //    var vectorAngles = new Dictionary<TransformAxisVectors, float>
        //    {
        //        {
        //            TransformAxisVectors.ForwardPositive,
        //            Vector3.Angle(MissileReferenceTransform.forward.normalized, referenceVector)
        //        },
        //        {
        //            TransformAxisVectors.ForwardNegative,
        //            Vector3.Angle(-MissileReferenceTransform.forward.normalized, referenceVector)
        //        },
        //        {
        //            TransformAxisVectors.UpNegative,
        //            Vector3.Angle(-MissileReferenceTransform.up.normalized, referenceVector)
        //        },
        //        {
        //            TransformAxisVectors.UpPositive,
        //            Vector3.Angle(MissileReferenceTransform.up.normalized, referenceVector)
        //        },
        //        {
        //            TransformAxisVectors.RightPositive,
        //            Vector3.Angle(MissileReferenceTransform.right.normalized, referenceVector)
        //        },
        //         {
        //            TransformAxisVectors.RightNegative,
        //            Vector3.Angle(-MissileReferenceTransform.right.normalized, referenceVector)
        //         }

        //    };
        //    var result = vectorAngles.First(x => x.Value == vectorAngles.Min(y => y.Value)).Key;
        //    return result;
        //}


        void UpdateGuidance()
        {
            if (!HasFired) return;
            if (guidanceActive)
            {
                switch (TargetingMode)
                {
                    case TargetingModes.None:
                        if (_targetVessel != null)
                        {
                            TargetPosition = _targetVessel.CurrentCoM;
                            TargetVelocity = _targetVessel.rb_velocity;
                            TargetAcceleration = _targetVessel.acceleration;
                        }
                        break;
                    case TargetingModes.Radar:
                        UpdateRadarTarget();
                        break;
                    case TargetingModes.Heat:
                        UpdateHeatTarget();
                        break;
                    case TargetingModes.Laser:
                        UpdateLaserTarget();
                        break;
                    case TargetingModes.Gps:
                         UpdateGPSTarget();
                        break;
                    case TargetingModes.AntiRad:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        private  Vector3 previousTargetVelocity { get; set; } = Vector3.zero;
        private Vector3 previousMissileVelocity { get; set; } = Vector3.zero;
        private Vector3 AAMGuidance()
        {
            Vector3 aamTarget;
            if (TargetAcquired)
            {
                float timeToImpact;
                aamTarget = MissileGuidance.GetAirToAirTargetModular(TargetPosition, TargetVelocity, previousTargetVelocity, TargetAcceleration, vessel, previousMissileVelocity, out timeToImpact);
                previousTargetVelocity = TargetVelocity;
                previousMissileVelocity = vessel.srf_velocity;
                TimeToImpact = timeToImpact;
                //if (Vector3.Angle(aamTarget - vessel.CoM, vessel.transform.forward) > maxOffBoresight * 0.75f)
                //{
                //    Debug.Log("BDModularGuidance:AAMGuidance not tracking");
                //    aamTarget = TargetPosition;
                //}
                DrawDebugLine(vessel.CoM, aamTarget);
            }
            else
            {
                aamTarget = vessel.CoM + (20 * vessel.srfSpeed * vessel.srf_velocity.normalized);
            }

            return aamTarget;
        }

        private Vector3 AGMGuidance()
        {
            if (TargetingMode != TargetingModes.Gps)
            {
                if (TargetAcquired)
                {
                    //lose lock if seeker reaches gimbal limit
                    float targetViewAngle = Vector3.Angle(GetForwardTransform(), TargetPosition - vessel.CoM);

                    if (targetViewAngle > maxOffBoresight)
                    {
                        Debug.Log("AGM Missile guidance failed - target out of view");
                        guidanceActive = false;
                    }
                }
                else
                {
                    if (TargetingMode == TargetingModes.Laser)
                    {
                        //keep going straight until found laser point
                        TargetPosition = laserStartPosition + (20000 * startDirection);
                    }
                }
            }
            Vector3 agmTarget = MissileGuidance.GetAirToGroundTarget(TargetPosition, vessel, 1.85f);
            return agmTarget;
        }

        private Vector3 CruiseGuidance()
        {
            Vector3 cruiseTarget = Vector3.zero;
            float distance = Vector3.Distance(TargetPosition, vessel.CoM);

            if (distance < 4500)
            {
                cruiseTarget = MissileGuidance.GetTerminalManeuveringTarget(TargetPosition, vessel, CruiseAltitude);
                debugString += "\nTerminal Maneuvers";
            }
            else
            {
                float agmThreshDist = 2500;
                if (distance < agmThreshDist)
                {
                    if (!MissileGuidance.GetBallisticGuidanceTarget(TargetPosition, vessel, true, out cruiseTarget))
                    {
                        cruiseTarget = MissileGuidance.GetAirToGroundTarget(TargetPosition, vessel, 1.85f);
                    }

                    debugString += "\nDescending On Target";
                }
                else
                {
                    cruiseTarget = MissileGuidance.GetCruiseTarget(TargetPosition, vessel, CruiseAltitude);
                    debugString += "\nCruising";
                }
            }

            debugString += "\nRadarAlt: " + MissileGuidance.GetRadarAltitude(vessel);

            return cruiseTarget;
        }
        private void CheckMiss()
        {
          
            double sqrDist = ((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (vessel.CoM + (vessel.srf_velocity * Time.fixedDeltaTime))).sqrMagnitude;
            if (sqrDist < 160000 || (MissileState == MissileStates.PostThrust && (GuidanceMode == GuidanceModes.AAMLead || GuidanceMode == GuidanceModes.AAMPure)))
            {
                checkMiss = true;
            }

            //kill guidance if missileBase has missed
            if (!HasMissed && checkMiss)
            {
                if (Vector3.Dot(TargetPosition - vessel.CoM, GetForwardTransform()) < 0 )
                {
                    Debug.Log("Missile CheckMiss showed miss");
                    HasMissed = true;
                    guidanceActive = false;

                    TargetMf = null;

                    if (sqrDist < Mathf.Pow(detonationRadius * 0.5f, 2)) AutoDestruction();

                    isTimed = true;
                    detonationTime = Time.time - timeFired + 1.5f;
                    return;
                }
            }
        }
        public void GuidanceSteer(FlightCtrlState s)
        {
            if (guidanceActive && MissileReferenceTransform != null && _velocityTransform != null)
            {
                if (_guidanceIndex == 1)
                {
                    TargetPosition = AAMGuidance();
                    CheckMiss();
                }
                else if (_guidanceIndex == 2)
                {
                    TargetPosition = AGMGuidance();
                }
                else if (_guidanceIndex == 3)
                {
                    TargetPosition = CruiseGuidance();
                }
                //Updating aero surfaces
                if (Time.time - timeFired > dropTime + 0.5f)
                {
                    _velocityTransform.rotation = Quaternion.LookRotation(vessel.srf_velocity, GetTransform(UpTransformAxis));
                    var targetDirection = _velocityTransform.InverseTransformPoint(TargetPosition).normalized;
                    targetDirection = Vector3.RotateTowards(Vector3.forward, targetDirection, 15*Mathf.Deg2Rad, 0);

                    var localAngVel = vessel.angularVelocity;
                    var steerYaw = SteerMult*targetDirection.x - SteerDamping*-localAngVel.z;
                    var steerPitch = SteerMult*targetDirection.y - SteerDamping*-localAngVel.x;

                    s.yaw = Mathf.Clamp(steerYaw, -MaxSteer, MaxSteer);
                    s.pitch = Mathf.Clamp(steerPitch, -MaxSteer, MaxSteer);
                }

                s.mainThrottle = 1;
            }
           
        }

        private void UpdateMenus(bool visible)
        {
            Events["HideUI"].active = visible;
            Events["ShowUI"].active = !visible;
        }

        private void OnActionGroupEditorOpened()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = false;
        }

        private void OnActionGroupEditorClosed()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
        }

        /// <summary>
        ///     Recursive method to find the top decoupler that should be used to jettison the missile.
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="last"></param>
        /// <returns></returns>
        private PartModule FindFirstDecoupler(Part parent, PartModule last)
        {
            if (parent == null || !parent) return last;

            PartModule newModuleDecouple = parent.FindModuleImplementing<ModuleDecouple>();
            if (newModuleDecouple == null)
            {
                newModuleDecouple = parent.FindModuleImplementing<ModuleAnchoredDecoupler>();
            }
            if (newModuleDecouple != null && newModuleDecouple)
            {
                return FindFirstDecoupler(parent.parent, newModuleDecouple);
            }
            return FindFirstDecoupler(parent.parent, last);
        }

        /// <summary>
        ///     This method will execute the next ActionGroup. Due to StageManager is designed to work with an active vessel
        ///     And a missile is not an active vessel. I had to use a different way handle stages. And action groups works perfect!
        /// </summary>
        public void ExecuteNextStage()
        {
            part.vessel.OnFlyByWire -= GuidanceSteer;
            part.vessel.ActionGroups.ToggleGroup((KSPActionGroup) _nextStage);

            _nextStage *= 2;

            part.vessel.OnFlyByWire += GuidanceSteer;
        }

        #region KSP FIELDS

        [KSPField]
        public string ForwardTransform = "ForwardNegative";
        [KSPField]
        public string UpTransform = "RightPositive";

        [KSPField(isPersistant = true, guiActive = true, guiName = "Weapon Name ", guiActiveEditor = true), UI_Label (affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]
        public string WeaponName;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "CruiseAltitude"), UI_FloatRange(minValue = 50f, maxValue = 1500f, stepIncrement = 50f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float CruiseAltitude = 500;

        [KSPField(isPersistant = false, guiActive = true, guiName = "Guidance Type ", guiActiveEditor = true)]
        public string GuidanceLabel = "AGM/STS";

        [KSPField(isPersistant = true, guiActive = true, guiName = "Targeting Mode ", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]
        private string _targetingLabel = TargetingModes.None.ToString();

        [KSPField(isPersistant = true)]
        public int _guidanceIndex = 2;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Active Radar Range"), UI_FloatRange(minValue = 6000f, maxValue = 50000f, stepIncrement = 1000f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float ActiveRadarRange = 6000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Limiter"), UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float MaxSteer = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Stages Number"), UI_FloatRange(minValue = 1f, maxValue = 5f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float StagesNumber = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Detonation distance"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float DetonationDistance = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Damping"), UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = .05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float SteerDamping = 5;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steer Factor"), UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float SteerMult = 10;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Max Off Boresight"), UI_FloatRange(minValue = 0f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public new float maxOffBoresight = 1;

        #endregion

        #region KSP ACTIONS
        [KSPAction("Fire Missile")]
        public void AgFire(KSPActionParam param)
        {
            FireMissile();
            if (BDArmorySettings.Instance.ActiveWeaponManager != null)
                BDArmorySettings.Instance.ActiveWeaponManager.UpdateList();
        }

        #endregion

        #region KSP EVENTS

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Fire Missile", active = true)]
        public override void FireMissile()
        {
            if (!HasFired)
            {
                GameEvents.onPartDie.Add(PartDie);
                BDATargetManager.FiredMissiles.Add(this);

                foreach (var wpm in vessel.FindPartModulesImplementing<MissileFire>())
                {
                    Team = wpm.team;
                    break;
                }

                SourceVessel = vessel;
                SetTargeting();

                //add target info to vessel


                Jettison();

                AddTargetInfoToVessel();

                vessel.vesselName = GetShortName();
                vessel.vesselType = VesselType.Station;

                timeFired = Time.time;

                MissileState = MissileStates.Drop;
                Misc.RefreshAssociatedWindows(part);

                HasFired = true;
            }
        }

        private void SetTargeting()
        {
            startDirection = GetForwardTransform();
            SetLaserTargeting();
        }


        public Vector3 StartDirection { get; set; }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "Guidance Mode", active = true)]
        public void SwitchGuidanceMode()
        {
            _guidanceIndex++;
            if (_guidanceIndex > 3)
            {
                _guidanceIndex = 1;
            }

            RefreshGuidanceMode();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "Targeting Mode", active = true)]
        public void SwitchTargetingMode()
        {
            var targetingModes = Enum.GetNames(typeof(TargetingModes));

            var currentIndex = targetingModes.IndexOf(TargetingMode.ToString());

            if (currentIndex < targetingModes.Length - 1)
            {
                UpdateTargetingMode((TargetingModes) Enum.Parse(typeof(TargetingModes), targetingModes[currentIndex + 1]));
            }
            else
            {
                UpdateTargetingMode((TargetingModes) Enum.Parse(typeof(TargetingModes), targetingModes[0]));
            }
        }


        [KSPEvent(guiActive = true, guiActiveEditor = false, active = true, guiName = "Jettison")]
        public override void Jettison()
        {
            if (_targetDecoupler == null || !_targetDecoupler || !(_targetDecoupler is IStageSeparator)) return;


            var decouple = _targetDecoupler as ModuleDecouple;
            if (decouple != null)
            {
                decouple.ejectionForce *= 5; 
                 decouple.Decouple();
            }
            else
            {
                ((ModuleAnchoredDecoupler) _targetDecoupler).ejectionForce *= 5;
                ((ModuleAnchoredDecoupler) _targetDecoupler).Decouple();
            }

            if (BDArmorySettings.Instance.ActiveWeaponManager != null)
                BDArmorySettings.Instance.ActiveWeaponManager.UpdateList();
        }

     
        public override float GetBlastRadius()
        {
            if (vessel.FindPartModulesImplementing<BDExplosivePart>().Count > 0)
            {
                return vessel.FindPartModulesImplementing<BDExplosivePart>().Max(x => x.blastRadius);
            }
            else
            {
                return 5;
            }
        }

        protected override void PartDie(Part p)
        {
            if (p == part)
            {
                AutoDestruction();
                BDATargetManager.FiredMissiles.Remove(this);
                GameEvents.onPartDie.Remove(PartDie);
            }
        }

        private void AutoDestruction()
        {
            foreach (var vesselPart in vesselParts)
            { 
                vesselPart.temperature = part.maxTemp + 100;
            }
        }

        public override void Detonate()
        {
            if (HasFired)
            {
                AutoDestruction();          
            }
        }

        public override Vector3 GetForwardTransform()
        {
            return GetTransform(ForwardTransformAxis);
        }

        public Vector3 GetTransform(TransformAxisVectors transformAxis)
        {
            switch (transformAxis)
            {
                case TransformAxisVectors.UpPositive:
                    return this.MissileReferenceTransform.up;
                case TransformAxisVectors.UpNegative:
                    return -this.MissileReferenceTransform.up;
                case TransformAxisVectors.ForwardPositive:
                    return this.MissileReferenceTransform.forward;
                case TransformAxisVectors.ForwardNegative:
                    return -this.MissileReferenceTransform.forward;
                case TransformAxisVectors.RightNegative:
                    return -this.MissileReferenceTransform.right;
                case TransformAxisVectors.RightPositive:
                    return this.MissileReferenceTransform.right;
                default:
                    return this.MissileReferenceTransform.forward;
            }
        }


        [KSPEvent(guiActiveEditor = true, guiName = "Hide Weapon Name UI", active = false)]
        public void HideUI()
        {
            WeaponNameWindow.HideGUI();
            UpdateMenus(false);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "Set Weapon Name UI", active = false)]
        public void ShowUI()
        {
            WeaponNameWindow.ShowGUI(this);
            UpdateMenus(true);
        }

        #endregion
    }


    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class WeaponNameWindow : MonoBehaviour
    {
        internal static EventVoid OnActionGroupEditorOpened = new EventVoid("OnActionGroupEditorOpened");
        internal static EventVoid OnActionGroupEditorClosed = new EventVoid("OnActionGroupEditorClosed");

        private static GUIStyle unchanged;
        private static GUIStyle changed;
        private static GUIStyle greyed;
        private static GUIStyle overfull;

        private static WeaponNameWindow instance;
        private static Vector3 mousePos = Vector3.zero;

        private bool ActionGroupMode;

        private Rect guiWindowRect = new Rect(0, 0, 0, 0);

        private BDModularGuidance missile_module;

        [KSPField] public int offsetGUIPos = -1;

        private Vector2 scrollPos;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Show Weapon Name Editor"), UI_Toggle(enabledText = "Weapon Name GUI", disabledText = "GUI")] [NonSerialized] public bool showRFGUI;

        private bool styleSetup;

        private string txtName = string.Empty;

        public static void HideGUI()
        {
            if (instance != null && instance.missile_module != null)
            {
                instance.missile_module.WeaponName = instance.missile_module.shortName;
                instance.missile_module = null;
                instance.UpdateGUIState();
            }
            var editor = EditorLogic.fetch;
            if (editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        public static void ShowGUI(BDModularGuidance missile_module)
        {
            if (instance != null)
            {
                instance.missile_module = missile_module;
                instance.UpdateGUIState();
            }
        }

        private void UpdateGUIState()
        {
            enabled = missile_module != null;
            var editor = EditorLogic.fetch;
            if (!enabled && editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        private IEnumerator<YieldInstruction> CheckActionGroupEditor()
        {
            while (EditorLogic.fetch == null)
            {
                yield return null;
            }
            var editor = EditorLogic.fetch;
            while (EditorLogic.fetch != null)
            {
                if (editor.editorScreen == EditorScreen.Actions)
                {
                    if (!ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorOpened.Fire();
                    }
                    var age = EditorActionGroups.Instance;
                    if (missile_module && !age.GetSelectedParts().Contains(missile_module.part))
                    {
                        HideGUI();
                    }
                    ActionGroupMode = true;
                }
                else
                {
                    if (ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorClosed.Fire();
                    }
                    ActionGroupMode = false;
                }
                yield return null;
            }
        }

        private void Awake()
        {
            enabled = false;
            instance = this;
            StartCoroutine(CheckActionGroupEditor());
        }

        private void OnDestroy()
        {
            instance = null;
        }

        public void OnGUI()
        {
            if (!styleSetup)
            {
                styleSetup = true;
                Styles.InitStyles();
            }

            var editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor)
            {
                return;
            }
            var cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

            var posMult = 0;
            if (offsetGUIPos != -1)
            {
                posMult = offsetGUIPos;
            }
            if (ActionGroupMode)
            {
                if (guiWindowRect.width == 0)
                {
                    guiWindowRect = new Rect(430*posMult, 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin + 440, mousePos.y - 5, 300, 20);
            }
            else
            {
                if (guiWindowRect.width == 0)
                {
                    //guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
                    guiWindowRect = new Rect(Screen.width - 8 - 430*(posMult + 1), 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin - (230 - 8), mousePos.y - 5, 220, 20);
            }
            cursorInGUI = guiWindowRect.Contains(mousePos);
            if (cursorInGUI)
            {
                editor.Lock(false, false, false, "BD_MN_GUILock");
                //if (EditorTooltip.Instance != null)
                //    EditorTooltip.Instance.HideToolTip();
            }
            else
            {
                editor.Unlock("BD_MN_GUILock");
            }
            guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, "Weapon Name GUI", Styles.styleEditorPanel);
        }

        public void GUIWindow(int windowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();

            GUILayout.Label("Weapon Name: ");


            txtName = GUILayout.TextField(txtName);


            if (GUILayout.Button("Save & Close"))
            {
                missile_module.WeaponName = txtName;
                missile_module.shortName = txtName;
                instance.missile_module.HideUI();
            }

            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private static void InitializeStyles()
        {
            if (unchanged == null)
            {
                if (GUI.skin == null)
                {
                    unchanged = new GUIStyle();
                    changed = new GUIStyle();
                    greyed = new GUIStyle();
                    overfull = new GUIStyle();
                }
                else
                {
                    unchanged = new GUIStyle(GUI.skin.textField);
                    changed = new GUIStyle(GUI.skin.textField);
                    greyed = new GUIStyle(GUI.skin.textField);
                    overfull = new GUIStyle(GUI.skin.label);
                }

                unchanged.normal.textColor = Color.white;
                unchanged.active.textColor = Color.white;
                unchanged.focused.textColor = Color.white;
                unchanged.hover.textColor = Color.white;

                changed.normal.textColor = Color.yellow;
                changed.active.textColor = Color.yellow;
                changed.focused.textColor = Color.yellow;
                changed.hover.textColor = Color.yellow;

                greyed.normal.textColor = Color.gray;

                overfull.normal.textColor = Color.red;
            }
        }
    }

    internal class Styles
    {
        // Base styles
        public static GUIStyle styleEditorTooltip;
        public static GUIStyle styleEditorPanel;


        /// <summary>
        ///     This one sets up the styles we use
        /// </summary>
        internal static void InitStyles()
        {
            styleEditorTooltip = new GUIStyle();
            styleEditorTooltip.name = "Tooltip";
            styleEditorTooltip.fontSize = 12;
            styleEditorTooltip.normal.textColor = new Color32(207, 207, 207, 255);
            styleEditorTooltip.stretchHeight = true;
            styleEditorTooltip.wordWrap = true;
            styleEditorTooltip.normal.background = CreateColorPixel(new Color32(7, 54, 66, 200));
            styleEditorTooltip.border = new RectOffset(3, 3, 3, 3);
            styleEditorTooltip.padding = new RectOffset(4, 4, 6, 4);
            styleEditorTooltip.alignment = TextAnchor.MiddleLeft;

            styleEditorPanel = new GUIStyle();
            styleEditorPanel.normal.background = CreateColorPixel(new Color32(7, 54, 66, 200));
            styleEditorPanel.border = new RectOffset(27, 27, 27, 27);
            styleEditorPanel.padding = new RectOffset(10, 10, 10, 10);
            styleEditorPanel.normal.textColor = new Color32(147, 161, 161, 255);
            styleEditorPanel.fontSize = 12;
        }


        /// <summary>
        ///     Creates a 1x1 texture
        /// </summary>
        /// <param name="Background">Color of the texture</param>
        /// <returns></returns>
        internal static Texture2D CreateColorPixel(Color32 Background)
        {
            var retTex = new Texture2D(1, 1);
            retTex.SetPixel(0, 0, Background);
            retTex.Apply();
            return retTex;
        }
    }
}