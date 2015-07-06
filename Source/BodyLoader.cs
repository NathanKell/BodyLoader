/*BodyLoader by NathanKell
 * License: MIT.

Copyright (c) 2015 NathanKell

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP;
using System.Reflection;

namespace BodyLoader
{

    [KSPAddon(KSPAddon.Startup.PSystemSpawn, true)]
    class BodyLoader : MonoBehaviour
    {
        static bool loadedData = false;
        static MethodInfo afgSetMaterial = null;
        
        static double globalRescale = 1d;
        static double globalOrbitRescale = 1d;
        static double globalRotationRescale = 1d;
        static double globalRescaleAtmo = 1d;

        const double DEG2RAD = Math.PI / 180.0;

        static bool patchOrbits = false;

        static CelestialBody GetBody(string bodyName)
        {
            foreach(CelestialBody b in FlightGlobals.Bodies)
                if(b.name.Equals(bodyName))
                    return b;
            return null;
        }

        static ConfigNode GetNode(string bodyName, ConfigNode rootnode)
        {
            foreach(ConfigNode node in rootnode.nodes)
                if (node.name.Equals(bodyName))
                    return node;
            return null;
        }
        static void GeeASLToOthers(CelestialBody body)
        {
            double rsq = body.Radius * body.Radius;
            body.gMagnitudeAtCenter = body.GeeASL * 9.81 * rsq;
            body.gravParameter = body.gMagnitudeAtCenter;
            body.Mass = body.gravParameter * (1 / 6.674E-11);
            patchOrbits = true;
        }
        static bool PatchPQS(CelestialBody body, ConfigNode node, double origRadius)
        {
            List<string> PQSs = new List<string>();
            bool modified = false;
            bool custom = false;
            if (node != null && node.HasNode("PQS"))
            {
                foreach (ConfigNode n in node.GetNode("PQS").nodes)
                    PQSs.Add(n.name);
                custom = true;
            }
            else
            {
                if (body.Radius != origRadius)
                {
                    PQSs.Add(body.bodyName);
                    PQSs.Add(body.bodyName + "Ocean");
                }
            }
            foreach (string pName in PQSs)
            {
                print("**Patching PQS " + pName);
                // yeah, slow, but juuuuuuuuust in case.
                foreach (PQS p in Resources.FindObjectsOfTypeAll(typeof(PQS)))
                {
                    if (p.name.Equals(pName))
                    {
                        /*if (body.pqsController != p)
                            if (body.pqsController != p.parentSphere)
                                continue;*/
                        if (p.radius != body.Radius)
                            modified = true;

                        p.radius = body.Radius;
                        // do nothing yet, because I don't want to copy-paste all my code
                        var mods = p.transform.GetComponentsInChildren(typeof(PQSMod), true);
                        // rebuilding should catch it, but...
                        foreach (var m in mods)
                        {
                            if (m is PQSCity)
                            {
                                PQSCity mod = (PQSCity)m;
                                try
                                {
                                    mod.OnSetup();
                                    mod.OnPostSetup();
                                }
                                catch
                                {
                                }
                                //SpaceCenter.Instance.transform.localPosition = mod.transform.localPosition;
                                //SpaceCenter.Instance.transform.localRotation = mod.transform.localRotation;
                            }
                            if (m is PQSMod_MapDecal)
                            {
                                PQSMod_MapDecal mod = (PQSMod_MapDecal)m;
                                mod.radius *= globalRescale;
                                mod.position *= (float)globalRescale;
                                try
                                {
                                    mod.OnSetup();
                                    mod.OnPostSetup();
                                }
                                catch
                                {
                                }
                            }
                            if (m is PQSMod_MapDecalTangent)
                            {
                                PQSMod_MapDecalTangent mod = (PQSMod_MapDecalTangent)m;
                                mod.radius *= globalRescale;
                                mod.position *= (float)globalRescale;
                                try
                                {
                                    mod.OnSetup();
                                    mod.OnPostSetup();
                                }
                                catch
                                {
                                }
                            }
                        }
                        try
                        {
                            p.RebuildSphere();
                        }
                        catch (Exception e)
                        {
                            print("Rebuild sphere for " + node.name + " failed: " + e.Message);
                        }
                    }
                }
            }
            return modified;
        }
        static FloatCurve RescaleCurve(FloatCurve curve, double fac)
        {
            AnimationCurve c = curve.Curve;
            FloatCurve newCurve = new FloatCurve();
            double facR = 1d / fac;
            foreach(Keyframe key in c.keys)
                newCurve.Add((float)(key.time * fac), key.value, (float)(key.inTangent * facR), (float)(key.outTangent * facR));

            return newCurve;
        }
        public static void UpdateAFG(CelestialBody body, AtmosphereFromGround afg, ConfigNode modNode = null)
        {
            // the defaults -- don't support the full RSS panoply
            afg.outerRadius = (float)body.Radius * 1.025f * ScaledSpace.InverseScaleFactor;
            afg.innerRadius = afg.outerRadius * 0.975f;

            // the stuff Start and UpdateAtmosphere(true) does
            afg.KrESun = afg.Kr * afg.ESun;
            afg.KmESun = afg.Km * afg.ESun;
            afg.Kr4PI = afg.Kr * 4f * (float)Math.PI;
            afg.Km4PI = afg.Km * 4f * (float)Math.PI;
            afg.g2 = afg.g * afg.g;
            afg.outerRadius2 = afg.outerRadius * afg.outerRadius;
            afg.innerRadius2 = afg.innerRadius * afg.innerRadius;
            afg.scale = 1f / (afg.outerRadius - afg.innerRadius);
            afg.scaleDepth = -0.25f;
            afg.scaleOverScaleDepth = afg.scale / afg.scaleDepth;
            try
            {
                afgSetMaterial.Invoke(afg, new object[] { true });
            }
            catch (Exception e)
            {
                print("**ERROR setting AtmosphereFromGround " + afg.name + " for body " + body.name + ": " + e);
            }
        }
        private IEnumerator<YieldInstruction> PatchScaledSpace(CelestialBody body, ConfigNode node, double origRadius)
        {
            while (ScaledSpace.Instance == null || ScaledSpace.Instance.scaledSpaceTransforms == null)
                yield return new WaitForSeconds(1f);

            foreach (Transform t in ScaledSpace.Instance.scaledSpaceTransforms)
            {
                if (t.name.Equals(body.bodyName))
                {
                    float origLocalScale = t.localScale.x; // assume uniform scale
                    float scaleFactor = (float)((double)origLocalScale * body.Radius / origRadius);
                    t.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
                }
            }

            // AFG
            ConfigNode afgNode = null;
            if(node != null)
                afgNode = node.GetNode("AtmosphereFromGround");
            foreach (AtmosphereFromGround ag in Resources.FindObjectsOfTypeAll(typeof(AtmosphereFromGround)))
            {
                //print("*RSS* Found AG " + ag.name + " " + (ag.tag == null ? "" : ag.tag) + ". Planet " + (ag.planet == null ? "NULL" : ag.planet.name));
                if (ag != null && ag.planet != null)
                {
                    if (ag.planet.name.Equals(body.bodyName))
                    {
                        UpdateAFG(body, ag, afgNode);
                    }
                }
            }
            print("**ScaledSpace done for " + body.bodyName);
            yield return null;
        }
        public bool PatchBody(CelestialBody body, ConfigNode node)
        {
            if((object)body == null)
                return false;
            double dtmp;
            bool btmp;

            bool updateMass = false;
            double origRadius = body.Radius;

            if (node != null)
            {
                print("Patching body " + body.bodyName + " from node.");
                if (node.HasValue("rotationPeriod"))
                    if (double.TryParse(node.GetValue("rotationPeriod"), out dtmp))
                        body.rotationPeriod = dtmp;
                if (node.HasValue("atmosphereDepth"))
                    if (double.TryParse(node.GetValue("atmosphereDepth"), out dtmp))
                        body.atmosphereDepth = dtmp;
                if (node.HasValue("atmosphereAdiabaticIndex"))
                    if (double.TryParse(node.GetValue("atmosphereAdiabaticIndex"), out dtmp))
                        body.atmosphereAdiabaticIndex = dtmp;
                if (node.HasValue("atmosphereMolarMass"))
                    if (double.TryParse(node.GetValue("atmosphereMolarMass"), out dtmp))
                        body.atmosphereMolarMass = dtmp;
                if (node.HasValue("atmospherePressureSeaLevel"))
                    if (double.TryParse(node.GetValue("atmospherePressureSeaLevel"), out dtmp))
                        body.atmospherePressureSeaLevel = dtmp;
                if (node.HasValue("atmosphereTemperatureSeaLevel"))
                    if (double.TryParse(node.GetValue("atmosphereTemperatureSeaLevel"), out dtmp))
                        body.atmosphereTemperatureSeaLevel = dtmp;

                if (node.HasNode("atmospherePressureCurve"))
                {
                    body.atmosphereUsePressureCurve = true;
                    body.atmospherePressureCurve.Load(node.GetNode("atmospherePressureCurve"));
                }
                else if (node.HasValue("atmosphereUsePressureCurve"))
                {
                    if (bool.TryParse(node.GetValue("atmosphereUsePressureCurve"), out btmp))
                        body.atmosphereUsePressureCurve = btmp;
                }
                if (node.HasNode("atmosphereTemperatureCurve"))
                {
                    body.atmosphereUseTemperatureCurve = true;
                    body.atmosphereTemperatureCurve.Load(node.GetNode("atmosphereTemperatureCurve"));
                }
                else if (node.HasValue("atmosphereUseTemperatureCurve"))
                {
                    if (bool.TryParse(node.GetValue("atmosphereUseTemperatureCurve"), out btmp))
                        body.atmosphereUseTemperatureCurve = btmp;
                }
                if (node.HasNode("latitudeTemperatureBiasCurve"))
                {
                    body.latitudeTemperatureBiasCurve.Load(node.GetNode("latitudeTemperatureBiasCurve"));
                }
                if (node.HasNode("latitudeTemperatureSunMultCurve"))
                {
                    body.latitudeTemperatureSunMultCurve.Load(node.GetNode("latitudeTemperatureSunMultCurve"));
                }
                if (node.HasNode("axialTemperatureSunMultCurve"))
                {
                    body.axialTemperatureSunMultCurve.Load(node.GetNode("axialTemperatureSunMultCurve"));
                }
                if (node.HasNode("atmosphereTemperatureSunMultCurve"))
                {
                    body.atmosphereTemperatureSunMultCurve.Load(node.GetNode("atmosphereTemperatureSunMultCurve"));
                }
                if (node.HasNode("eccentricityTemperatureSunMultCurve"))
                {
                    body.eccentricityTemperatureSunMultCurve.Load(node.GetNode("eccentricityTemperatureSunMultCurve"));
                }

                if (node.HasValue("Radius"))
                {
                    if (double.TryParse(node.GetValue("Radius"), out dtmp))
                    {
                        body.Radius = dtmp;
                        updateMass = true;
                    }
                }

                // Orbit
                ConfigNode onode = node.GetNode("Orbit");
                if (body.orbitDriver != null && body.orbit != null && onode != null)
                {
                    patchOrbits = true;

                    if (node.HasValue("semiMajorAxis"))
                        if (double.TryParse(node.GetValue("semiMajorAxis"), out dtmp))
                            body.orbit.semiMajorAxis = dtmp;
                    if (node.HasValue("eccentricity"))
                        if (double.TryParse(node.GetValue("eccentricity"), out dtmp))
                            body.orbit.eccentricity = dtmp;
                    if (node.HasValue("meanAnomalyAtEpoch"))
                        if (double.TryParse(node.GetValue("meanAnomalyAtEpoch"), out dtmp))
                            body.orbit.meanAnomalyAtEpoch = dtmp;

                    if (node.HasValue("meanAnomalyAtEpochD"))
                    {
                        if (double.TryParse(node.GetValue("meanAnomalyAtEpochD"), out dtmp))
                        {
                            body.orbit.meanAnomalyAtEpoch = dtmp;
                            body.orbit.meanAnomalyAtEpoch *= DEG2RAD;
                        }
                    }
                    if (node.HasValue("inclination"))
                        if (double.TryParse(node.GetValue("inclination"), out dtmp))
                            body.orbit.inclination = dtmp;
                    if (node.HasValue("LAN"))
                        if (double.TryParse(node.GetValue("LAN"), out dtmp))
                            body.orbit.LAN = dtmp;
                    if (node.HasValue("argumentOfPeriapsis"))
                        if (double.TryParse(node.GetValue("argumentOfPeriapsis"), out dtmp))
                            body.orbit.argumentOfPeriapsis = dtmp;

                }
            }
            else if (globalRotationRescale != 1d || globalRescale != 1d)
                print("Patching body " + body.bodyName + " from globals.");

            body.rotationPeriod *= globalRotationRescale;

            if(globalRescale != 1d)
            {
                body.Radius *= globalRescale;
                updateMass = true;
            }
            if (updateMass)
            {
                patchOrbits = true;
                GeeASLToOthers(body);
            }
            if (globalRescaleAtmo != 1d && body.atmosphere)
            {
                body.atmospherePressureCurve = RescaleCurve(body.atmospherePressureCurve, globalRescaleAtmo);
                body.atmosphereTemperatureCurve = RescaleCurve(body.atmosphereTemperatureCurve, globalRescaleAtmo);
                body.atmosphereTemperatureSunMultCurve = RescaleCurve(body.atmosphereTemperatureSunMultCurve, globalRescaleAtmo);
                body.atmosphereDepth *= globalRescaleAtmo;
            }

            
            body.SetupConstants();
            // Fix up PQS
            if(PatchPQS(body, node, origRadius) || origRadius != body.Radius)
                StartCoroutine(PatchScaledSpace(body, node, origRadius));
            body.CBUpdate();
            return true;
        }
        static void FinalizeOrbits()
        {
            foreach (CelestialBody body in FlightGlobals.fetch.bodies)
            {
                if (body.orbitDriver != null)
                {
                    if (body.orbit != null)
                        body.orbit.semiMajorAxis *= globalOrbitRescale;

                    if (body.referenceBody != null)
                    {
                        body.hillSphere = body.orbit.semiMajorAxis * (1.0 - body.orbit.eccentricity) * Math.Pow(body.Mass / body.orbit.referenceBody.Mass, 1 / 3);
                        body.sphereOfInfluence = body.orbit.semiMajorAxis * Math.Pow(body.Mass / body.orbit.referenceBody.Mass, 0.4);
                        if (body.sphereOfInfluence < body.Radius * 1.5 || body.sphereOfInfluence < body.Radius + 20000.0)
                            body.sphereOfInfluence = Math.Max(body.Radius * 1.5, body.Radius + 20000.0); // sanity check
                        
                        // period should be (body.Mass + body.referenceBody.Mass) at the end, not just ref body, but KSP seems to ignore that bit so I will too.
                        body.orbit.period = 2 * Math.PI * Math.Sqrt(Math.Pow(body.orbit.semiMajorAxis, 2) / 6.674E-11 * body.orbit.semiMajorAxis / (body.referenceBody.Mass));

                        if (body.orbit.eccentricity <= 1.0)
                        {
                            body.orbit.meanAnomaly = body.orbit.meanAnomalyAtEpoch;
                            body.orbit.orbitPercent = body.orbit.meanAnomalyAtEpoch / (Math.PI * 2);
                            body.orbit.ObTAtEpoch = body.orbit.orbitPercent * body.orbit.period;
                        }
                        else
                        {
                            // ignores this body's own mass for this one...
                            body.orbit.meanAnomaly = body.orbit.meanAnomalyAtEpoch;
                            body.orbit.ObT = Math.Pow(Math.Pow(Math.Abs(body.orbit.semiMajorAxis), 3.0) / body.orbit.referenceBody.gravParameter, 0.5) * body.orbit.meanAnomaly;
                            body.orbit.ObTAtEpoch = body.orbit.ObT;
                        }
                    }
                    else
                    {
                        body.sphereOfInfluence = double.PositiveInfinity;
                        body.hillSphere = double.PositiveInfinity;
                    }
                }
                try
                {
                    body.CBUpdate();
                }
                catch (Exception e)
                {
                    print("CBUpdate for " + body.name + " failed: " + e.Message);
                }
            }
        }
        
        public void Start()
        {
            if (loadedData)
                return; // just in case
            
            patchOrbits = false;

            // Get this, we'll need it later
            Type afgType = typeof(AtmosphereFromGround);
            if(afgType != null)
                afgSetMaterial = afgType.GetMethod("SetMaterial", BindingFlags.NonPublic | BindingFlags.Instance);

            // Get the data node
            ConfigNode bodyData = null;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("CELESTIALBODIES"))
                bodyData = node;

            // Patch bodies
            if (bodyData != null)
            {
                // load globals
                double dtmp;
                if (bodyData.HasValue("globalOrbitRescale"))
                    if (double.TryParse(bodyData.GetValue("globalOrbitRescale"), out dtmp))
                        globalOrbitRescale = dtmp;
                if (bodyData.HasValue("globalRotationRescale"))
                    if (double.TryParse(bodyData.GetValue("globalRotationRescale"), out dtmp))
                        globalRotationRescale = dtmp;   
                if (bodyData.HasValue("globalRescale"))
                    if (double.TryParse(bodyData.GetValue("globalRescale"), out dtmp))
                        globalRescale = dtmp;
                if (bodyData.HasValue("globalRescaleAtmo"))
                    if (double.TryParse(bodyData.GetValue("globalRescaleAtmo"), out dtmp))
                        globalRescaleAtmo = dtmp;

                // patch bodies
                foreach (CelestialBody body in FlightGlobals.Bodies)
                    if (!PatchBody(body, GetNode(body.bodyName, bodyData)))
                        print("**Failed to patch " + body.bodyName);
            }
            if (patchOrbits || globalOrbitRescale != 1d)
                FinalizeOrbits();

            DontDestroyOnLoad(this);

        }
    }
}
