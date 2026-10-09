using System;
using System.Collections.Generic;
using HarmonyLib;
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

    // This function gets called a lot, and I guess is why they used a coroutine to try and break up the calls,
    // but the coroutine runs at inconsistent times so I couldn't reliably reject repeats.
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
    }
}