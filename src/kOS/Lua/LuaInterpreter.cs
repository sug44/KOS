using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Unity;
using System.Net;
using System.IO;
using System.Reflection;
using System.CodeDom;
using NLua;
using KeraLua;
using kOS.Safe;
using kOS.Safe.Utilities;
using Debug = UnityEngine.Debug;
using kOS.Safe.Screen;
using kOS.Safe.Persistence;

namespace kOS.Lua
{
    public class LuaInterpreter : IInterpreter, IFixedUpdateObserver
    {
        private NLua.Lua state;
        private KeraLua.Lua commandCoroutine;
        private bool commandPending = false;
        private static int instructionsPerUpdate = SafeHouse.Config.InstructionsPerUpdate;
        private static int instructionsThisUpdate = 0;
        public const string luaVersion = "5.4";
        public static readonly string[] FilenameExtensions = new string[] { "lua" };

        protected SharedObjects Shared { get; private set; }

        public LuaInterpreter(SharedObjects shared)
        {
            Shared = shared;
        }

        public void Boot()
        {
            state = new NLua.Lua();
            commandCoroutine = state.State.NewThread();
            commandCoroutine.SetHook(YieldHook, LuaHookMask.Count, 1);
            state["Shared"] = Shared;
            state["FlightGlobals"] = UnityEngine.MonoBehaviour.FindObjectOfType<FlightGlobals>();
            using (var streamReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("kOS.Lua.init.lua"))) {
                try {
                    state.DoString(streamReader.ReadToEnd());
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }

            if (Shared.GameEventDispatchManager != null) Shared.GameEventDispatchManager.Clear();
            if (Shared.FunctionManager != null) Shared.FunctionManager.Load();
            if (Shared.BindingMgr != null) Shared.BindingMgr.Load();

            if (Shared.Terminal != null) Shared.Terminal.Reset();
            // Booting message
            if (Shared.Screen != null)
            {
                Shared.Screen.ClearScreen();
                string bootMessage = string.Format("kOS Operating System\nLua v{0}\n(manual at {1})\n \nProceed.\n", luaVersion, SafeHouse.DocumentationURL);
                Shared.Screen.Print(bootMessage);
            }

            if (!Shared.Processor.CheckCanBoot()) return;

            VolumePath path = Shared.Processor.BootFilePath;
            // Check to make sure the boot file name is valid, and then that the boot file exists.
            if (path == null)
            {
                SafeHouse.Logger.Log("Boot file name is empty, skipping boot script");
            }
            else
            {
                // Boot is only called once right after turning the processor on,
                // the volume cannot yet have been changed from that set based on
                // Config.StartOnArchive, and Processor.CheckCanBoot() has already
                // handled the range check for the archive.
                Volume sourceVolume = Shared.VolumeMgr.CurrentVolume;
                var file = Shared.VolumeMgr.CurrentVolume.Open(path) as VolumeFile;
                if (file == null)
                {
                    SafeHouse.Logger.Log(string.Format("Boot file \"{0}\" is missing, skipping boot script", path));
                }
                else
                {
                    var content = file.ReadAll();
                    if (content == null)
                    {
                        DisplayError(string.Format("File '{0}' not found", path));
                        return;
                    }
                    ProcessCommand(content.String); // TODO: run through dofile when its ready
                }
            }
            Shared.UpdateHandler.AddFixedObserver(this);
        }

        public void Shutdown()
        {
            Shared.UpdateHandler.RemoveFixedObserver(this);
            state?.Dispose();
        }

        // This function will be running in lua land so be careful about stuff being garbage collected
        // Setting instructionsThisUpdate and instructionsPerUpdate to static somehow prevents them from being collected
        private void YieldHook(System.IntPtr L, System.IntPtr ar)
        {
            if (instructionsThisUpdate++ >= instructionsPerUpdate)
            {
                KeraLua.Lua.FromIntPtr(L).Yield(0);
            }
        }

        public void ProcessCommand(string commandText)
        {
            if ((LuaStatus)commandCoroutine.ResetThread() != LuaStatus.OK || commandCoroutine.LoadString(commandText, "command") != LuaStatus.OK)
            {
                var err = commandCoroutine.ToString(-1);
                commandCoroutine.Pop(1);
                DisplayError(err);
            } else
            {
                commandPending = true;
            }
        }

        public bool IsCommandComplete(string commandText)
        {
            if (commandCoroutine.LoadString(commandText) == LuaStatus.ErrSyntax)
            {
                var err = commandCoroutine.ToString(-1);
                commandCoroutine.Pop(1);
                // if the error is not caused by leaving stuff like do('"{ open let it go to ProcessCommand to be displayed to the user
                return !err.EndsWith("<eof>");
            }
            commandCoroutine.Pop(1);
            return true;
        }

        public bool IsWaitingForCommand()
        {
            return commandCoroutine.Status != LuaStatus.Yield;
        }

        public void BreakExecution(bool manual)
        {
            commandCoroutine.ResetThread();
        }

        public int InstructionsThisUpdate()
        {
            return instructionsThisUpdate;
        }

        public void KOSFixedUpdate(double dt)
        {
            instructionsPerUpdate = SafeHouse.Config.InstructionsPerUpdate;
            instructionsThisUpdate = 0;
            // resumes the coroutine created by ProcessCommand after it was created and after it yielded due to running out of instructions
            if (commandCoroutine.Status == LuaStatus.Yield || commandPending)
            {
                commandPending = false;
                LuaStatus status = commandCoroutine.Resume(state.State, 0);
                if (status != LuaStatus.OK & status != LuaStatus.Yield)
                {
                    DisplayError(commandCoroutine.ToString(-1));
                }
            }
        }

        public void Dispose()
        {
            state.Dispose();
            Shared.UpdateHandler.RemoveFixedObserver(this);
        }

        private void DisplayError(string errorMessage)
        {
            Shared.Logger.Log("lua error: "+errorMessage);
            Shared.SoundMaker.BeginFileSound("error");
            Shared.Screen.Print(errorMessage);
        }
    }
}
