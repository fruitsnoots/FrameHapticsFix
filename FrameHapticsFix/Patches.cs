using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using IPA.Utilities;
using Libraries.HM.HMLib.VR;
using UnityEngine;
using UnityEngine.XR;

namespace FrameHapticsFix
{
    /*
     * The commented stuff below would be where I'd try to differentiate knuckles and frame controllers,
     * but the frame controllers apparently report being index, so I'm just gonna leave that there
     * in case I can figure it out later
     */
    /*internal class Flag
    {
        public static bool isProbablySteamFrame = false;
    }
    
    [HarmonyPatch(typeof(UnityXRController),"TryToUpdateManufacturerName")]
    static class ManufacturerNamePatch
    {
        public static bool Prefix(UnityXRController __instance, UnityEngine.XR.InputDevice device)
        {
            if (string.IsNullOrEmpty(device.manufacturer))
            {
                Plugin.Log.Info("No manufacturer name found for controller");
                return true;
            }

            string manufacturer = device.manufacturer.ToLowerInvariant();
            if (!manufacturer.Contains("valve"))
            {
                Plugin.Log.Info($"Controller manufactured by: '{device.manufacturer}'");
                return true;
            }

            if (string.IsNullOrEmpty(device.name))
            {
                Plugin.Log.Info("No device name found for controller");
                return true;
            }
            
            // Can't actually use this to identify the controller because the frame controllers report being index
            // controllers
            string name = device.name.ToLowerInvariant();
            Plugin.Log.Info($"Found controller: {name}");

            // Should be a config, whatever.
            Flag.isProbablySteamFrame = true;

            // Still need to let it identify the controller as a valve one (and use the knuckles haptics handler
            // class), or it'll throw controller settings defaults off
            return true;
        }
    }*/

    internal class RunningRumbleData(RumbleHapticFeedbackPlayer.RumbleData rumbleData, float stopTime, float autoStopTime)
    {
        public static Dictionary<XRNode, RunningRumbleData> RunningRumbles = 
            new Dictionary<XRNode, RunningRumbleData>();

        public RumbleHapticFeedbackPlayer.RumbleData _rumbleData = rumbleData;
        // The following fields are only relevant to continuous haptics. Basically, we want to be able to stop haptics
        // as fast as every 1/60th of a second, but we don't need to give the controller handler new durations that
        // often (we use 0.2 second chunks)
        
        // The endTime in the _rumbleData reference may be updated from under us, so track our own stop time values.
        // _stopTime: Will be set to 1/60th of a second in the future on every update. If we stop getting updates,
        //            we will stop the haptics directly.
        public float _stopTime = stopTime;
        // _autoStopTime: Can be set to e.g. 0.2 seconds in the future at the time we issue the haptic pulse (which
        //                would also last 0.2 seconds). If we're still getting updated when this expires, we'll
        //                set off a new pulse.
        public float _autoStopTime = autoStopTime;
    }

    [HarmonyPatch(typeof(RumbleHapticFeedbackPlayer), "UpdateRumbles")]
    static class UpdateRumblesPatch
    {
        public const float StopTime = (1f / 60f);
        public const float AutoStopTime = 0.2f;
        
