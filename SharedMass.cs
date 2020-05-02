using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage;
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
        public class SharedMass 
        {
            public IMyVirtualMass mass;
            int nbNeedEnable;

            public SharedMass(IMyVirtualMass Mass) { mass = Mass; }

            public bool Enabled
            {
                get
                {
                    return nbNeedEnable > 0;
                }

                set
                {
                    if (value)
                        ++nbNeedEnable;
                    else if(nbNeedEnable > 0)
                        --nbNeedEnable;

                    mass.Enabled = nbNeedEnable > 0;
                }
            }

        }
    }
}
