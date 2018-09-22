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

    const string PrimaryVentId = "Primary";

    const string DefaultAirlockControlSettings = @"[Airlock Control Settings]
statusLCD=!airlocks
;Below the --- list a group name for each airlock per line
;The 3 already listed are for examples and can safely be removed
---
Airlock 1
Main Hangar
Airlock 2 ";

    const string DefaultAirLockSettings = @"[Airlock Settings]
externalOjects=Ext
internalOjects=Int
backupAirVents=Backup
depresserizeWarning=10
presserizeWarning=5";

  }
}
