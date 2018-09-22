using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
  partial class Program : MyGridProgram
  {
    // This file contains your actual script.
    //
    // You can either keep all your code here, or you can create separate
    // code files to make your program easier to navigate while coding.
    //
    // In order to add a new utility class, right-click on your project, 
    // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
    // category under 'Visual C# Items' on the left hand side, and select
    // 'Utility Class' in the main area. Name it in the box below, and
    // press OK. This utility class will be merged in with your code when
    // deploying your final script.
    //
    // You can also simply create a new utility class manually, you don't
    // have to use the template if you don't want to. Just do so the first
    // time to see what a utility class looks like.

    Dictionary<string, Airlock> Airlocks;
    StringBuilder Output;

    MyIni InitSettings;
    MyIniParseResult InitParseResult;
    MyCommandLine CommandLine;
    string OldCustomData;

    public Program()
    {
      Airlocks = new Dictionary<string, Airlock>();
      CommandLine = new MyCommandLine();
      InitSettings = new MyIni();
      Runtime.UpdateFrequency = UpdateFrequency.Update10;
      Output = new StringBuilder();
      Initialize();
    }

    private void Initialize()
    {
      Airlocks.Clear();
      if (string.IsNullOrEmpty(Me.CustomData)) Me.CustomData = DefaultAirlockControlSettings;
      if (!InitSettings.TryParse(Me.CustomData, out InitParseResult))
      {
        Echo(InitParseResult.ToString());
        Runtime.UpdateFrequency = UpdateFrequency.None;
        return;
      }
      var AirlockIds = InitSettings.EndContent.Split('\n').Where(x => !string.IsNullOrEmpty(x)).ToList();
      foreach (var airlockId in AirlockIds) Airlocks.Add(airlockId, new Airlock(this, airlockId));
      OldCustomData = Me.CustomData;
    }

    public void Save() { }

    public void Main(string argument, UpdateType updateSource)
    {
      Output.Clear();
      switch (updateSource)
      {
        case UpdateType.Terminal:
        case UpdateType.Trigger:
          ProcessCommand(argument);
          break;
        default:
          Update();
          break;
      }
      Echo(Output.ToString());
    }

    private void ProcessCommand(string argument)
    {
      if (CommandLine.TryParse(argument))
      {
        var modifiers = CycleModifiers.None;
        if (CommandLine.Switch("open")) modifiers |= CycleModifiers.AutoOpen;
        if (CommandLine.Switch("skip-wait")) modifiers |= CycleModifiers.SkipWaitTime;
        switch (CommandLine.Argument(0).ToLower())
        {
          case "initialize":
          case "init":
            Output.AppendLine("Reinitializing");
            Initialize();
            break;
          case "presserize":
          case "in":
            Airlocks[CommandLine.Argument(1)].CycleIn(modifiers);
            break;
          case "depresserize":
          case "out":
            Airlocks[CommandLine.Argument(1)].CycleOut(modifiers);
            break;
          case "toggle":
            Airlocks[CommandLine.Argument(1)].CycleToggle(modifiers);
            break;
        }
      }
    }

    private void Update()
    {
      if (Me.CustomData != OldCustomData) Initialize();
      if (false && !InitParseResult.Success)
      {
        Output.AppendLine($"Unable to read settings: {InitParseResult.LineNo} - {InitParseResult.ToString()}");
        return;
      }
      Output.AppendLine($"Known Airlocks: {Airlocks.Count}");
      foreach (var airlock in Airlocks)
      {
        airlock.Value.Update();
        Output.AppendLine(airlock.Value.ToString());
      }
    }
  }
}