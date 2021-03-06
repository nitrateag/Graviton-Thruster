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


        #region mdk preserve

        //Name of group to filter thruster's components, if needed
        const string FILTER_GRAVITY_COMPONENTS = "Gravity Thruster";

        //Number of steps to compute  every 10 ticks for the Simplex algorithms
        //High number increase rate of script updating, but can decrease game performances. 
        //Too higher number can block the script if you are using a LOT OF gravity generator
        const int NB_SIMPLEX_STEPS_PER_TICKS = 20;

        //enable optimisation tools
        const bool USE_OPTYM_TOOLS = false;

        //rename gravity generator to easily find it (need USE_OPTYM_TOOLS = true;)
        //(LR = LeftRigh, UD = UpDown, FB = ForwardBackward)
        const bool RENAME_GRAVITY_GENERATOR = false;

        //Fit the sithe of gravity generator feild to perfect match a multiple of 2.5m
        const bool FIT_GRAVITY_FEILD = false;

        //Display every thrusters on cockpit LCD
        const bool USE_DEBUG = true;
        //
        //
        //
        const bool uselessLine = true;
        #endregion




        StateOfShip[] stateOfShip;
        int idCurrentStateOfShip;
        int idNextStateOfShip;

        IEnumerator<bool> NewStateOfShipNeedMoreComputeTime;
        public Vector3 lastDirection_Bship = new Vector3(0, 0, 0);


        public static MatrixD Babs_2_Bship; //Matrice to change a vector in base absolute to base of cockpit

        public Program()
        {

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


            setFont(Me.GetSurface(0));
            setFontBIG(Me.GetSurface(1));

            Me.GetSurface(0).WriteText("::GRAVITY THRUSTER::");

            stateOfShip = new StateOfShip[2];
            stateOfShip[0] = new StateOfShip(this);
            stateOfShip[1] = new StateOfShip(this);
            //stateOfShip[0] = new StateOfShip(this);
            idCurrentStateOfShip = 0;
            idNextStateOfShip = 1;

            //nexStateOfShip = new StateOfShip(this);

            NewStateOfShipNeedMoreComputeTime = stateOfShip[idNextStateOfShip].ComputeNewStateMachine_OverTime(NB_SIMPLEX_STEPS_PER_TICKS);

            //PrintLog();
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

        #region fancyTools
        public static void setFont(IMyTextSurface lcd)
        {
            lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            lcd.Font = "Monospace";
            lcd.FontSize = 0.6f;
            lcd.FontColor = new Color(100,200,100);
            //lcd.FontColor = Color.PaleGreen;
            //lcd.FontColor = Color.LightGreen;
        }
        public static void setFontBIG(IMyTextSurface lcd)
        {
            setFont(lcd);
            lcd.FontSize = 2f;
        }

        static string[] prefixeSI = { "y", "z", "a", "f", "p", "n", "µ", "m", "", "k", "M", "G", "T", "P", "E", "Z", "Y" };
        public static string numSi(double num)
        {
            int log10 = (int)Math.Log10(Math.Abs(num));
            if (log10 < -27)
                return "0.00";
            if (log10 % -3 < 0)
                log10 -= 3;
            int log1000 = Math.Max(-8, Math.Min(log10 / 3, 8));

            return ((double)num / Math.Pow(10, log1000 * 3)).ToString("###.##" + prefixeSI[log1000 + 8]);
        }


        int waiting = 0;
        string setUserWaiting()
        {
            waiting %= 3;
            switch (++waiting)
            {
                case 1:
                    return " .";
                case 2:
                    return " ..";
                default:
                    return " ...";
            }

        }

        #endregion




        public void moveShip(ref StateOfShip ship)
        {
            Vector3D speed_Bship = ship.m_arrCockpit[0].m_cockpit.GetShipVelocities().LinearVelocity;
            Vector3D.Rotate(ref speed_Bship, ref Babs_2_Bship, out speed_Bship);


            if(USE_DEBUG)
                ship.m_arrCockpit.ForEach(advCock => advCock.DebugSpeed(ref speed_Bship));


            Vector3 allCockpitInput_Bship = new Vector3(0, 0, 0);
            ship.m_arrCockpit.ForEach(advCock => allCockpitInput_Bship += advCock.getMoveIndicator_Bship());

            if(allCockpitInput_Bship.AbsMax() > 1)
                allCockpitInput_Bship /= allCockpitInput_Bship.AbsMax();

            Vector3 direction_Bship;
            if (ship.m_arrCockpit[0].m_cockpit.DampenersOverride)
            {
                var dampenersMoveIndicator = -speed_Bship / ship.maxSpeedBy10Ticks_Bship_ms_noZero;
                if (dampenersMoveIndicator.AbsMax() > 1)
                    dampenersMoveIndicator /= dampenersMoveIndicator.AbsMax();

                direction_Bship = new Vector3(
                   allCockpitInput_Bship.X == 0 && Math.Abs(speed_Bship.X) > 0.0009 ? dampenersMoveIndicator.X : allCockpitInput_Bship.X,
                   allCockpitInput_Bship.Y == 0 && Math.Abs(speed_Bship.Y) > 0.0009 ? dampenersMoveIndicator.Y : allCockpitInput_Bship.Y,
                   allCockpitInput_Bship.Z == 0 && Math.Abs(speed_Bship.Z) > 0.0009 ? dampenersMoveIndicator.Z : allCockpitInput_Bship.Z);


            }
            else
                direction_Bship = allCockpitInput_Bship;

            if (direction_Bship != lastDirection_Bship)
            {
                ship.SetPower(direction_Bship);

                lastDirection_Bship = direction_Bship;
            }
        }


        int currentTik = 0;

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
            ++currentTik;
            Babs_2_Bship = MatrixD.Transpose(Me.CubeGrid.WorldMatrix); //Need to be actualized every time because it follow the ship orientation in world base

            if (stateOfShip[idCurrentStateOfShip].isReadyToUse)
            {
                moveShip(ref stateOfShip[idCurrentStateOfShip]);

                if (USE_DEBUG)
                {
                    stateOfShip[idCurrentStateOfShip].DebugLn("CurrentTick : " + currentTik);
                    stateOfShip[idCurrentStateOfShip].DebugThrusters();
                    stateOfShip[idCurrentStateOfShip].PrintDebug();
                }


                double progess = Math.Round(currentTik * 10d / stateOfShip[idCurrentStateOfShip].nbStepUsedToCompute);
                StringBuilder str = new StringBuilder("[");

                for (int i = 0; i <= 10; ++i)
                {
                    if (i < progess)
                        str.Append("\u25A0");
                    else
                        str.Append("-");
                }

                Me.GetSurface(1).WriteText(str.Append($"] {(stateOfShip[idCurrentStateOfShip].nbStepUsedToCompute - currentTik) / 6}sec\n").ToString());
            }
            else
            {
                Me.GetSurface(1).WriteText("First computing" + setUserWaiting());
            }


            if (!NewStateOfShipNeedMoreComputeTime.MoveNext())
            {
                //Compute finished !

                if (stateOfShip[idNextStateOfShip].isReadyToUse)
                {
                    //lastDirection_Bship = new Vector3();

                    idCurrentStateOfShip = idNextStateOfShip;
                    idNextStateOfShip = (idCurrentStateOfShip + 1) % 2;
                    stateOfShip[idCurrentStateOfShip].nbStepUsedToCompute = currentTik;

                    Me.GetSurface(0).WriteText(stateOfShip[idCurrentStateOfShip].ToString());

                    NewStateOfShipNeedMoreComputeTime.Dispose();
                    NewStateOfShipNeedMoreComputeTime = stateOfShip[idNextStateOfShip].ComputeNewStateMachine_OverTime(NB_SIMPLEX_STEPS_PER_TICKS);
                }
                else
                {
                    Me.GetSurface(0).WriteText(stateOfShip[idNextStateOfShip].ToString());
                    Me.GetSurface(1).WriteText(Me.GetSurface(1).GetText() + setUserWaiting());
                    Echo(Me.GetSurface(0).GetText());
                };

                NewStateOfShipNeedMoreComputeTime.Dispose();
                NewStateOfShipNeedMoreComputeTime = stateOfShip[idNextStateOfShip].ComputeNewStateMachine_OverTime(NB_SIMPLEX_STEPS_PER_TICKS);

                currentTik = 0;
            }


        }

        //[Flags]
        //public enum OrientationFlags : byte
        //{
        //    Forward = 1,
        //    Backward = 2,
        //    Left = 4,
        //    Right = 8,
        //    Up = 16,
        //    Down = 32,
        //    LowFlagIsForward = 64
        //}


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