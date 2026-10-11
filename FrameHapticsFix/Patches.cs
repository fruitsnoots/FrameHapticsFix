using System;
using System.Collections.Generic;
using HarmonyLib;
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
        // The endTime in the _rumbleData reference may be updated from under us, so track our own stop time values.
        // _stopTime: Will be set to 1/60th of a second in the future on every update. If we stop getting updates,
        //            we will stop the haptics directly.
        public float _stopTime = stopTime;
        // _autoStopTime: Will be set to 0.2 seconds in the future at the time we issue the haptic pulse (which
        //                will also last 0.2 seconds). If we're still getting updated when this expires, we'll
        //                set off a new pulse.
        public float _autoStopTime = autoStopTime;
    }

    [HarmonyPatch(typeof(RumbleHapticFeedbackPlayer), "UpdateRumbles")]
    static class UpdateRumblesPatch
    {
        public static bool Prefix(RumbleHapticFeedbackPlayer __instance)
        {
            // The _rumblesByNode dictionary mapping is (XRNode, (preset, RumbleData)).
            //
            // Original implementation frequently blasts _vrPlatformHelper.TriggerHapticPulse with new
            // short pulses as long as continuous (i.e. saber clash, wall clash) items are active. This seems
            // to happen every frame? In either case, simply rejecting new calls sometimes as being "the same"
            // can mean we still keep getting blasted too much.
            //
            // The goal here is to just reduce the number of calls to TriggerHapticPulse as much as
            // possible so the controller doesn't panic I guess. A big chunk of that is actively reducing
            // the frequency with which we send new pulses for continuous events, which means overwriting
            // the actual duration with a longer value (I'm selecting 0.25 seconds), and explicitly ending
            // the pulse when it's expired on its real resolution.

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
                    Plugin.Log.Info("Stopping expired haptics");
                    __instance._vrPlatformHelper.StopHaptics(key);
                    RunningRumbleData.RunningRumbles.Remove(key);
                }
                
                if (selected == null)
                {
                    continue;
                }

                // New discrete impulse always takes priority, regardless of what was previously running.
                if (!selected.continuous)
                {
                    Plugin.Log.Info("New discrete haptics");
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
                    Plugin.Log.Info("New continuous haptics with nothing to override");
                    __instance._vrPlatformHelper.TriggerHapticPulse(
                        key, 0.2f, selected.strength, selected.frequency);
                    RunningRumbleData.RunningRumbles[key] = new RunningRumbleData(
                        selected, Time.time + 1f / 60f, Time.time + 0.2f);
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
                    oldData._stopTime = Time.time + (1f / 60f);
                    
                    // Re-fire a little early to avoid spin-down?
                    if ((oldData._autoStopTime-0.1f) < Time.time)
                    {
                        Plugin.Log.Info("Re-firing existing continuous haptics");
                        __instance._vrPlatformHelper.TriggerHapticPulse(
                            key, 0.2f, oldData._rumbleData.strength, oldData._rumbleData.frequency);
                        oldData._autoStopTime = Time.time + 0.2f;
                    }

                    continue;
                }
                
                // New impulse only beats old one for greater strength
                if (selected.strength > oldData._rumbleData.strength)
                {
                    Plugin.Log.Info("New continuous haptics win on strength");
                    __instance._vrPlatformHelper.TriggerHapticPulse(
                        key, 0.2f, selected.strength, selected.frequency);
                    RunningRumbleData.RunningRumbles[key] = new RunningRumbleData(
                        selected, Time.time + 1f / 60f, Time.time + 0.2f);
                    continue;
                }
            }

            return false;
        }
    }
    
    // Normally the haptics handler passes everything through a coroutine. Don't do that.
    [HarmonyPatch(typeof(KnucklesUnityXRHapticsHandler), "TriggerHapticPulse")]
    static class TriggerPulsePatch
    {
        public static bool Prefix(KnucklesUnityXRHapticsHandler __instance, float strength, float duration)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(__instance._node);
            // I feel like I could just multiply duration by 2 here and have this not break knuckles controllers,
            // and then HapticsTweaks/Tweaks55 could just function mostly normally. But then (a) I'd have to test
            // that cause something could break, and (b) folks swapping between the controllers types would still
            // need to make a mod adjustment every time they do.
            device.SendHapticImpulse(0, strength, duration);
            return false;
        }
    }
    
    /*[HarmonyPatch(typeof(RumbleHapticFeedbackPlayer), "PlayHapticFeedback")]
    static class LogHapticFeedback
    {
        public static bool Prefix(RumbleHapticFeedbackPlayer __instance, XRNode node, HapticPresetSO hapticPreset)
        {
            Plugin.Log.Info($"Playing Preset {hapticPreset.name}");
            return true;
        }
    }

    [HarmonyPatch(typeof(HapticFeedbackManager), "PlayHapticFeedback")]
    static class LogPlayHapticFeedback
    {
        public static bool Prefix(HapticFeedbackManager __instance, XRNode node, HapticPresetSO hapticPreset)
        {
            Plugin.Log.Info(
                $"Playing preset {hapticPreset.name}, {__instance._advancedHapticFeedbackPlayer.CanPlayHapticPreset(hapticPreset, node)}");
            return true;
        }
    }*/

    // This function gets called a lot, and I guess is why they used a coroutine to try and break up the calls,
    // but the coroutine runs at inconsistent times so I couldn't reliably reject repeats.
    /*[HarmonyPatch(typeof(KnucklesUnityXRHapticsHandler), "TriggerHapticPulse")]
    static class TriggerPulsePatch
    {
        public static bool Prefix(KnucklesUnityXRHapticsHandler __instance, float strength, float duration)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(__instance._node);
            // I feel like I could just multiply duration by 2 here and have this not break knuckles controllers,
            // and then HapticsTweaks/Tweaks55 could just function mostly normally. But then (a) I'd have to test
            // that cause something could break, and (b) folks swapping between the controllers types would still
            // need to make a mod adjustment every time they do.
            device.SendHapticImpulse(0, strength, duration);
            return false;
        }
    }

    internal class ImpulseData
    {
        public static Dictionary<object, ImpulseData> Lookup = new Dictionary<object, ImpulseData>();

        public float _endTime;
        public uint _channel;
        public float _amplitude;

        public ImpulseData(uint channel, float amplitude, float endTime)
        {
            _endTime = endTime;
            _channel = channel;
            _amplitude = amplitude;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.XR.InputDevice), "SendHapticImpulse")]
    static class HapticImpulsePatch
    {
        public static bool Prefix(UnityEngine.XR.InputDevice __instance, uint channel, float amplitude, float duration)
        {
            float endTime = Time.time + duration;

            if (!(ImpulseData.Lookup.ContainsKey(__instance)))
            {
                ImpulseData.Lookup.Add(__instance, new ImpulseData(channel, amplitude, duration));
                //Plugin.Log.Debug("Created impulse data");
                return true;
            }

            ImpulseData data = ImpulseData.Lookup[__instance];

            // Frame controllers don't like being told to vibrate too often apparently, so reject extra attempts
            // to keep impulsing
            if (data._channel == channel
                && Math.Abs(data._amplitude - amplitude) < 0.01f
                && Math.Abs(data._endTime - endTime) < 0.001f)
            {
                // This is functionally the same impulse
                //Plugin.Log.Debug("Rejected impulse data");
                return false;
            }

            ImpulseData newData = new ImpulseData(channel, amplitude, endTime);
            ImpulseData.Lookup[__instance] = newData;
            //Plugin.Log.Debug("Accepted new impulse");
            return true;
        }
    }*/
}