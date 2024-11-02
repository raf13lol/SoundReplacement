using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Networking;
using UnityEngine;

namespace SoundReplacement;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Rhythm Doctor.exe")]
// i don't know what
#pragma warning disable BepInEx002 // Classes with BepInPlugin attribute must inherit from BaseUnityPlugin
public class SoundReplacement : BaseUnityPlugin
#pragma warning restore BepInEx002 // Classes with BepInPlugin attribute must inherit from BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static SoundReplacement inst;

    private ConfigEntry<bool> soundReplacement;

    private void Awake()
    {
        inst = this;
        // Plugin startup logic
        Logger = base.Logger;
        soundReplacement = Config.Bind("General", "ReplaceSounds", true, "Whether to replace sounds or not with user-defined sounds.");
        Logger.LogInfo($"Sound Replacement plugin loaded! Will {(soundReplacement.Value ? "" : "not ")}replace sounds!");

        Harmony instance = new Harmony("patcher");
        instance.PatchAll(typeof(AwakePatch)); 
        instance.PatchAll(typeof(FlushData));
        instance.PatchAll(typeof(FindOrLoadAudioClip));
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Awake))]
    private static class AwakePatch
    {
        public static void Postfix(AudioManager __instance)
        {
            inst.ScanFiles(__instance);
        }
    }
    
    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.FlushData))]
    private static class FlushData
    {
        public static void Postfix(AudioManager __instance)
        {
            inst.ScanFiles(__instance);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.FindOrLoadAudioClip))]
    private static class FindOrLoadAudioClip
    {
        public static bool Prefix(AudioManager __instance, ref AudioClip __result, string clipName)
        {
            string text = Path.GetFileName(clipName);

            if (RDBase.IsAprilFoolsDay() && Persistence.GetLevelClearedByAnySlot(Level.OrientalInsomniac))
            {
                if (text == "sndPagerCursorMove")
                    text = "sndDoctahWeh";
                if (text == "sndPagerButton" || text == "sndMenuSelect" || text == "sndMenuSelectBoss")
                    text = "sndDoctahHehehe";
            }

            if (inst.soundReplacement.Value && inst.replacedSongs.IndexOf(text) >= 0)
            {
                List<string> songs = inst.replacedSongsDir[text];
                int index = UnityEngine.Random.Range(0, songs.Count);
                string thing = songs[index] + "*external";

                Logger.LogInfo($"Replaced {text} with {thing}!");
                __result = __instance.audioLib[thing];
                return false;
            }
            return true;
        }
    }

    public void ScanFiles(AudioManager am, string path = "")
    {
        bool start = path != "";
        if (start)
        {
            if (!Directory.Exists(path))
                return;

            foreach (string text in Directory.GetFileSystemEntries(path))
            {
                if (text.Replace(path, "").StartsWith("!!"))
                    continue;

                if (Directory.Exists(text))
                {
                    ScanFiles(am, text + "\\");
                    continue;
                }
                if (!text.EndsWith(".ogg") && !text.EndsWith(".wav"))
                    continue;
                
                string text2 = text.Replace(path, "").Replace(".ogg", "").Replace(".wav", "");

                if (text2.IndexOf(".") >= 0)
                    text2 = text2.Substring(text2.IndexOf(".") + 1);
                if (!dsls.ContainsKey(text2))
                    dsls.Add(text2, []);

                dsls[text2].Add(text);
            }
            return;
        }
        replacedSongs.Clear();
        replacedSongsDir = [];
        dsls = [];
        ScanFiles(am, AppDomain.CurrentDomain.BaseDirectory + "\\replaced\\");
        foreach (KeyValuePair<string, List<string>> keyValuePair in dsls)
        {
            int num = 0;
            List<string> files = [];
            string log = "";
            foreach (string text3 in keyValuePair.Value)
            {
                if (num > 0)
                    log += ", ";
                log += text3;

                StartCoroutine(FindOrLoadAudioClipExternalPath(am, text3));

                files.Add(text3);
                num++;
            }
            Logger.LogInfo($"Added sound replacements for {keyValuePair.Key}: [{log}]!");
            replacedSongs.Add(keyValuePair.Key);
            replacedSongsDir.Add(keyValuePair.Key, files);
        }
    }

    // yeah this is not different much just simplified and uses path
    public IEnumerator FindOrLoadAudioClipExternalPath(AudioManager am, string path)
    {
        string extension = Path.GetExtension(path);
        if (am.audioLib.ContainsKey(path + "*external"))
        {
            yield return new RDAudioLoadResult(RDAudioLoadType.SuccessExternalClipLoaded, am.audioLib[path + "*external"]);
        }
        else
        {
            bool loadSuccess = false;
            AudioClip clip = null;
            AudioType audioType = RDUtils.GetAudioType(extension);
            if (audioType == AudioType.OGGVORBIS || audioType == AudioType.WAV)
            {
                string text = new Uri(path).AbsoluteUri;
                text = text.Replace("+", "%2B");
                UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(text, audioType);
                yield return uwr.SendWebRequest();
                DownloadHandlerAudioClip downloadHandlerAudioClip = uwr.downloadHandler as DownloadHandlerAudioClip;
                clip = downloadHandlerAudioClip.audioClip;
                if (clip == null)
                {
                    yield return new RDAudioLoadResult(RDAudioLoadType.ErrorFileNotFound, null);
                }
                else if (clip.length == 0f)
                {
                    if (audioType == AudioType.OGGVORBIS)
                    {
                        yield return new RDAudioLoadResult(RDAudioLoadType.ErrorLoadingOggVorbis, null);
                    }
                    else if (audioType == AudioType.WAV)
                    {
                        yield return new RDAudioLoadResult(RDAudioLoadType.ErrorLoadingWAV, null);
                    }
                }
                else
                {
                    loadSuccess = true;
                }
            }
            if (loadSuccess)
            {
                string conductorName = path + "*external";
                clip.name = conductorName;
                yield return new RDAudioLoadResult(RDAudioLoadType.SuccessExternalClipLoaded, clip);
                if (!am.audioLib.ContainsKey(conductorName))
                {
                    am.audioLib.Add(conductorName, clip);
                }
            }
        }
        yield break;
    }

    public List<string> replacedSongs = [];
	public Dictionary<string, List<string>> replacedSongsDir;
	public Dictionary<string, List<string>> dsls;
}