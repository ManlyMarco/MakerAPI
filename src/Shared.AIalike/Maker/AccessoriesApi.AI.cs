﻿using System;
using AIChara;
using CharaCustom;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KKAPI.Maker
{
    /// <summary>
    /// Collection of methods useful for interfacing with character accessories. Has methods both for chara maker and
    /// everywhere else.
    /// Abstracts away MoreAccessories so you don't have to worry if it's installed or not.
    /// </summary>
    public static partial class AccessoriesApi
    {
        private static Type _moreAccessoriesType;

        private static CanvasGroup _accessorySlotCanvasGroup;

        private static Func<int, ChaFileAccessory.PartsInfo> _getPartsInfo;
        private static Func<int> _getCvsAccessoryCount;
        private static Func<ChaControl, int, CmpAccessory> _getChaAccessoryCmp;
        private static Func<ChaControl, CmpAccessory, int> _getChaAccessoryCmpIndex;

        /// <summary>
        /// Returns true if the accessory tab in maker is currently selected.
        /// If you want to know if the user can actually see the tab on the screen check <see cref="MakerAPI.IsInterfaceVisible"/>.
        /// </summary>
        public static bool AccessoryCanvasVisible => _accessorySlotCanvasGroup != null && _accessorySlotCanvasGroup.alpha.Equals(1f);

        /// <summary>
        /// True if the MoreAccessories mod is installed.
        /// Avoid relying on this and instead use other methods in this class since they will handle this for you.
        /// </summary>
        public static bool MoreAccessoriesInstalled => _moreAccessoriesType != null;

        /// <summary>
        /// Get the index of the currently selected accessory slot under Accessories group in Chara Maker.
        /// If none are selected or chara maker is not opened, returns -1. 0-indexed.
        /// Use <see cref="SelectedMakerAccSlotChanged"/> to get notified when the selected slot changes.
        /// </summary>
        public static int SelectedMakerAccSlot { get; private set; } = -1;

        /// <summary>
        /// Fires whenever the index of the currently selected accessory slot under Accessories group in Chara Maker is changed.
        /// This happens when user click on another slot.
        /// </summary>
        public static event EventHandler<AccessorySlotEventArgs> SelectedMakerAccSlotChanged;

        /// <summary>
        /// Fires when user selects a different accessory in the accessory window.
        /// </summary>
        public static event EventHandler<AccessorySlotEventArgs> AccessoryKindChanged;

        /// <summary>
        /// Fires after user copies an accessory within a single coordinate by using the Transfer window.
        /// </summary>
        public static event EventHandler<AccessoryTransferEventArgs> AccessoryTransferred;

        /// <summary>
        /// Get the accessory given a slot index.
        /// </summary>
        public static CmpAccessory GetAccessory(this ChaControl character, int accessoryIndex)
        {
            return _getChaAccessoryCmp(character, accessoryIndex);
        }

        /// <summary>
        /// Get slot index of his accessory, useful for referencing to the accesory in extended data.
        /// </summary>
        public static int GetAccessoryIndex(this CmpAccessory accessoryComponent)
        {
            var chaControl = GetOwningChaControl(accessoryComponent);
            return _getChaAccessoryCmpIndex(chaControl, accessoryComponent);
        }

        /// <summary>
        /// Get accessory UI entry in maker.
        /// Only works inside chara maker.
        /// </summary>
        public static CustomAcsCorrectSet GetCvsAccessory()
        {
            if (!MakerAPI.InsideMaker) throw new InvalidOperationException("Can only call GetCvsAccessory when inside Chara Maker");
            return Object.FindObjectOfType<CustomAcsCorrectSet>();
        }

        /*/// <summary>
        /// Get the index of the currently selected accessory slot under Accessories group in Chara Maker.
        /// If none are selected or chara maker is not opened, returns -1.
        /// Use <see cref="SelectedMakerSlotObservable"/> to get notified when the selected slot changes.
        /// </summary>
        public static int GetSelectedAccessoryIndex()
        {
            if (!MakerAPI.InsideMaker) return -1;
            return _getSelectedAccessoryIndex.Invoke();
        }*/

        /// <summary>
        /// Get accessory PartsInfo entry in maker.
        /// Only works inside chara maker.
        /// </summary>
        public static ChaFileAccessory.PartsInfo GetPartsInfo(int index)
        {
            if (_getPartsInfo == null) throw new InvalidOperationException("Can only call GetPartsInfo when inside Chara Maker");
            return _getPartsInfo(index);
        }

        /// <summary>
        /// Get count of the UI entries for accessories (accessory slots).
        /// Returns 0 outside of chara maker.
        /// </summary>
        public static int GetCvsAccessoryCount()
        {
            if (_getCvsAccessoryCount == null) return 0;
            return _getCvsAccessoryCount.Invoke();
        }

        /// <summary>
        /// Get the ChaControl that owns this accessory
        /// </summary>
        public static ChaControl GetOwningChaControl(this CmpAccessory accessoryComponent)
        {
            return accessoryComponent.GetComponentInParent<ChaControl>();
        }

        internal static void Init()
        {
            DetectMoreAccessories();

            BepInEx.Harmony.HarmonyWrapper.PatchAll(typeof(Hooks));

            MakerAPI.InsideMakerChanged += MakerAPI_InsideMakerChanged;
            MakerAPI.MakerFinishedLoading += (sender, args) => OnSelectedMakerSlotChanged(sender, 0);

            NoMoreaccsFallback:
            if (MoreAccessoriesInstalled)
            {
                try
                {
                    var patchesTraverse = Traverse.CreateWithType("MoreAccessoriesAI.Patches.ChaControl_Patches, MoreAccessories");

                    //GetCmpAccessory(ChaControl self, int slotNo)
                    var mGca = patchesTraverse.Method("GetCmpAccessory", new Type[] { typeof(ChaControl), typeof(int) });
                    if (!mGca.MethodExists()) throw new InvalidOperationException("Failed to find MoreAccessoriesAI.Patches.ChaControl_Patches.GetCmpAccessory");
                    _getChaAccessoryCmp = (control, componentIndex) => mGca.GetValue<CmpAccessory>(control, componentIndex);

                    _getChaAccessoryCmpIndex = (control, component) =>
                    {
                        var idx = Array.IndexOf(control.cmpAccessory, component);
                        if (idx >= 0) return idx;

                        // No better way than to iterate the entries until we get an out of range exception
                        idx = 20;
                        try
                        {
                            while (true)
                            {
                                if (_getChaAccessoryCmp(control, idx) == component)
                                    return idx;
                                idx++;
                            }
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            return -1;
                        }
                    };

                    //ChaFileAccessory.PartsInfo GetPartsInfo(ChaControl self, int slotNo)
                    var mGpi = patchesTraverse.Method("GetPartsInfo", new Type[] { typeof(ChaControl), typeof(int) });
                    if (!mGpi.MethodExists()) throw new InvalidOperationException("Failed to find MoreAccessoriesAI.Patches.ChaControl_Patches.GetPartsInfo");
                    _getPartsInfo = i => mGpi.GetValue<ChaFileAccessory.PartsInfo>(MakerAPI.GetCharacterControl(), i);
                }
                catch (Exception e)
                {
                    _moreAccessoriesType = null;
                    KoikatuAPI.Logger.LogWarning("Failed to set up MoreAccessories integration!");
                    KoikatuAPI.Logger.LogDebug(e);
                    goto NoMoreaccsFallback;
                }
            }
            else
            {
                _getChaAccessoryCmp = (control, i) => control.cmpAccessory[i];
                _getChaAccessoryCmpIndex = (control, component) => Array.IndexOf(control.cmpAccessory, component);
                _getPartsInfo = i => MakerAPI.GetCharacterControl().nowCoordinate.accessory.parts[i];
            }

            if (KoikatuAPI.EnableDebugLogging)
            {
                SelectedMakerAccSlotChanged += (sender, args) => KoikatuAPI.Logger.LogMessage(
                    $"SelectedMakerAccSlotChanged - id: {args.SlotIndex}, cvs: {args.CvsAccessory?.transform.name}, component: {args.AccessoryComponent?.name ?? "null"}");
                /* todo#if KK AccessoriesCopied += (sender, args) => KoikatuAPI.Logger.LogMessage(
                    $"AccessoriesCopied - ids: {string.Join(", ", args.CopiedSlotIndexes.Select(x => x.ToString()).ToArray())}, src:{args.CopySource}, dst:{args.CopyDestination}"); #endif*/
                AccessoryTransferred += (sender, args) => KoikatuAPI.Logger.LogMessage(
                    $"AccessoryTransferred - srcId:{args.SourceSlotIndex}, dstId:{args.DestinationSlotIndex}");
            }
        }

        private static void DetectMoreAccessories()
        {
            try
            {
                _moreAccessoriesType = Type.GetType("MoreAccessoriesAI.MoreAccessories, MoreAccessories", false);
            }
            catch (Exception e)
            {
                _moreAccessoriesType = null;
                KoikatuAPI.Logger.LogWarning("Failed to detect MoreAccessories!");
                KoikatuAPI.Logger.LogDebug(e);
            }
        }

        private static void MakerAPI_InsideMakerChanged(object sender, EventArgs e)
        {
            if (MakerAPI.InsideMaker)
            {
                _accessorySlotCanvasGroup = GameObject.Find("SubMenuAccessory").GetComponent<CanvasGroup>();

                _getCvsAccessoryCount = () => 20;

                SelectedMakerAccSlot = 0;
            }
            else
            {
                _accessorySlotCanvasGroup = null;
                _getCvsAccessoryCount = null;

                SelectedMakerAccSlot = -1;
            }
        }

        private static void OnSelectedMakerSlotChanged(object source, int newSlotIndex)
        {
            if (newSlotIndex == SelectedMakerAccSlot) return;
            SelectedMakerAccSlot = newSlotIndex;

            if (KoikatuAPI.EnableDebugLogging)
                KoikatuAPI.Logger.LogMessage("SelectedMakerSlotChanged - slot:" + newSlotIndex);

            if (SelectedMakerAccSlotChanged == null) return;
            try
            {
                SelectedMakerAccSlotChanged(source, new AccessorySlotEventArgs(newSlotIndex));
            }
            catch (Exception ex)
            {
                KoikatuAPI.Logger.LogError("Subscription to SelectedMakerSlot crashed: " + ex);
            }
        }

        private static void OnAccessoryKindChanged(object source, int slotNo)
        {
            if (!MakerAPI.InsideAndLoaded) return;

            if (KoikatuAPI.EnableDebugLogging)
                KoikatuAPI.Logger.LogMessage("AccessoryKindChanged - slot:" + slotNo);

            if (AccessoryKindChanged == null) return;
            try
            {
                AccessoryKindChanged(source, new AccessorySlotEventArgs(slotNo));
            }
            catch (Exception ex)
            {
                KoikatuAPI.Logger.LogError("Subscription to AccessoryKindChanged crashed: " + ex);
            }
        }

        private static void OnChangeAcs(CvsA_Copy instance)
        {
            if (AccessoryTransferred == null) return;

            try
            {
                var traverse = Traverse.Create(instance);
                var selSrc = traverse.Field("selSrc").GetValue<int>();
                var selDst = traverse.Field("selDst").GetValue<int>();

                var args = new AccessoryTransferEventArgs(selSrc, selDst);

                AccessoryTransferred(instance, args);
            }
            catch (Exception ex)
            {
                KoikatuAPI.Logger.LogError("Crash in AccessoryTransferred event: " + ex);
            }
        }
    }
}
