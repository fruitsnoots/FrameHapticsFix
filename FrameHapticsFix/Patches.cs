using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FrameHapticsFix
{
    internal class Flag
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

            return true;
        }
    }

    [HarmonyPatch(typeof(UnityXRController), "UpdateHapticsHandler")]
    static class UpdateHandlerPatch
    {
        public static bool Prefix(UnityXRController __instance)
        {
            if (!Flag.isProbablySteamFrame)
            {
                return true;
            }
            
            // The knuckles haptics code tries to break the haptic signals into 0.0125 second chunks,
            // which seem to cause the frame controllers to trip up. Switch to default handler because
            // that'll fire a leading full-duration impulse (and not double all the preset durations...)
            if (!(__instance._hapticsHandler is DefaultUnityXRHapticsHandler))
            {
                Plugin.Log.Info("Creating DefaultUnityXRHapticsHandler");
                __instance._hapticsHandler?.Dispose();
                __instance._hapticsHandler = new DefaultUnityXRHapticsHandler(__instance.node);
            }

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
                && Math.Abs(data._endTime - endTime) < 0.01f)
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