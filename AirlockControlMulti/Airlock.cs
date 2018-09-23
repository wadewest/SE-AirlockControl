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
  partial class Program
  {

    public enum AirlockStates { BlockGroupNotFound, Deactivated, Initializing, Pressurized, Pressurizing, Depressurized, Depressurizing };
    public struct CycleModifiers
    {
      public bool AutoOpenAll, SkipWaitTime;
      public HashSet<string> AutoOpen;
    }

    public class Airlock
    {
      static readonly Color ColorSafe = new Color(0, 255, 0, 255);
      static readonly Color ColorDanger = new Color(255, 0, 0, 255);
      static readonly Color ColorNormal = new Color(255, 255, 255, 255);

      private IMyBlockGroup BlockGroup;
      private int CurrentBlockCount;
      private string AirlockId;
      private Program TheProgram;
      private string CurrentConfig;
      private StringBuilder lcdOutput;
      private IEnumerator<bool> CurrentProcedure;

      private Dictionary<string, object> _lazy_cache;

      private string TagExternalObjects = "Ext";
      private string TagInternalObjects = "Int";
      private int DepressurizeWarningTime = 10;
      private int PressurizeWarningTime = 2;

      public AirlockStates State { get; private set; }

      public Airlock(Program theProgram, string airlockId)
      {
        AirlockId = airlockId;
        TheProgram = theProgram;
        _lazy_cache = new Dictionary<string, object>();
        lcdOutput = new StringBuilder();
        Initialize();
      }

      private void Initialize()
      {
        State = AirlockStates.Initializing;
        BlockGroup = TheProgram.GridTerminalSystem.GetBlockGroupWithName(AirlockId);
        CurrentBlockCount = CountTheBlocks();
        if (BlockGroup == null)
        {
          State = AirlockStates.BlockGroupNotFound;
          return;
        }
        _lazy_cache.Clear();
        foreach (var panel in AllTextPanels)
        {
          panel.ShowPublicTextOnScreen();
          panel.FontSize = 2.5F;
        }
        CurrentConfig = PrimaryVent.CustomData;
        CurrentProcedure = DetectCurrentState();
      }

      public void Update()
      {
        if (State == AirlockStates.BlockGroupNotFound) return;
        lcdOutput.Clear();
        var nextStep = CurrentProcedure.MoveNext();
        if (!nextStep) // current procedure is done, switch to monitoring
        {
          SwitchProcedure(ProcedureMonitor());
        }
        foreach (var panel in AllTextPanels) panel.WritePublicText(lcdOutput);
      }

      private IEnumerator<bool> DetectCurrentState()
      {
        var cycleModifiers = default(CycleModifiers);
        cycleModifiers.SkipWaitTime = true;
        if (!PrimaryVent.CanPressurize || PrimaryVent.GetOxygenLevel() == 0)
        {
          State = AirlockStates.Depressurized;
          return ProcedureMonitor();
        }
        if (PrimaryVent.GetOxygenLevel() > 0 && PrimaryVent.Depressurize)
        {
          return ProcedureDepressurize(cycleModifiers);
        }
        if (PrimaryVent.GetOxygenLevel() < 1F && !PrimaryVent.Depressurize)
        {
          return ProcedurePressurize(cycleModifiers);
        }
        if (PrimaryVent.GetOxygenLevel() == 1F && !PrimaryVent.Depressurize)
        {
          State = AirlockStates.Pressurized;
          return ProcedureMonitor();
        }
        return ProcedureMonitor();
      }

      public void CycleToggle(CycleModifiers modifiers = default(CycleModifiers))
      {
        switch (State)
        {
          case AirlockStates.Pressurized:
          case AirlockStates.Pressurizing:
            CycleOut(modifiers);
            break;
          default:
            CycleIn(modifiers);
            break;
        }
      }

      public void CycleIn(CycleModifiers modifiers)
      {
        if (State == AirlockStates.Pressurized || State == AirlockStates.Pressurizing) return;
        SwitchProcedure(ProcedurePressurize(modifiers));
      }

      public void CycleOut(CycleModifiers modifiers)
      {
        if (State == AirlockStates.Depressurized || State == AirlockStates.Depressurizing) return;
        SwitchProcedure(ProcedureDepressurize(modifiers));
      }

      public void SwitchProcedure(IEnumerator<bool> NewProcedure)
      {
        CurrentProcedure.Dispose();
        CurrentProcedure = NewProcedure;
      }

      private IEnumerator<bool> ProcedureMonitor()
      {
        while (true)
        {
          if (ShouldReinitialize())
          {
            Initialize();
            yield return false;
          }
          lcdOutput.AppendLine($"Airlock:\n{State}");
          yield return true;
        }
      }

      private IEnumerator<bool> ProcedurePressurize(CycleModifiers modifiers)
      {
        State = AirlockStates.Pressurizing;
        // Signal Warning
        UpdateLights();
        // Wait for Presserize Warning Time
        if (!modifiers.SkipWaitTime)
        {
          var startTime = DateTime.Now;
          int timeRemaining;
          while ((timeRemaining = PressurizeWarningTime - (DateTime.Now - startTime).Seconds) > 0)
          {
            lcdOutput.AppendLine("Warning\nAirlock Will\nPressurize");
            lcdOutput.Append(timeRemaining.ToString()).AppendLine(" seconds");
            if (timeRemaining <= 5) lcdOutput.AppendLine("Stand Clear\nof Doors");
            yield return true;
          }
        }
        // Close and Lock all doors
        foreach (var door in AllDoors) door.CloseDoor();
        yield return true;
        while (AllDoors.Any(door => door.Status != DoorStatus.Closed))
        {
          lcdOutput.AppendLine("Warning\nClosing Doors\nStand Clear");
          yield return true;
        }
        foreach (var door in AllDoors) door.Enabled = false;
        yield return true;
        // Pressurize Airlock
        foreach (var vent in MainVents) vent.Depressurize = false;
        while (MainVents.Any(vent => vent.GetOxygenLevel() < 1F))
        {
          lcdOutput.AppendLine("Warning\nPressurizing");
          lcdOutput.AppendLine($"{(int)(PrimaryVent.GetOxygenLevel() * 100)}%");
          yield return true;
        }
        State = AirlockStates.Pressurized;
        // Turn Alarms Off
        UpdateLights();
        // Update Lights and Displays
        UnlockDoors(modifiers, InternalDoors);
        // TODO: Finish Implementing PresserizeProcedure
        yield return false;
      }

      private IEnumerator<bool> ProcedureDepressurize(CycleModifiers modifiers)
      {
        State = AirlockStates.Depressurizing;
        // Signal Warnings
        UpdateLights();
        // Wait for Depresserize Warning Time
        if (!modifiers.SkipWaitTime)
        {
          var startTime = DateTime.Now;
          int timeRemaining;
          while ((timeRemaining = DepressurizeWarningTime - (DateTime.Now - startTime).Seconds) > 0)
          {
            lcdOutput.AppendLine("Warning\nAirlock Will\nDepressurize");
            lcdOutput.Append(timeRemaining.ToString()).AppendLine(" seconds");
            if (timeRemaining <= 5) lcdOutput.AppendLine("Stand Clear\nof Doors");
            else if (timeRemaining <= 10) lcdOutput.AppendLine("Helments On");
            yield return true;
          }
        }
        // Close and Lock all doors
        foreach (var door in AllDoors) door.CloseDoor();
        yield return true;
        while (AllDoors.Any<IMyDoor>(door => door.Status != DoorStatus.Closed))
        {
          lcdOutput.AppendLine("Warning\nClosing Doors\nStand Clear");
          yield return true;
        }
        foreach (var door in AllDoors) door.Enabled = false;
        yield return true;
        // Decompress Airlock
        foreach (var vent in MainVents) vent.Depressurize = true;
        while (MainVents.Any(vent => vent.GetOxygenLevel() > 0F))
        {
          lcdOutput.AppendLine("Warning\nDepressurizing");
          lcdOutput.AppendLine($"{(int)(PrimaryVent.GetOxygenLevel() * 100)}%");
          yield return true;
        }
        State = AirlockStates.Depressurized;
        // Turn Alarms Off
        UpdateLights();
        // Update Lights and Displays
        UnlockDoors(modifiers, ExternalDoors);
        // TODO: Finish DepresserizeProcedure Implementation
        yield return false;
      }

      private void UpdateLights()
      {
        IEnumerable<IMyLightingBlock> safeLights, unsafeLights;
        var blinkTheLights = false;
        switch(State)
        {
          case AirlockStates.Depressurized:
            safeLights = ExternalLights;
            unsafeLights = InternalLights;
            break;
          case AirlockStates.Pressurized:
            safeLights = InternalLights;
            unsafeLights = ExternalLights;
            break;
          case AirlockStates.Depressurizing:
          case AirlockStates.Pressurizing:
            safeLights = new List<IMyLightingBlock>();
            unsafeLights = AllLights;
            blinkTheLights = true;
            break;
          default:
            safeLights = unsafeLights = AllLights;
            break;
        }
        foreach(var light in AllLights)
        {
          if (safeLights.Contains(light))
          {
            light.Color = ColorSafe;
          }
          else if (unsafeLights.Contains(light))
          {
            light.Color = ColorDanger;
          }
          else
          {
            light.Color = ColorNormal;
          }
          if (blinkTheLights)
          {
            light.BlinkIntervalSeconds = 0.5F;
            light.BlinkOffset = 15F;
          }
          else
          {
            light.BlinkIntervalSeconds = 0F;
          }
        }
      }

      private void UnlockDoors(CycleModifiers modifiers, IEnumerable<IMyDoor> Doors)
      {
        var doorsToOpen = new HashSet<IMyDoor>();
        if (modifiers.AutoOpen != null)
        {
          foreach (var tag in modifiers.AutoOpen)
          {
            foreach (var door in Doors.Where(x => x.CustomName.Contains(tag))) { doorsToOpen.Add(door); }
          }
        }
        foreach (var door in Doors)
        {
          door.Enabled = true;
          if (modifiers.AutoOpenAll && doorsToOpen.Count < 1) door.OpenDoor();
        }
        foreach (var door in doorsToOpen) { door.OpenDoor(); }
      }

      public IMyAirVent PrimaryVent
      {
        get
        {
          if (!_lazy_cache.ContainsKey("PrimaryVent"))
          {
            var vents = MainVents.OrderBy(x => x.CustomName);
            try
            {
              _lazy_cache["PrimaryVent"] = vents.First(x => x.CustomName.Contains(PrimaryVentId));
            }
            catch
            {
              var vent = vents.First();
              vent.CustomName = $"{vent.CustomName} {PrimaryVentId}";
              _lazy_cache["PrimaryVent"] = vent;
            }
          }
          return _lazy_cache["PrimaryVent"] as IMyAirVent;
        }
      }

      public IEnumerable<IMyAirVent> MainVents
      {
        get
        {
          if (!_lazy_cache.ContainsKey("MainVents"))
          {
            _lazy_cache["MainVents"] = AllVents.Except(BackupVents);
          }
          return _lazy_cache["MainVents"] as IEnumerable<IMyAirVent>;
        }
      }

      public IEnumerable<IMyAirVent> BackupVents
      {
        get
        {
          if (!_lazy_cache.ContainsKey("BackupVents"))
          {
            _lazy_cache["BackupVents"] = AllVents.Where(x => x.CustomName.Contains(TagBackupVents));
          }
          return _lazy_cache["BackupVents"] as IEnumerable<IMyAirVent>;
        }
      }

      public IEnumerable<IMyAirVent> AllVents
      {
        get
        {
          if (!_lazy_cache.ContainsKey("AllVents"))
          {
            var vents = new List<IMyAirVent>();
            BlockGroup.GetBlocksOfType<IMyAirVent>(vents);
            _lazy_cache["AllVents"] = vents;
          }
          return _lazy_cache["AllVents"] as IEnumerable<IMyAirVent>;
        }
      }

      public IEnumerable<IMyDoor> ExternalDoors
      {
        get
        {
          if (!_lazy_cache.ContainsKey("ExternalDoors"))
          {
            _lazy_cache["ExternalDoors"] = AllDoors.Where(x => x.CustomName.Contains(TagExternalObjects));
          }
          return _lazy_cache["ExternalDoors"] as IEnumerable<IMyDoor>;
        }
      }

      public IEnumerable<IMyDoor> InternalDoors
      {
        get
        {
          if (!_lazy_cache.ContainsKey("InternalDoors"))
          {
            _lazy_cache["InternalDoors"] = AllDoors.Where(x => x.CustomName.Contains(TagInternalObjects));
          }
          return _lazy_cache["InternalDoors"] as IEnumerable<IMyDoor>;
        }
      }

      public IEnumerable<IMyLightingBlock> ExternalLights
      {
        get
        {
          if (!_lazy_cache.ContainsKey("ExternalLights"))
          {
            _lazy_cache["ExternalLights"] = AllLights.Where(x => x.CustomName.Contains(TagExternalObjects));
          }
          return _lazy_cache["ExternalLights"] as IEnumerable<IMyLightingBlock>;
        }
      }

      public IEnumerable<IMyLightingBlock> InternalLights
      {
        get
        {
          if (!_lazy_cache.ContainsKey("InternalLights"))
          {
            _lazy_cache["InternalLights"] = AllLights.Where(x => x.CustomName.Contains(TagInternalObjects));
          }
          return _lazy_cache["InternalLights"] as IEnumerable<IMyLightingBlock>;
        }
      }

      public IEnumerable<IMyLightingBlock> AllLights
      {
        get
        {
          if (!_lazy_cache.ContainsKey("AllLights"))
          {
            var lights = new List<IMyLightingBlock>();
            BlockGroup.GetBlocksOfType<IMyLightingBlock>(lights);
            _lazy_cache["AllLights"] = lights;
          }
          return _lazy_cache["AllLights"] as IEnumerable<IMyLightingBlock>;
        }
      }

      public IEnumerable<IMyDoor> AllDoors
      {
        get
        {
          if (!_lazy_cache.ContainsKey("AllDoors"))
          {
            var doors = new List<IMyDoor>();
            BlockGroup.GetBlocksOfType<IMyDoor>(doors);
            _lazy_cache["AllDoors"] = doors;
          }
          return _lazy_cache["AllDoors"] as IEnumerable<IMyDoor>;
        }
      }

      public IEnumerable<IMyTextPanel> AllTextPanels
      {
        get
        {
          if (!_lazy_cache.ContainsKey("AllTextPanels"))
          {
            var panels = new List<IMyTextPanel>();
            BlockGroup.GetBlocksOfType<IMyTextPanel>(panels);
            _lazy_cache["AllTextPanels"] = panels;
          }
          return _lazy_cache["AllTextPanels"] as IEnumerable<IMyTextPanel>;
        }
      }

      public override string ToString()
      {
        return $"{AirlockId} - {State}";
      }

      private string TagBackupVents
      {
        get
        {
          return "Backup";
        }
      }

      private int CountTheBlocks()
      {
        var blocks = new List<IMyTerminalBlock>();
        BlockGroup.GetBlocks(blocks);
        return blocks.Count;
      }

      private bool ShouldReinitialize()
      {
        return CurrentBlockCount != CountTheBlocks();
      }

    }
  }
}
