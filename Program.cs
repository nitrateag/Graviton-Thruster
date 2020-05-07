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
        const string gravityThrusterGroupName = "Gravity Thruster";
        float shipMass;

        //Maximum thrust available on each side
        Vector3D maximumThrustPerSide_Bship_kN_noZero;
        //Maximum speed gained every 10 Ticks
        Vector3D maxSpeedBy10Ticks_Bship_ms_noZero;

        //The direction of gravity thruster are exprimed in base of ship grid
        List<MyGravitonThruster> BackForw_thruster_Bship;
        List<MyGravitonThruster> LeftRight_thruster_Bship;
        List<MyGravitonThruster> UpDown_thruster_Bship;

        List<IMyGravityGenerator> allGravityGen;
        StringBuilder outDebug = new StringBuilder();
        IMyTextSurface lcd1 = null;
        IMyTextSurface lcd2 = null;
        IMyTextSurface lcd3 = null;
        IMyTextSurface prgLcd = null;

        IMyCockpit cockpit = null;
        Vector3 lastDirection_Bcock = new Vector3(0, 0, 0);

        Vector3D centerOfMass_Bship;
        MatrixD Babs_2_Bcockpit; //Matrice to change a vector in base absolute to base of cockpit
        MatrixD rotation_Bcockpit_2_Bship; //Matrice to change a vector base of cockpit to base of ship grid
        MatrixD rotation_Bship_2_Bcockpit; //Matrice to change a vector base of cockpit to base of ship grid
        MatrixD Babs_2_Bship; //Matrice to change a vector in base absolute to base of cockpit

        float[][] ThrustFactorComposator_Bship_kN;

        public void printErrorPgr(string errorMsg)
        {
            printMsgPgr("ERROR: " + errorMsg);
        }
        public void printMsgPgr(string Msg)
        {
            Echo(Msg);
            Me.GetSurface(0).WriteText(Me.GetSurface(0).GetText() + "\n" + Msg);
        }

        //search the good cockpit
        public void findCockpit()
        {
            List<IMyCockpit> allCockpit = new List<IMyCockpit>();

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
                setFont(ref lcd1);
            }
            if (lcd2 != null)
            {
                setFont(ref lcd2);
            }
            if (lcd3 != null)
            {
                setFont(ref lcd3);
            }
        }

        //Update all gravity thruters and compute news optimal coefficients
        public void resetAllGravityThruster()
        {
            //Search all thruster component
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
                printErrorPgr($"We didn't found yours thruster component : \n - gravity generator found = {allGravityGen.Count}\n - artificial mass found = {allGravityMass.Count}\nTry to set all yours gravity thrusters component in a same group \"{gravityThrusterGroupName}\"");
                return;
            }

            List<SharedMass> allShrGravityMass = new List<SharedMass>();
            allGravityMass.ForEach(mass => allShrGravityMass.Add(new SharedMass(mass)));

            //Sort gravity component
            LeftRight_thruster_Bship = new List<MyGravitonThruster>(12);
            UpDown_thruster_Bship = new List<MyGravitonThruster>(12);
            BackForw_thruster_Bship = new List<MyGravitonThruster>(12);


            foreach (IMyGravityGenerator gg in allGravityGen)
            {
                var newGravitonThurster = new MyGravitonThruster(gg, allShrGravityMass);
                if (newGravitonThurster.m_maximumThrust_kN == 0) //If there is no artificial mass in the feild of gravity generator, we don't record it
                    continue;

                switch (gg.Orientation.Up)
                {
                    case Base6Directions.Direction.Right:
                    case Base6Directions.Direction.Left:
                        LeftRight_thruster_Bship.Add(newGravitonThurster);
                        break;

                    case Base6Directions.Direction.Up:
                    case Base6Directions.Direction.Down:
                        UpDown_thruster_Bship.Add(newGravitonThurster);
                        break;

                    case Base6Directions.Direction.Forward:
                    case Base6Directions.Direction.Backward:
                        BackForw_thruster_Bship.Add(newGravitonThurster);
                        break;
                }

            }

            //Init Bases
            Matrix cockOrientation = new Matrix();
            cockpit.Orientation.GetMatrix(out cockOrientation);
            rotation_Bcockpit_2_Bship = cockOrientation;
            rotation_Bship_2_Bcockpit = MatrixD.Transpose(rotation_Bcockpit_2_Bship);

            Babs_2_Bcockpit = MatrixD.Transpose(cockpit.WorldMatrix); //Transpose is quicker than invert, and equivalent in this case
            Babs_2_Bship = MatrixD.Multiply(Babs_2_Bcockpit, rotation_Bcockpit_2_Bship);

            resetCenterOfMass();
        }

        //Get new center of mass and compute news optimal coefficients
        public void resetCenterOfMass()
        {
            //Recording of distances :
            centerOfMass_Bship = cockpit.CenterOfMass - cockpit.GetPosition();
            Vector3D.Rotate(ref centerOfMass_Bship, ref Babs_2_Bship, out centerOfMass_Bship);
            centerOfMass_Bship = centerOfMass_Bship + (cockpit.Position * 2.5f);

            //voir la diff entre cockpit.GetPosition() et cockpit.Position * 2.5f
            TorqueComposatorCalculator torqueComposator = new TorqueComposatorCalculator();
            StringBuilder strDebugCompute = new StringBuilder();

            try
            {
                torqueComposator.setThrusters(LeftRight_thruster_Bship, UpDown_thruster_Bship, BackForw_thruster_Bship, centerOfMass_Bship);
                torqueComposator.ComputeSolution(ref strDebugCompute, true);
            }
            catch (Exception e)
            {
                printErrorPgr("Cannot compute thruster balance\nSee custom data of program bloc for more info");

                Echo($"\nCannot compute thruster balance Exception:\n {e}\n---");
                Echo(strDebugCompute.ToString());
                Me.CustomData = strDebugCompute.ToString();

                throw;
            }

            shipMass = cockpit.CalculateShipMass().TotalMass;


            ThrustFactorComposator_Bship_kN = torqueComposator.OptimalThrustPowerPerThruster_kN;


            // we set float.MaxValue by default to counter some division by 0
            maximumThrustPerSide_Bship_kN_noZero = new Vector3D(
                torqueComposator.sumOptimalThrustPowerPerSide_kN[0] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                torqueComposator.sumOptimalThrustPowerPerSide_kN[1] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                torqueComposator.sumOptimalThrustPowerPerSide_kN[2] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[2]
                );
            maxSpeedBy10Ticks_Bship_ms_noZero = (maximumThrustPerSide_Bship_kN_noZero * 1000) / (shipMass * 6); // *6 because ther is 60 ticks pers second, so each 10Ticks is 1/6 seconds

            //Send thrust characteristics to user
            var maximumThrustPerSide_Bcock_kN = Bship_2_Bcock(new Vector3(torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                                                            torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                                                            torqueComposator.sumOptimalThrustPowerPerSide_kN[2]));
            printMsgPgr("Gravity Thruster is operational");
            LogV3Prg(Vector3.Abs(maximumThrustPerSide_Bcock_kN * 1000 / shipMass), "Maximum Acceleration :", "m/s²");
            LogV3Prg(Vector3.Abs(maximumThrustPerSide_Bcock_kN*1000), "Maximum Thrust :", "N");

            Vector3 theoricMaximumThrust_Bship = new Vector3(LeftRight_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                             UpDown_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                             BackForw_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN));

            Vector3 thrusterPosition_efficiency_Bship = new Vector3(
                theoricMaximumThrust_Bship.X == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.X / theoricMaximumThrust_Bship.X,
                theoricMaximumThrust_Bship.Y == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Y / theoricMaximumThrust_Bship.Y,
                theoricMaximumThrust_Bship.Z == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Z / theoricMaximumThrust_Bship.Z);

            LogV3Prg(Vector3.Abs(Bship_2_Bcock(thrusterPosition_efficiency_Bship)), "Position Efficiency :", "%");

            Me.CustomData = strDebugCompute.ToString();

        }

        public Program()
        {
            prgLcd = Me.GetSurface(0);
            setFont(ref prgLcd);
            prgLcd.WriteText("::GRAVITY THRUSTER::");

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


            findCockpit();

            resetAllGravityThruster();


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

        public void setFont(ref IMyTextSurface lcd)
        {
            lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            lcd.Font = "Monospace";
            lcd.FontSize = 0.6f;
            lcd.FontColor = new Color(100,200,100);
            //lcd.FontColor = Color.PaleGreen;
            //lcd.FontColor = Color.LightGreen;
        }

        static string[] prefixeSI = { "y", "z", "a", "f", "p", "n", "µ", "m", "", "k", "M", "G", "T", "P", "E", "Z", "Y" };
        static public string numSi(double num)
        {
            int log10 = (int)Math.Log10(Math.Abs(num));
            if (log10 < -27)
                return "0.00";
            if (log10 % -3 < 0)
                log10 -= 3;
            int log1000 = Math.Max(-8, Math.Min(log10 / 3, 8));

            return ((double)num / Math.Pow(10, log1000 * 3)).ToString("###.##" + prefixeSI[log1000 + 8]);
        }

        public void PrintLog()
        {
            if (lcd1 == null)
            {
                Echo("LCD not found");
                Echo(outDebug.ToString());
                return;
            }

            float nbLineMax = 17;

            lcd1.WriteText("");

            if (lcd2 == null)
            {
                lcd1.WriteText(outDebug.ToString());
            }
            else
            {
                lcd2.WriteText("");

                var multiLine = outDebug.ToString().Split('\n');
                for (int i = 0; i < multiLine.Length; ++i)
                {
                    if (i < nbLineMax)
                        lcd1.WriteText(multiLine[i] + '\n', true);
                    else
                        lcd2.WriteText(multiLine[i] + '\n', true);

                }
            }

            //Echo(outDebug);

            outDebug.Clear();
        }

        public void LogLn(string str)
        { outDebug.Append(str + "\n"); }

        
        public void LogV3(Vector3 v, string unit)
        {
            LogLn("X : " + v.X + " " + unit);
            LogLn("Y : " + v.Y + " " + unit);
            LogLn("Z : " + v.Z + " " + unit);
        }

        public void LogV3(Vector3D v, string title, string units = "")
        {
            LogLn(title);
            outDebug.Append("X:" + (v.X > 0 ? " " : "") + numSi(v.X) + units);
            outDebug.Append(" Y:" + (v.Y > 0 ? " " : "") + numSi(v.Y) + units);
            LogLn(" Z:" + (v.Z > 0 ? " " : "") + numSi(v.Z) + units);
        }

        public void LogV3Prg(Vector3D v, string title, string units)
        {
            printMsgPgr(title);
            StringBuilder str = new StringBuilder();
            str.Append("X:" + (v.X > 0 ? " " : "") + numSi(v.X) + units);
            str.Append(" Y:" + (v.Y > 0 ? " " : "") + numSi(v.Y) + units);
            printMsgPgr(str.Append(" Z:" + (v.Z > 0 ? " " : "") + numSi(v.Z) + units).ToString());
        }


        public void LogM3(MatrixD m, string title)
        {
            LogLn(title);
            outDebug.Append(numSi(m.M11) + "_" + numSi(m.M12) + "_" + numSi(m.M13) + "\n");
            outDebug.Append(numSi(m.M21) + "_" + numSi(m.M22) + "_" + numSi(m.M23) + "\n");
            outDebug.Append(numSi(m.M31) + "_" + numSi(m.M32) + "_" + numSi(m.M33) + "\n");
        }
        public void LogM3(Matrix m, string title)
        {
            LogLn(title);
            outDebug.Append(m.M11 + "_" + m.M12 + "_" + m.M13 + "\n");
            outDebug.Append(m.M21 + "_" + m.M22 + "_" + m.M23 + "\n");
            outDebug.Append(m.M31 + "_" + m.M32 + "_" + m.M33 + "\n");
        }
        public void LogM3(Matrix3x3 m, string title)
        {
            LogLn(title);
            outDebug.Append(m.M11 + "_" + m.M12 + "_" + m.M13 + "\n");
            outDebug.Append(m.M21 + "_" + m.M22 + "_" + m.M23 + "\n");
            outDebug.Append(m.M31 + "_" + m.M32 + "_" + m.M33 + "\n");
        }

        public void LogThrusters()
        {
            LogLn("X :");
            foreach (MyGravitonThruster ggD in LeftRight_thruster_Bship)
                LogLn(ggD.ToString());
            LogLn("Y :");
            foreach (MyGravitonThruster ggD in UpDown_thruster_Bship)
                LogLn(ggD.ToString());
            LogLn("Z :");
            foreach (MyGravitonThruster ggD in BackForw_thruster_Bship)
                LogLn(ggD.ToString());
        }

        public Vector3D Bcock_2_Bship(ref Vector3D v_Bcock)
        {
            Vector3D v_Bship = new Vector3D();
            Vector3D.Rotate(ref v_Bcock, ref rotation_Bcockpit_2_Bship, out v_Bship);
            return v_Bship;
        }
        public Vector3 Bcock_2_Bship(Vector3 v_Bcock)
        {
            return Vector3.Transform(v_Bcock, rotation_Bcockpit_2_Bship);
        }

        public Vector3D Bship_2_Bcock(ref Vector3D v_Bship)
        {
            Vector3D v_Bcock = new Vector3D();
            Vector3D.Rotate(ref v_Bship, ref rotation_Bship_2_Bcockpit, out v_Bcock);
            return v_Bcock;
        }
        public Vector3 Bship_2_Bcock(Vector3 v_Bship)
        {
            return Vector3.Transform(v_Bship, rotation_Bship_2_Bcockpit);
        }

        public void moveShip()
        {
            Vector3D speed_Bship = cockpit.GetShipVelocities().LinearVelocity;
            Vector3D speed_Bcock = new Vector3D(); 
            Babs_2_Bcockpit = MatrixD.Transpose(cockpit.WorldMatrix);
            Babs_2_Bship = MatrixD.Multiply(Babs_2_Bcockpit, rotation_Bcockpit_2_Bship);

            Vector3D.Rotate(ref speed_Bship, ref Babs_2_Bcockpit, out speed_Bcock);
            Vector3D.Rotate(ref speed_Bship, ref Babs_2_Bship, out speed_Bship);

            LogV3(speed_Bship, "Speed Local ship: m/s");
            LogV3(speed_Bcock, "Speed Local Cockpit: m/s");

            Vector3 direction_Bship;
            Vector3D cockpitInput_Bship = Bcock_2_Bship(cockpit.MoveIndicator);

            LogV3(cockpit.MoveIndicator, "cockpit.MoveIndicator: m/s");
            LogV3(cockpitInput_Bship, "cockpitInput_Bship: m/s");

            speed_Bship = speed_Bcock;

            if (cockpit.DampenersOverride)
            {
                var dampenersMoveIndicator = -speed_Bship / maxSpeedBy10Ticks_Bship_ms_noZero;
                if (dampenersMoveIndicator.AbsMax() > 1)
                    dampenersMoveIndicator /= dampenersMoveIndicator.AbsMax();

                direction_Bship = Bcock_2_Bship(new Vector3(
                   cockpit.MoveIndicator.X == 0 && Math.Abs(speed_Bship.X) > 0.0009 ? dampenersMoveIndicator.X : cockpit.MoveIndicator.X,
                   cockpit.MoveIndicator.Y == 0 && Math.Abs(speed_Bship.Y) > 0.0009 ? dampenersMoveIndicator.Y : cockpit.MoveIndicator.Y,
                   cockpit.MoveIndicator.Z == 0 && Math.Abs(speed_Bship.Z) > 0.0009 ? dampenersMoveIndicator.Z : cockpit.MoveIndicator.Z)
                   );

            }
            else
                direction_Bship = Bcock_2_Bship(cockpit.MoveIndicator);


            if (direction_Bship != lastDirection_Bcock)
            {
                SetPower(direction_Bship);

                lastDirection_Bcock = direction_Bship;
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.

            while(cockpit == null || !cockpit.IsFunctional)
            {
                findCockpit();
                return;
            }

            moveShip();

            //LogV3(cockpit.)
            LogThrusters();
            PrintLog();
            outDebug.Clear();
        }

        public void SetPower(Vector3 Direction_Bship)
        {
            for (int i = 0; i < LeftRight_thruster_Bship.Count; ++i)
                LeftRight_thruster_Bship[i].Thrust = ThrustFactorComposator_Bship_kN[0][i] * Direction_Bship.X;
            for (int i = 0; i < UpDown_thruster_Bship.Count; ++i)
                UpDown_thruster_Bship[i].Thrust = ThrustFactorComposator_Bship_kN[1][i] * Direction_Bship.Y;
            for (int i = 0; i < BackForw_thruster_Bship.Count; ++i)
                BackForw_thruster_Bship[i].Thrust = ThrustFactorComposator_Bship_kN[2][i] * Direction_Bship.Z;
        }

        public interface vec3<T>
        {
            T X { get; set; }
            T Y { get; set; }
            T Z { get; set; }
        }

        [Flags]
        public enum OrientationFlags : byte
        {
            Forward = 1,
            Backward = 2,
            Left = 4,
            Right = 8,
            Up = 16,
            Down = 32,
            LowFlagIsForward = 64
        }


        //public Vector3 aligneOrientation2Bship(Vector3 v, MyBlockOrientation dir)
        //{
        //    OrientationFlags UpFlag = (OrientationFlags)Base6Directions.GetDirectionFlag(dir.Up);
        //    OrientationFlags BackwardFlag = (OrientationFlags)Base6Directions.GetDirectionFlag(Base6Directions.GetOppositeDirection(dir.Forward));
        //    //OrientationFlags ForwardFlag = (OrientationFlags)Base6Directions.GetDirectionFlag(dir.Forward);
        //    //OrientationFlags forwardFlagIslowerThanUpFlag = (OrientationFlags)(Convert.ToByte(ForwardFlag < UpFlag) << 6);
        //    OrientationFlags BackwardFlagIslowerThanUpFlag = (OrientationFlags)(Convert.ToByte(BackwardFlag > UpFlag) << 6);


        //    //OrientationFlags orientation = UpFlag | ForwardFlag | forwardFlagIslowerThanUpFlag;
        //    OrientationFlags orientation = UpFlag | BackwardFlag | BackwardFlagIslowerThanUpFlag;



        //                /*
        //     * We look for the down left part of this table when LowFlagIsForward == true
        //     * 
        //     *                                    |                      Direction.Up             	  |													
        //     *                                    |Forward |Backward|  Left  | Right  |	Up	 |	Down  |
        //     * ===================================|========|========|========|========|========|========|
        //     *                          Forward   |	 	 |	      |-Y -X -Z| Y  X -Z|-X  Y -Z| X -Y -Z|
        //     *                          Backward  |	 	 |		  |-Y  X  Z| Y -X  Z| X  Y  Z|-X -Y  Z|
        //     *      Direction.Forward   Left      |-Z  X -Y|-Z -X  Y|	       |		|-Z  Y  X|-Z -Y -X|
        //     *                          Right	    | Z -X -Y| Z  X  Y|	       |		| Z  Y -X| Z -Y  X|
        //     *                          Up        | X  Z -Y|-X  Z  Y|-Y  Z -X| Y  Z  X|		 |		  |
        //     *                          Down	    |-X -Z -Y| X -Z  Y|-Y -Z  X| Y -Z -X|		 |  	  |
        //    */
        //    switch (orientation)
        //    {
        //        case OrientationFlags.Forward  | OrientationFlags.Left  :   return new Vector3(-v.Y, -v.X, -v.Z);
        //        case OrientationFlags.Forward  | OrientationFlags.Right :   return new Vector3( v.Y,  v.X, -v.Z);
        //        case OrientationFlags.Forward  | OrientationFlags.Up    :   return new Vector3(-v.X,  v.Y, -v.Z);
        //        case OrientationFlags.Forward  | OrientationFlags.Down  :   return new Vector3( v.X, -v.Y, -v.Z);

        //        case OrientationFlags.Backward | OrientationFlags.Left  :   return new Vector3(-v.Y,  v.X,  v.Z);
        //        case OrientationFlags.Backward | OrientationFlags.Right :   return new Vector3( v.Y, -v.X,  v.Z);
        //        case OrientationFlags.Backward | OrientationFlags.Up    :   return new Vector3( v.X,  v.Y,  v.Z);
        //        case OrientationFlags.Backward | OrientationFlags.Down  :   return new Vector3(-v.X, -v.Y,  v.Z);

        //        case OrientationFlags.Left     | OrientationFlags.Up    :   return new Vector3(-v.Z,  v.Y,  v.X);
        //        case OrientationFlags.Left     | OrientationFlags.Down  :   return new Vector3(-v.Z, -v.Y, -v.X);

        //        case OrientationFlags.Right    | OrientationFlags.Up    :   return new Vector3( v.Z,  v.Y, -v.X);
        //        case OrientationFlags.Right    | OrientationFlags.Down  :   return new Vector3( v.Z, -v.Y,  v.X);

        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Left  | OrientationFlags.Forward  : return new Vector3(-v.Z,  v.X, -v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Left  | OrientationFlags.Backward : return new Vector3(-v.Z, -v.X,  v.Y);

        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Right | OrientationFlags.Forward  : return new Vector3( v.Z, -v.X, -v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Right | OrientationFlags.Backward : return new Vector3( v.Z,  v.X,  v.Y);

        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Up    | OrientationFlags.Forward  : return new Vector3( v.X,  v.Z, -v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Up    | OrientationFlags.Backward : return new Vector3(-v.X,  v.Z,  v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Up    | OrientationFlags.Left     : return new Vector3(-v.Y,  v.Z, -v.X);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Up    | OrientationFlags.Right    : return new Vector3( v.Y,  v.Z,  v.X);

        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Down  | OrientationFlags.Forward  : return new Vector3(-v.X, -v.Z, -v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Down  | OrientationFlags.Backward : return new Vector3( v.X, -v.Z,  v.Y);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Down  | OrientationFlags.Left     : return new Vector3(-v.Y, -v.Z,  v.X);
        //        case OrientationFlags.LowFlagIsForward | OrientationFlags.Down  | OrientationFlags.Right    : return new Vector3( v.Y, -v.Z, -v.X);

        //        default:
        //            return new Vector3();

        //    }
        //}

    }
}