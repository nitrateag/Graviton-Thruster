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


//https://github.com/malware-dev/MDK-SE/wiki/SpaceEngineers.Game.ModAPI.Ingame.IMyGravityGenerator

//https://github.com/malware-dev/MDK-SE/wiki/Sandbox.ModAPI.Ingame.IMyShipController

// détection de direction input
//https://github.com/malware-dev/MDK-SE/wiki/Sandbox.ModAPI.Ingame.IMyShipController.MoveIndicator


//Event !!!
//https://github.com/malware-dev/MDK-SE/wiki/Continuous-Running-No-Timers-Needed


//Simplex
//https://sites.math.washington.edu/~burke/crs/407/notes/section2.pdf

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

        //Name of group for filter onlycomponent user wnat to use
        const string gravityThrusterGroupName = "Graviton Thruster";
        float shipMass;

        //Maximum thrust available on each side
        Vector3D maximumThrustPerSide_kN;
        //Maximum speed gained every 10 Ticks
        Vector3D maxSpeedBy10Ticks_ms;

        //The direction of graviton thruster are exprimed in base of ship grid
        List<MyGravitonThruster> BackForw_thruster;
        List<MyGravitonThruster> LeftRight_thruster;
        List<MyGravitonThruster> UpDown_thruster;

        List<IMyGravityGenerator> allGravityGen;
        string outDebug = "";
        IMyTextSurface lcd1 = null;
        IMyTextSurface lcd2 = null;
        IMyTextSurface lcd3 = null;

        IMyCockpit cockpit = null;
        //Vector3 currentSideEnabled = new Vector3(0, 0, 0);
        Vector3 lastDirection = new Vector3(0, 0, 0);

        Vector3D centerOfMass;
        MatrixD B_abs2B_cockpit; //Matrice to change a vector in base absolute to base of cockpit
        Matrix rotation_B_cockpit2B_ship; //Matrice to change a vector base of cockpit to base of ship grid
        MatrixD B_abs2B_ship; //Matrice to change a vector in base absolute to base of cockpit

        float[][] ThrustFactorComposator;

        public void printErrorPgr(string errorMsg)
        {
            printMsgPgr("ERROR: " + errorMsg);
        }
        public void printMsgPgr(string Msg)
        {
            Echo(Msg);
            Me.GetSurface(0).WriteText(Me.GetSurface(0).GetText() + "\n" + Msg);
        }


        public Program()
        {
            var prgLcd = Me.GetSurface(0);
            prgLcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            prgLcd.Font = "Green";
            Me.GetSurface(0).WriteText("::GRAVITY THRUSTER::\n");

            
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set RuntimeInfo.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.

            //Debug
            //lcd1 = GridTerminalSystem.GetBlockWithName("LCD") as IMyTextPanel;
            //if (lcd1 == null)
            //    lcd1 = GridTerminalSystem.GetBlockWithName("LCD1") as IMyTextPanel;
            //else if (lcd1 == null)
            //    Echo(" \"LCD1\" not found");

            //lcd2 = GridTerminalSystem.GetBlockWithName("LCD2") as IMyTextPanel;
            //if (lcd2 == null)
            //    Echo(" \"LCD2\" not found");


            //init


            List <IMyCockpit> allCockpit = new List<IMyCockpit>();

            GridTerminalSystem.GetBlocksOfType(allCockpit, cockpit => cockpit.CanControlShip && cockpit.ControlThrusters && cockpit.IsWorking);

            if (allCockpit.Count == 0)
            {
                printErrorPgr("No cockit who can control thrusters found");
                return;
            }
            else if (allCockpit.Count == 1)
                cockpit = allCockpit[0];
            else
            {
                //allCockpit.FirstOrDefault(cockpit => cockpit.IsMainCockpit)
                cockpit = allCockpit.FirstOrDefault(cockpit => cockpit.IsMainCockpit);
                if (cockpit == null)
                {
                    printErrorPgr("If your are using multi cockpit, set once of them 'Main Cockpit' or enable 'Control Thrusters' at only once of them");
                    return;
                }
            }
            lcd1 = cockpit.GetSurface(0);
            lcd2 = cockpit.GetSurface(1);
            lcd3 = cockpit.GetSurface(2);
            if (lcd1 != null)
            {
                lcd1.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                lcd1.Font = "Monospace";
                lcd1.FontSize = 0.6f;
                lcd1.FontColor = Color.Green;

            }
            if (lcd2 != null)
            {
                lcd2.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                lcd2.Font = "Green";
            }
            if (lcd3 != null)
            {
                lcd3.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                lcd3.Font = "Green";
            }
            shipMass = cockpit.CalculateShipMass().TotalMass;

            IMyBlockGroup allThruster = GridTerminalSystem.GetBlockGroupWithName(gravityThrusterGroupName);
            List<IMyVirtualMass> allGravityMass = new List<IMyVirtualMass>();
            allGravityGen = new List<IMyGravityGenerator>();

            if (allThruster != null)
            {
                allThruster.GetBlocksOfType(allGravityGen);
                allThruster.GetBlocksOfType(allGravityMass);
            }

            if (allGravityGen.Count == 0)
                GridTerminalSystem.GetBlocksOfType(allGravityGen);

            if (allGravityMass.Count == 0)
                GridTerminalSystem.GetBlocksOfType(allGravityMass);

            if (allGravityMass.Count == 0 || allGravityGen.Count == 0)
            {
                printErrorPgr($"We didn't found yours thruster component : \n - gravity generator found = {allGravityMass.Count}\n - artificial mass found = {allGravityMass.Count}\nTry to set all yours gravity thrusters component in a same group \"{gravityThrusterGroupName}\"");
                return;
            }

            List <SharedMass> allShrGravityMass = new List<SharedMass>();
            allGravityMass.ForEach(mass => allShrGravityMass.Add(new SharedMass(mass)));

            LeftRight_thruster = new List<MyGravitonThruster>(12);
            UpDown_thruster = new List<MyGravitonThruster>(12);
            BackForw_thruster = new List<MyGravitonThruster>(12);


            foreach (IMyGravityGenerator gg in allGravityGen)
            {
                var newGravitonThurster = new MyGravitonThruster(gg, allShrGravityMass);
                if (newGravitonThurster.m_maximumThrust_kN == 0) //If there is no artificial mass in the feild of gravity generator, we don't record it
                    continue;

                switch (gg.Orientation.Up)
                {
                    case Base6Directions.Direction.Right:
                    case Base6Directions.Direction.Left:
                        LeftRight_thruster.Add(newGravitonThurster);
                        break;

                    case Base6Directions.Direction.Up:
                    case Base6Directions.Direction.Down:
                        UpDown_thruster.Add(newGravitonThurster);
                        break;

                    case Base6Directions.Direction.Forward:
                    case Base6Directions.Direction.Backward:
                        BackForw_thruster.Add(newGravitonThurster);
                        break;
                }

            }

      
            //Bases
            B_abs2B_cockpit = MatrixD.Transpose(cockpit.WorldMatrix);
            cockpit.Orientation.GetMatrix(out rotation_B_cockpit2B_ship);
            B_abs2B_ship = MatrixD.Multiply(B_abs2B_cockpit, rotation_B_cockpit2B_ship);

            //Recording of distances :

            centerOfMass = cockpit.CenterOfMass - cockpit.GetPosition();
            Vector3D.Rotate(ref centerOfMass, ref B_abs2B_ship, out centerOfMass);
            centerOfMass = centerOfMass + (cockpit.Position * 2.5f);

            //voir la diff entre cockpit.GetPosition() et cockpit.Position * 2.5f
            TorqueComposatorCalculator torqueComposator = new TorqueComposatorCalculator();
            StringBuilder strDebugCompute = new StringBuilder();

            try                     
            {
                torqueComposator.setThrusters(LeftRight_thruster, UpDown_thruster, BackForw_thruster, centerOfMass);
                torqueComposator.ComputeSolution(ref strDebugCompute, true);
            }
            catch(Exception e)
            {
                printErrorPgr("Cannot compute thruster balance\nSee custom data of program bloc for more info");

                Echo($"\nCannot compute thruster balance Exception:\n {e}\n---");
                Echo(strDebugCompute.ToString());
                Me.CustomData = strDebugCompute.ToString();

                throw;
            }
            Me.CustomData = strDebugCompute.ToString();

            ThrustFactorComposator = torqueComposator.OptimalThrustPowerPerThruster_kN;
            // we set 1 by default to counter some division by 0
            maximumThrustPerSide_kN = new Vector3(torqueComposator.sumOptimalThrustPowerPerSide_kN[0] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                torqueComposator.sumOptimalThrustPowerPerSide_kN[1] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                torqueComposator.sumOptimalThrustPowerPerSide_kN[2] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[2]
                );
            maxSpeedBy10Ticks_ms = (maximumThrustPerSide_kN * 1000) / (shipMass * 6);

            printMsgPgr("Gravity Thruster is operational");
            LogV3Prg(maximumThrustPerSide_kN * 1000 / shipMass, "Maximum Acceleration :", "m /s²");
            LogV3Prg(maximumThrustPerSide_kN, "Maximum Thrust :", "kN");
            //outDebug += torqueComposator.ToString();

            PrintLog();
            Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update10;

        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void PrintLog()
        {
            if (lcd1 == null)
            {
                Echo("LCD not found");
                Echo(outDebug);
                return;
            }

            float nbLineMax = 12;

            lcd1.WriteText("");

            if (lcd2 == null)
            {
                lcd1.WriteText(outDebug);
            }
            else
            {
                lcd2.WriteText("");

                var multiLine = outDebug.Split('\n');
                for (int i = 0; i < multiLine.Length; ++i)
                {
                    if (i < nbLineMax)
                        lcd1.WriteText(multiLine[i] + '\n', true);
                    else
                        lcd2.WriteText(multiLine[i] + '\n', true);

                }
            }

            //Echo(outDebug);

            outDebug = "";
        }

        public void LogLn(string str)
        { outDebug += str + "\n"; }

        
        public void LogV3(Vector3 v, string unit)
        {
            LogLn("X : " + v.X + " " + unit);
            LogLn("Y : " + v.Y + " " + unit);
            LogLn("Z : " + v.Z + " " + unit);
        }

        public void LogV3(Vector3D v, string title, string units = "")
        {
            LogLn(title);
            outDebug += "X:" + (v.X > 0 ? " " : "") + Math.Round(v.X, 3) + units;
            outDebug += " Y:" + (v.Y > 0 ? " " : "") + Math.Round(v.Y, 3) + units;
            LogLn(" Z: " + (v.Z > 0 ? " " : "") + Math.Round(v.Z, 3) + units);
        }

        public void LogV3Prg(Vector3D v, string title, string units)
        {
            printMsgPgr(title);
            string str = "X:" + (v.X > 0 ? " " : "") + Math.Round(v.X, 3) + units;
            str += " Y:" + (v.Y > 0 ? " " : "") + Math.Round(v.Y, 3) + units;
            printMsgPgr(str + " Z: " + (v.Z > 0 ? " " : "") + Math.Round(v.Z, 3) + units);
        }


        public void LogM3(MatrixD m, string title)
        {
            LogLn(title);
            outDebug += Math.Round(m.M11, 3) + "_" + Math.Round(m.M12, 3) + "_" + Math.Round(m.M13, 3) + "\n";
            outDebug += Math.Round(m.M21, 3) + "_" + Math.Round(m.M22, 3) + "_" + Math.Round(m.M23, 3) + "\n";
            outDebug += Math.Round(m.M31, 3) + "_" + Math.Round(m.M32, 3) + "_" + Math.Round(m.M33, 3) + "\n";
        }
        public void LogM3(Matrix m, string title)
        {
            LogLn(title);
            outDebug += m.M11 + "_" + m.M12 + "_" + m.M13 + "\n";
            outDebug += m.M21 + "_" + m.M22 + "_" + m.M23 + "\n";
            outDebug += m.M31 + "_" + m.M32 + "_" + m.M33 + "\n";
        }
        public void LogM3(Matrix3x3 m, string title)
        {
            LogLn(title);
            outDebug += m.M11 + "_" + m.M12 + "_" + m.M13 + "\n";
            outDebug += m.M21 + "_" + m.M22 + "_" + m.M23 + "\n";
            outDebug += m.M31 + "_" + m.M32 + "_" + m.M33 + "\n";
        }

        public void LogThrusters()
        {
            LogLn("X :");
            foreach (MyGravitonThruster ggD in LeftRight_thruster)
                LogLn(ggD.ToString());
            LogLn("Y :");
            foreach (MyGravitonThruster ggD in UpDown_thruster)
                LogLn(ggD.ToString());
            LogLn("Z :");
            foreach (MyGravitonThruster ggD in BackForw_thruster)
                LogLn(ggD.ToString());
        }


        public void Main(string argument, UpdateType updateSource)
        {


           // LogLn("Position : ");
           // LogLn(cockpit.Position.ToString());

            //LogLn("Direction (X,Y,Z): " + direction.ToString());
            //LogM3(MatrixD.Transpose(cockpit.WorldMatrix), "Transpose");
            //LogM3(MatrixD.Invert(cockpit.WorldMatrix), "Invert");

            Vector3D speedByCockpitOrientation = new Vector3D(cockpit.GetShipVelocities().LinearVelocity);
            B_abs2B_cockpit = MatrixD.Transpose(cockpit.WorldMatrix);
            B_abs2B_ship = MatrixD.Multiply(B_abs2B_cockpit, rotation_B_cockpit2B_ship);


           // LogV3(speedByCockpitOrientation, "Absolute Speed : m/s");

            Vector3D.Rotate(ref speedByCockpitOrientation, ref B_abs2B_cockpit, out speedByCockpitOrientation);
            LogV3(speedByCockpitOrientation, "Speed Local Cockpit: m/s");

            Vector3 direction;
            if (cockpit.DampenersOverride)
            {
                var dampenersMoveIndicator = -speedByCockpitOrientation / maxSpeedBy10Ticks_ms;
                if (dampenersMoveIndicator.AbsMax() > 1)
                    dampenersMoveIndicator /= dampenersMoveIndicator.AbsMax();

                direction = Vector3.Transform(new Vector3(
                   cockpit.MoveIndicator.X == 0 && Math.Abs(speedByCockpitOrientation.X) > 0.0009 ? dampenersMoveIndicator.X : cockpit.MoveIndicator.X,
                   cockpit.MoveIndicator.Y == 0 && Math.Abs(speedByCockpitOrientation.Y) > 0.0009 ? dampenersMoveIndicator.Y : cockpit.MoveIndicator.Y,
                   cockpit.MoveIndicator.Z == 0 && Math.Abs(speedByCockpitOrientation.Z) > 0.0009 ? dampenersMoveIndicator.Z : cockpit.MoveIndicator.Z)
                   , rotation_B_cockpit2B_ship);

            }
            else
                direction = Vector3.Transform(cockpit.MoveIndicator, rotation_B_cockpit2B_ship);

            //outDebug += ("CockPit direction : ");
            //LogLn(cockpit.Orientation.ToString());

            //LogLn("CockPit Pos : " + cockpit.Position.ToString());

            //LogLn("Center of mass : ");
            //LogV3(centerOfMass, "Center of mass : ");
            //Matrix3x3 yawRotaion = new Matrix3x3();
            //yawRotaion.SetDirectionVector(cockpit.Orientation.Forward, new Vector3(1, 1, 1));

            //LogM3(yawRotaion, "yawRotaion Transpose");
            //LogM3(MatrixD.Transpose(rotation_B_cockpit2B_ship), "B_cockpit2B_ship vanilla");
            //LogM3(rotation_B_cockpit2B_ship, "B_cockpit2B_ship Transpose");


            //LogLn("Direction");
            //LogV3(direction,"");


            if (direction != lastDirection)
            {
//                EnablePowerSide(direction);
                //var maxPower = LeftRight_thruster[0].m_maximumThrust;
                SetPower(direction);

                lastDirection = direction;
            }


            //foreach (IMyGravityGenerator gg in allGravityGen)
            //{
            //    //LogLn("thrusterPos"+ ((gg.Position)*2.5f).ToString());

            //    //LogV3(globPos, "glob");

            //    //LogLn("acc:" + gg.GravityAcceleration.ToString() + " enable : " + gg.Enabled.ToString());
            //    //LogLn("dir:" + gg.Orientation.ToString());

            //}

            //foreach (IMyGravityGenerator gg in allGravityGen)
            //{
            //    LogV3((centerOfMass - gg.Position * 2.5f), "thruster 2 center_mass :");
            //}

            LogThrusters();
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.
            PrintLog();
            outDebug = "";
        }

   


        //public void EnablePowerSide(Vector3 side)
        //{


        //    if (Math.Abs(side.X) != Math.Abs(currentSideEnabled.X))
        //    {
        //        bool enable = side.X != 0;
        //        foreach (MyGravitonThruster ggD in LeftRight_thruster)
        //        {
        //            ggD.Enabled = enable;
        //        }
        //    }

        //    if (Math.Abs(side.Y) != Math.Abs(currentSideEnabled.Y))
        //    {
        //        bool enable = side.Y != 0;

        //        foreach (MyGravitonThruster ggD in UpDown_thruster)
        //        {
        //            ggD.Enabled = enable;
        //        }
        //    }

        //    if (Math.Abs(side.Z) != Math.Abs(currentSideEnabled.Z))
        //    {
        //        bool enable = side.Z != 0;

        //        foreach (MyGravitonThruster ggD in BackForw_thruster)
        //        {
        //            ggD.Enabled = enable;
        //        }
        //    }

        //    currentSideEnabled = side;
        //}


        public void SetPower(Vector3 Direction)
        {
            for (int i = 0; i < LeftRight_thruster.Count; ++i)
                LeftRight_thruster[i].Thrust = ThrustFactorComposator[0][i] * Direction.X;
            for (int i = 0; i < UpDown_thruster.Count; ++i)
                UpDown_thruster[i].Thrust = ThrustFactorComposator[1][i] * Direction.Y;
            for (int i = 0; i < BackForw_thruster.Count; ++i)
                BackForw_thruster[i].Thrust = ThrustFactorComposator[2][i] * Direction.Z;
        }

    }
}