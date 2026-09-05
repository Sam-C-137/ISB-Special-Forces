using Audio.SpatialSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.Weather;
using EFT.GameTriggers;
using MultiFlare;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using EFT.Communications;
using SPT.Reflection.Patching;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;

namespace ISBSSOFExtra
{
	public static class IcebreakerBlackout
	{
		public static IEnumerator DisableFlares(ManualLogSource logger)
		{
            var flaresList = new List<FlareLight>(240);
			var volumetrics = new List<VolumetricLight>(20);

            var sceneName = "Icebreaker_Lights";
            var scene = SceneManager.GetSceneByName(sceneName);
            foreach (var sceneRootGO in scene.GetRootGameObjects())
            {
                var sceneRootName = sceneRootGO.name;
				if (sceneRootName == "SBG_Icebreaker_Lights")
				{
					// we are locating "Icebreaker_Lights/SBG_Icebreaker_Lights/OO/LIGHT"
					var lightRoots = sceneRootGO.transform.GetChild(0).GetChild(0);
                    var childrenCount = lightRoots.childCount;
                    for (var i = 0; i < childrenCount; i++)
					{
                        var child = lightRoots.GetChild(i);

                        child.GetComponentsInChildren<FlareLight>(true, flaresList);
                        foreach (var flare in flaresList)
						{
							// same as FlareLight.OnDisable but ignores flare.bool_0 flag
							// which prevents flare from being disabled
							GClass1023.Instance.AccessLightData(flare).Enabled = false;
						}
                        flaresList.Clear();

                        child.GetComponentsInChildren<VolumetricLight>(true, volumetrics);
                        foreach (var volumetric in volumetrics)
						{
							volumetric.enabled = false;
						}
                        volumetrics.Clear();

						// big light shafts are in "Icebreaker_Lights/SBG_Icebreaker_Lights/OO/LIGHT/SUPERSTRUCTURE_FRONT/EFEECTS/Lightplanes"
						// technically only 6 of them are visible, but its easier to just disable Lightplanes root object
						if (child.name == "SUPERSTRUCTURE_FRONT")
						{
							var lightplanes = child.Find("EFEECTS/Lightplanes");
							if (lightplanes)
							{
								lightplanes.gameObject.SetActive(false);
							}
						}
			            yield return null;
					}
					break;
				}
			}
		}
	}
}
