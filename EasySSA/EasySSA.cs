﻿#region License
// ====================================================
// EasySSA Copyright(C) 2017 Furkan Türkal
// This program comes with ABSOLUTELY NO WARRANTY; This is free software,
// and you are welcome to redistribute it under certain conditions; See
// file LICENSE, which is part of this source code package, for details.
// ====================================================
#endregion

using System;
using System.Net;
using System.Collections.Generic;
using EasySilkroadSecurityApi.Core.Tweening;
using EasySilkroadSecurityApi.Core.Tweening.Options;

namespace EasySilkroadSecurityApi
{
    public sealed class EasySSA {

        public static readonly Version Version = new Version(1, 0, 0, 0);


        public const string TEXT_MAX_TWEENS_REACHED = "Max Tweens reached: capacity has automatically been increased from #0 to #1. Use DOTween.SetTweensCapacity to set it manually at startup";
        public const string TEXT_CANT_CHANGE_SEQUENCED_VALUES = "You cannot change the values of a tween contained inside a Sequence";

        internal static bool WasInitialized { get; private set; }

        internal static bool IsQuitting { get; private set; }

        internal static bool IsDebugBuild;

        internal static int maxActiveTweenersReached, maxActiveSequencesReached; 
        
        internal static EasySSAComponent instance;

        static EasySSA() {
#if DEBUG
            IsDebugBuild = true;
#endif
        }

        //EasySSA ssa = new EasySSA();
        //ssa.Init();
        //ssa.OnPacketReceived()

        //SROModuleServer agent = new AgentServer();
        //agent.DOBind(ip, port).On

        static void InitCheck() {
            if (WasInitialized || IsQuitting) return;

            AutoInit();
        }

        static void AutoInit() {
            EasySSASettings settings = new EasySSASettings();
            Init(settings, false);
        }

        public static IEasySSAInit Init(bool useSafeMode) {
            if (WasInitialized) return instance;
            if (!IsQuitting) return null;

            EasySSASettings settings = new EasySSASettings();
            return Init(settings, useSafeMode);
        }

        static IEasySSAInit Init(EasySSASettings settings, bool useSafeMode) {
            WasInitialized = true;


            return instance;
        }


        public static void Clear(bool destroy = false) {
            if (!destroy) return;


        }


    }
}
