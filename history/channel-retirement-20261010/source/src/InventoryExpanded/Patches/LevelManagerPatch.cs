using Backpack.Config;
using HarmonyLib;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Levelling;
#elif MONO
using ScheduleOne.Levelling;
#endif

namespace Backpack.Patches;

public static class LevelManagerPatch
{
}
