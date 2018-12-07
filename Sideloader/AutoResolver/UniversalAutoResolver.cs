﻿using BepInEx;
using BepInEx.Logging;
using Harmony;
using ResourceRedirector;
using Studio;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sideloader.AutoResolver
{
    public static class UniversalAutoResolver
    {
        public const string UARExtID = "com.bepis.sideloader.universalautoresolver";

        public static List<ResolveInfo> LoadedResolutionInfo = new List<ResolveInfo>();
        public static List<StudioResolveInfo> LoadedStudioResolutionInfo = new List<StudioResolveInfo>();

        public static void ResolveStructure(Dictionary<CategoryProperty, StructValue<int>> propertyDict, object structure, IEnumerable<ResolveInfo> extInfo, string propertyPrefix = "")
        {
            void CompatibilityResolve(KeyValuePair<CategoryProperty, StructValue<int>> kv)
            {
                //check if it's a vanilla item
                if (!ListLoader.InternalDataList[kv.Key.Category].ContainsKey(kv.Value.GetMethod(structure)))
                {
                    //the property does not have external slot information
                    //check if we have a corrosponding item for backwards compatbility
                    var intResolve = LoadedResolutionInfo.FirstOrDefault(x => x.Property == kv.Key.ToString()
                                                                           && x.Slot == kv.Value.GetMethod(structure)
                                                                           && x.CategoryNo == kv.Key.Category);

                    if (intResolve != null)
                    {
                        //found a match
                        Logger.Log(LogLevel.Debug, $"[UAR] Compatibility resolving {intResolve.Property} from slot {kv.Value.GetMethod(structure)} to slot {intResolve.LocalSlot}");

                        kv.Value.SetMethod(structure, intResolve.LocalSlot);
                    }
                    else
                    {
                        //No match was found
                        Logger.Log(LogLevel.Debug, $"[UAR] Compatibility resolving failed, no match found for ID {kv.Value.GetMethod(structure)} Category {kv.Key.Category}");
                    }
                }
            }

            foreach (var kv in propertyDict)
            {
                string property = $"{propertyPrefix}{kv.Key}";

                //For accessories, make sure we're checking the appropriate category
                if (kv.Key.Category.ToString().Contains("ao_"))
                {
                    ChaFileAccessory.PartsInfo AccessoryInfo = (ChaFileAccessory.PartsInfo)structure;

                    if ((int)kv.Key.Category != AccessoryInfo.type)
                    {
                        //If the current category does not match the category saved to the card do not attempt resolving
                        continue;
                    }
                }

                if (extInfo != null)
                {
                    var extResolve = extInfo.FirstOrDefault(x => x.Property == property);

                    if (extResolve != null)
                    {
                        //the property has external slot information 
                        var intResolve = LoadedResolutionInfo.FirstOrDefault(x => x.Property == kv.Key.ToString()
                                                                               && x.Slot == extResolve.Slot
                                                                               && x.GUID == extResolve.GUID
                                                                               && x.CategoryNo == kv.Key.Category);

                        if (intResolve != null)
                        {
                            //found a match to a corrosponding internal mod
                            Logger.Log(LogLevel.Debug, $"[UAR] Resolving {extResolve.GUID}:{extResolve.Property} from slot {extResolve.Slot} to slot {intResolve.LocalSlot}");
                            kv.Value.SetMethod(structure, intResolve.LocalSlot);
                        }
                        else
                        {
                            ShowGUIDError(extResolve.GUID);
                            kv.Value.SetMethod(structure, 999999); //set to an invalid ID
                        }
                    }
                    else
                    {
                        CompatibilityResolve(kv);
                    }
                }
                else
                {
                    CompatibilityResolve(kv);
                }
            }
        }

        private static int CurrentSlotID = 100000000;
        public static void GenerateResolutionInfo(Manifest manifest, ChaListData data)
        {
            var category = (ChaListDefine.CategoryNo)data.categoryNo;

            var propertyKeys = StructReference.CollatedStructValues.Keys.Where(x => x.Category == category).ToList();

            //Logger.Log(LogLevel.Debug, StructReference.CollatedStructValues.Count.ToString());

            foreach (var kv in data.dictList)
            {
                int newSlot = Interlocked.Increment(ref CurrentSlotID);

                foreach (var propertyKey in propertyKeys)
                {
                    LoadedResolutionInfo.Add(new ResolveInfo
                    {
                        GUID = manifest.GUID,
                        Slot = int.Parse(kv.Value[0]),
                        LocalSlot = newSlot,
                        Property = propertyKey.ToString(),
                        CategoryNo = category
                    });

                    //Logger.Log(LogLevel.Info, $"ResolveInfo - " +
                    //                          $"GUID: {manifest.GUID} " +
                    //                          $"Slot: {int.Parse(kv.Value[0])} " +
                    //                          $"LocalSlot: {newSlot} " +
                    //                          $"Property: {propertyKey.ToString()} " +
                    //                          $"CategoryNo: {category} " +
                    //                          $"Count: {LoadedResolutionInfo.Count}");
                }

                kv.Value[0] = newSlot.ToString();
            }
        }

        private static int CurrentStudioSlotID = 100000000;
        public static void GenerateStudioResolutionInfo(Manifest manifest, ListLoader.StudioListData data)
        {
            string StudioListType;
            if (data.FileNameWithoutExtension.Contains('_'))
                StudioListType = data.FileNameWithoutExtension.Split('_')[0].ToLower();
            else
                return; //Not a studio list

            if (StudioListType == "itembonelist")
            {
                foreach (List<string> entry in data.Entries)
                {
                    int slot = int.Parse(entry[0]);
                    int newSlot;

                    //See if the item this bone info cooresponds to has been resolved and set the ID to the same resolved ID
                    var item = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.GUID == manifest.GUID && x.Slot == slot);
                    newSlot = item == null ? slot : item.LocalSlot;

                    LoadedStudioResolutionInfo.Add(new StudioResolveInfo
                    {
                        GUID = manifest.GUID,
                        Slot = slot,
                        LocalSlot = newSlot,
                        ResolveItem = false
                    });

                    entry[0] = newSlot.ToString();
                }
            }
            else if (CategoryAndGroupList.Contains(StudioListType))
            {
                foreach (List<string> entry in data.Entries)
                {
                    //Add it to the resolution info as is, studio will automatically merge groups with the same IDs without causing exceptions.
                    //The IDs are expected to stay the same anyway as ItemLists will contain a reference to them.
                    //Because of this, all ID lookups should check if the thing is a ResolveItem.
                    LoadedStudioResolutionInfo.Add(new StudioResolveInfo
                    {
                        GUID = manifest.GUID,
                        Slot = int.Parse(entry[0]),
                        LocalSlot = int.Parse(entry[0]),
                        ResolveItem = false
                    });
                }
            }
            else
            {
                foreach (List<string> entry in data.Entries)
                {
                    int newSlot = Interlocked.Increment(ref CurrentStudioSlotID);

                    LoadedStudioResolutionInfo.Add(new StudioResolveInfo
                    {
                        GUID = manifest.GUID,
                        Slot = int.Parse(entry[0]),
                        LocalSlot = newSlot,
                        ResolveItem = true
                    });

                    //Logger.Log(LogLevel.Info, $"StudioResolveInfo - " +
                    //                          $"GUID: {manifest.GUID} " +
                    //                          $"Slot: {int.Parse(entry[0])} " +
                    //                          $"LocalSlot: {newSlot} " +
                    //                          $"Count: {LoadedStudioResolutionInfo.Count}");

                    entry[0] = newSlot.ToString();
                }
            }
        }

        public static void ShowGUIDError(string GUID)
        {
            if (LoadedResolutionInfo.Any(x => x.GUID == GUID) || LoadedStudioResolutionInfo.Any(x => x.GUID == GUID))
                //we have the GUID loaded, so the user has an outdated mod
                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Outdated mod detected! [{GUID}]");
            else
                //did not find a match, we don't have the mod
                Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] WARNING! Missing mod detected! [{GUID}]");
        }

        public enum ResolveType { Save, Load }
        internal static void ResolveStudioObjects(ExtensibleSaveFormat.PluginData extendedData, ResolveType resolveType)
        {
            Dictionary<int, ObjectInfo> ObjectList = StudioObjectSearch.FindObjectInfo(StudioObjectSearch.SearchType.All);

            //Resolve every item with extended data
            if (extendedData != null && extendedData.data.ContainsKey("itemInfo"))
            {
                List<StudioResolveInfo> extInfo;

                if (resolveType == ResolveType.Save)
                    extInfo = ((List<byte[]>)extendedData.data["itemInfo"]).Select(x => StudioResolveInfo.Unserialize(x)).ToList();
                else
                    extInfo = ((object[])extendedData.data["itemInfo"]).Select(x => StudioResolveInfo.Unserialize((byte[])x)).ToList();

                foreach (StudioResolveInfo extResolve in extInfo)
                {
                    ResolveStudioObject(extResolve, ObjectList[extResolve.DicKey], resolveType);
                    ObjectList.Remove(extResolve.DicKey);
                }
            }

            //Resolve every item without extended data in case of hard mods
            if (resolveType == ResolveType.Load)
            {
                foreach (ObjectInfo OI in ObjectList.Where(x => x.Value is OIItemInfo || x.Value is OILightInfo).Select(x => x.Value))
                {
                    if (OI is OIItemInfo Item)
                        ResolveStudioObject(Item);
                    else if (OI is OILightInfo Light)
                        ResolveStudioObject(Light);
                }
            }
        }

        internal static void ResolveStudioObject(StudioResolveInfo extResolve, ObjectInfo OI, ResolveType resolveType = ResolveType.Load)
        {
            if (OI is OIItemInfo Item)
            {
                StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == Item.no && x.GUID == extResolve.GUID);
                if (intResolve != null)
                {
                    if (resolveType == ResolveType.Load)
                        Logger.Log(LogLevel.Info, $"[UAR] Resolving (Studio Item) [{extResolve.GUID}] {Item.no}->{intResolve.LocalSlot}");
                    Traverse.Create(Item).Property("no").SetValue(intResolve.LocalSlot);
                }
                else if (resolveType == ResolveType.Load)
                    ShowGUIDError(extResolve.GUID);
            }
            else if (OI is OILightInfo Light)
            {
                StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == Light.no && x.GUID == extResolve.GUID);
                if (intResolve != null)
                {
                    if (resolveType == ResolveType.Load)
                        Logger.Log(LogLevel.Info, $"[UAR] Resolving (Studio Light) [{extResolve.GUID}] {Light.no}->{intResolve.LocalSlot}");
                    Traverse.Create(Light).Property("no").SetValue(intResolve.LocalSlot);
                }
                else if (resolveType == ResolveType.Load)
                    ShowGUIDError(extResolve.GUID);
            }
        }

        internal static void ResolveStudioObject(ObjectInfo OI)
        {
            if (OI is OIItemInfo Item)
            {
                if (!ListLoader.InternalStudioItemList.Contains(Item.no))
                {
                    //Item does not exist in the item list, probably a missing hard mod. See if we have a sideloader mod with the same ID
                    StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == Item.no);
                    if (intResolve != null)
                    {
                        //Found a match
                        Logger.Log(LogLevel.Info, $"[UAR] Compatibility resolving (Studio Item) {Item.no}->{intResolve.LocalSlot}");
                        Traverse.Create(Item).Property("no").SetValue(intResolve.LocalSlot);
                    }
                    else
                    {
                        //No match was found
                        Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] Compatibility resolving (Studio Item) failed, no match found for ID {Item.no}");
                    }
                }
            }
            else if (OI is OILightInfo Light)
            {
                if (!Singleton<Info>.Instance.dicLightLoadInfo.TryGetValue(Light.no, out Info.LightLoadInfo lightLoadInfo))
                {
                    StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == Light.no);
                    if (intResolve != null)
                    {
                        //Found a match
                        Logger.Log(LogLevel.Info, $"[UAR] Compatibility resolving (Studio Light) {Light.no}->{intResolve.LocalSlot}");
                        Traverse.Create(Light).Property("no").SetValue(intResolve.LocalSlot);
                    }
                    else
                    {
                        //No match was found
                        Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] Compatibility resolving (Studio Light) failed, no match found for ID {Light.no}");
                    }
                }
            }
        }

        internal static void ResolveStudioMap(ExtensibleSaveFormat.PluginData extData, ResolveType resolveType)
        {
            //Set map ID to the resolved ID
            int MapID = Singleton<Studio.Studio>.Instance.sceneInfo.map;

            if (MapID == -1) //Loaded scene has no map
                return;

            if (extData != null && extData.data.ContainsKey("mapInfoGUID"))
            {
                string MapGUID = (string)extData.data["mapInfoGUID"];

                StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == MapID && x.GUID == MapGUID);
                if (intResolve != null)
                {
                    if (resolveType == ResolveType.Load)
                        Logger.Log(LogLevel.Info, $"[UAR] Resolving (Studio Map) [{MapGUID}] {MapID}->{intResolve.LocalSlot}");
                    Singleton<Studio.Studio>.Instance.sceneInfo.map = intResolve.LocalSlot;
                }
                else
                    ShowGUIDError(MapGUID);
            }
            else if (resolveType == ResolveType.Load)
            {
                if (!Singleton<Info>.Instance.dicMapLoadInfo.TryGetValue(MapID, out Info.MapLoadInfo mapInfo))
                {
                    //Map ID saved to the scene doesn't exist in the map list, try compatibility resolving
                    StudioResolveInfo intResolve = LoadedStudioResolutionInfo.FirstOrDefault(x => x.ResolveItem && x.Slot == MapID);
                    if (intResolve != null)
                    {
                        //Found a matching sideloader mod
                        Logger.Log(LogLevel.Info, $"[UAR] Compatibility resolving (Studio Map) {MapID}->{intResolve.LocalSlot}");
                        Singleton<Studio.Studio>.Instance.sceneInfo.map = intResolve.LocalSlot;
                    }
                    else
                    {
                        Logger.Log(LogLevel.Warning | LogLevel.Message, $"[UAR] Compatibility resolving (Studio Map) failed, no match found for ID {MapID}");
                    }
                }
            }
        }

        private static readonly HashSet<string> CategoryAndGroupList = new HashSet<string>()
        {
            "itemcategory",
            "animecategory",
            "voicecategory",
            "itemgroup",
            "animegroup",
            "voicegroup"
        };
    }
}