        public static bool Prefix(RumbleHapticFeedbackPlayer __instance)
        {
            // The original implementation of UpdateRumbles regularly blasts the haptics handlers with new
            // information. For discrete events (e.g. note cut), it'll update the handlers regularly with
            // remaining duration, and continuous events (e.g. wall clash) keep updating it with new durations
            // refreshed to 1/60th of a second. This seems to happen every frame?
            //
            // To clean up the inputs to the haptics handlers, this rewrite ensures that each discrete event
            // only produces *one* TriggerHapticPulse call. We can also reduce the frequency at which
            // the haptics handlers get new information about continuous events (i.e. only giving them
            // new duration info every 0.1-0.2 seconds), and just explicitly cancel the haptics when those are
            // finished.

            // The _rumblesByNode dictionary mapping is (XRNode, (preset, RumbleData)).
            foreach (KeyValuePair<XRNode, Dictionary<object, RumbleHapticFeedbackPlayer.RumbleData>> item in __instance
                         ._rumblesByNode)
            {
                XRNode key = item.Key;

                // Look through all the (new) active rumbles for this node and select the highest-priority one.
                //
                // (The active flag gets set once on trigger for non-continuous rumbles, but repeatedly
                // for continuous ones.)
                RumbleHapticFeedbackPlayer.RumbleData? selected = null;
                foreach (RumbleHapticFeedbackPlayer.RumbleData value in item.Value.Values)
                {
                    if (!value.active) continue;

                    // Mark all items as not active so sequential invocations don't cause extra re-issue of haptics
                    // (i.e. if two different triggers happen on the same update we should just pick one and discard
                    // the other)
                    value.active = false;

                    if (selected == null)
                    {
                        selected = value;
                        continue;
                    }

                    // Non-continuous impulses take priority over continuous impulses, even if they're weaker,
                    // to allow discrete events to still feel like they register.
                    if (!value.continuous && selected.continuous)
                    {
                        selected = value;
                        continue;
                    }

                    // After that, strongest new pulse wins. If the strengths match, we'll pick the longest.
                    if (Math.Abs(value.strength - selected.strength) < 0.001f)
                    {
                        // Because continuous haptics are going to have their endTimes updated regularly, we're
                        // likely to end up ping-ponging in the case of two simultaneous, continuous haptics effects
                        // that have the same strength. However, I don't think I can rely on this loop presenting
                        // each RumbleData in the same order every time, so we just need to handle that later.
                        if (value.endTime > selected.endTime)
                        {
                            selected = value;
                        }
                    }

                    else if (value.strength > selected.strength)
                    {
                        selected = value;
                    }
                }

                // Now that we (may) have selected the best new option, we can do checks with existing effects.
                RunningRumbleData.RunningRumbles.TryGetValue(key, out var oldData);

                // Stop old haptics if expired
                if (oldData != null && oldData._stopTime < Time.time)
                {
                    // Because the coroutine that actually sends the impulses is asynchronous (with a potentially
                    // delayed start), I don't want to send an explicit stop if the haptics event was discrete, as
                    // doing so could truncate the actual impulse time, which might cause noticeable variations
                    // for shorter events.
                    if (oldData._rumbleData.continuous)
                    {
                        //Plugin.Log.Info("Stopping expired haptics");
                        __instance._vrPlatformHelper.StopHaptics(key);
                    }

                    RunningRumbleData.RunningRumbles.Remove(key);
                }
                
                if (selected == null)
                {
                    continue;
                }

                // New discrete impulse always takes priority, regardless of what was previously running.
                if (!selected.continuous)
                {
                    //Plugin.Log.Info("New discrete haptics");
                    __instance._vrPlatformHelper.TriggerHapticPulse(
                        key, selected.endTime - Time.time, selected.strength, selected.frequency);
                    RunningRumbleData.RunningRumbles[key] = new RunningRumbleData(
                        selected, selected.endTime, selected.endTime);
                    continue;
                }
                
                // New impulse must be continuous.
                
                // If there wasn't anything running before, then there's nothing we need to compare.
                if (oldData == null)
                {
                    //Plugin.Log.Info("New continuous haptics with nothing to override");
                    __instance._vrPlatformHelper.TriggerHapticPulse(
                        key, AutoStopTime, selected.strength, selected.frequency);
                    RunningRumbleData.RunningRumbles[key] = new RunningRumbleData(
                        selected, Time.time + StopTime, Time.time + AutoStopTime);
                    continue;
                }
                
                // New continuous impulse generally loses to still-playing discrete impulse. We do need to re-fire
                // early to avoid spin-down, though, and admittedly in practice the error bars I'm using span
                // an entire discrete preset.
                if (!oldData._rumbleData.continuous && (oldData._autoStopTime-0.1f) > Time.time)
                {
                    continue;
                }
                
                // New and old impulses must both be continuous.
                
                // If new and old are the same strength, they're functionally the same preset - treat
                // it as an update to the old data.
                if (Math.Abs(selected.strength - oldData._rumbleData.strength) < 0.001f)
                {
                    oldData._stopTime = Time.time + StopTime;
                    
                    // Re-fire a little early to avoid spin-down?
                    if ((oldData._autoStopTime-0.1f) < Time.time)
                    {
                        //Plugin.Log.Info("Re-firing existing continuous haptics");
                        __instance._vrPlatformHelper.TriggerHapticPulse(
                            key, AutoStopTime, oldData._rumbleData.strength, oldData._rumbleData.frequency);
                        oldData._autoStopTime = Time.time + AutoStopTime;
                    }

                    continue;
                }
                
                // New impulse only beats old one for greater strength
                if (selected.strength > oldData._rumbleData.strength)
                {
                    //Plugin.Log.Info("New continuous haptics win on strength");
                    __instance._vrPlatformHelper.TriggerHapticPulse(
                        key, AutoStopTime, selected.strength, selected.frequency);
                    RunningRumbleData.RunningRumbles[key] = new RunningRumbleData(
                        selected, Time.time + StopTime, Time.time + AutoStopTime);
                    continue;
                }
            }

            return false;
        }
    }
    
    /*internal class TimerThing
    {
        public static Dictionary<KnucklesUnityXRHapticsHandler, float> Blah =
            new Dictionary<KnucklesUnityXRHapticsHandler, float>();
    }*/
    
    // Letting everything pass through the coroutine to use the extra layer of protection against too many pulses.
    [HarmonyPatch(typeof(KnucklesUnityXRHapticsHandler), "TriggerHapticPulse")]
    static class TriggerPulsePatch
    {
        public static bool Prefix(KnucklesUnityXRHapticsHandler __instance, float strength, float duration)
        {
            // Don't double the duration though, these controllers don't need it.
            __instance._remainingTime = duration;
            __instance._amplitude = Mathf.Clamp01(strength);
            //TimerThing.Blah[__instance] = Time.time;
            return false;
        }
    }

    [HarmonyPatch(typeof(KnucklesUnityXRHapticsHandler), "StopHaptics")]
    static class StopHapticsPatch
    {
        public static bool Prefix(KnucklesUnityXRHapticsHandler __instance)
        {
            // This method of stopping haptics immediately means that there's no actual minimum amount of time that
            // a saber/wall clash haptic effect will stay in effect. Personally, I kind of like that it gives a
            // whooshy vibe when passing sabers through each other. I also felt obligated to bust out the quest 3
            // to compare the behavior and this is actually about consistent with how that feels.
            __instance._remainingTime = 0;
            InputDevices.GetDeviceAtXRNode(__instance._node).StopHaptics();
            return false;
        }
    }

    [HarmonyPatch(typeof(KnucklesUnityXRHapticsHandler), MethodType.Constructor,
        new Type[] { typeof(XRNode), typeof(MonoBehaviour) })]
    static class KnucklesHandlerConstructorPatch
    {
        public static bool Prefix(KnucklesUnityXRHapticsHandler __instance, XRNode node, MonoBehaviour coroutineRunner)
        {
            __instance.SetField("_node", node);
            __instance.SetField("_coroutineRunner", coroutineRunner);
            __instance._amplitude = 0f;
            __instance._remainingTime = 0f;
            __instance.SetField("_hapticsCoroutine",
                coroutineRunner.StartCoroutine(PatchedHapticsCoroutine(__instance)));
            return false;
        }

        private static IEnumerator PatchedHapticsCoroutine(KnucklesUnityXRHapticsHandler __instance)
        {
            XRNode node = __instance._node;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            // A minimum of 0.0125 seconds must pass between impulses, but to keep things a little more responsive
            // we can use a separate waiter to check for when we've actually received impulse info
            WaitForSecondsRealtime timeWaiter = new WaitForSecondsRealtime(0.0125f);
            WaitUntil condWaiter = new WaitUntil(() => (device.isValid && __instance._remainingTime > 0f));
            while (true)
            {
                if (!(device.isValid && __instance._remainingTime > 0f))
                    yield return condWaiter;
                
                //Plugin.Log.Info($"Time diff {Time.time - TimerThing.Blah[__instance]}");
                device.SendHapticImpulse(0u, __instance._amplitude, __instance._remainingTime);
                
                // Consuming the entire remaining time in one single impulse prevents weird behavior for very small
                // time values (think chain links configured to be like 0.02 seconds long)
                __instance._remainingTime = 0f;
                yield return timeWaiter;
            }
        }
    }
}