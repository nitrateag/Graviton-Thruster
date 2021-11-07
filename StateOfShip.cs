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
        /*
         *  Contain all data needed to move the ship 
         * 
         */

        private enum KeepReducing { accept, reject, needToSeeFurther }
        public class StateOfShip
        {
            public bool isReadyToUse = false;
            public int nbStepUsedToCompute = 0;

            public float shipMass;

            MyGridProgram pgr;

            public StateOfShip(MyGridProgram gridProg)
            {
                pgr = gridProg;

                LeftRight_thruster_Bship = new List<MyGravitonThruster>(12);
                UpDown_thruster_Bship = new List<MyGravitonThruster>(12);
                BackForw_thruster_Bship = new List<MyGravitonThruster>(12);

            }
            //Maximum thrust available on each side
            public Vector3D maximumThrustPerSide_Bship_kN_noZero;
            public Vector3 maximumThrustPerSide_Bcock_kN;
            //Maximum speed gained every 10 Ticks
            public Vector3D maxSpeedBy10Ticks_Bship_ms_noZero;

            //The direction of gravity thruster are exprimed in base of ship grid
            public List<MyGravitonThruster> BackForw_thruster_Bship;
            public List<MyGravitonThruster> LeftRight_thruster_Bship;
            public List<MyGravitonThruster> UpDown_thruster_Bship;


            public List<AdvanceCockpit> m_arrCockpit = null;

            public Vector3D centerOfMass_Bship;

            public float[][] ThrustFactorComposator_Bship_kN;

            // strLog is for user information 
            StringBuilder strLog = new StringBuilder();
            // strDebug is for software developer information, seen if USE_DEBUG = true 
            StringBuilder strDebug = new StringBuilder();




            // Manage the computation of the new state of ship
            public IEnumerator<string> ComputeNewStateMachine_OverTime(int nbStepsPerTikcs)
            {
                strLog.Clear().Append("::GRAVITY THRUSTER::");
                isReadyToUse = false;

                if (!findCockpit())
                    yield break;// it's impossible to continue without cockpits, so we wait to have one

                LeftRight_thruster_Bship.Clear();
                UpDown_thruster_Bship.Clear();
                BackForw_thruster_Bship.Clear();


                //  findAllGravityThruster

                IEnumerable<string> findAllGravitonThruster = FindAllGravitonThruster();

                foreach (string computInfo in findAllGravitonThruster)
                {
                    yield return computInfo;
                }



                // find best power balance

                IEnumerator<bool> SimplexNeedMoreComputationTime = LaunchSimplexComputation(nbStepsPerTikcs);

                while (SimplexNeedMoreComputationTime.MoveNext())
                    yield return ""; // ("Compute best power balance\n(torque compensator)"); //Do a pause
                


                SimplexNeedMoreComputationTime.Dispose();

            }

            public IEnumerable<string> FindAllGravitonThruster()
            {
                //Update all gravity thruters and compute news optimal coefficients

                //Search all thruster component
                IMyBlockGroup allThruster = pgr.GridTerminalSystem.GetBlockGroupWithName(GROUP_NAME_OF_THRUSTER_COMPONENT);
                List<IMyVirtualMass> allGravityMass = new List<IMyVirtualMass>(200);
                List<IMyGravityGenerator> allGravityGen = new List<IMyGravityGenerator>(50);

                if (allThruster != null)
                {
                    allThruster.GetBlocksOfType(allGravityGen, gravGen => gravGen.IsFunctional);
                    allThruster.GetBlocksOfType(allGravityMass, mass => mass.IsFunctional);
                }

                if (allGravityGen.Count == 0)
                    pgr.GridTerminalSystem.GetBlocksOfType(allGravityGen, gravGen => gravGen.IsFunctional);

                if (allGravityMass.Count == 0)
                    pgr.GridTerminalSystem.GetBlocksOfType(allGravityMass, mass => mass.IsFunctional);

                if (allGravityMass.Count == 0 || allGravityGen.Count == 0)
                {
                    LogError($"We didn't found yours thruster component :\n - functional gravity generators found = {allGravityGen.Count}\n - functional artificial masses found = {allGravityMass.Count}\nTry to set all yours gravity thrusters component in a same group \"{GROUP_NAME_OF_THRUSTER_COMPONENT}\"");
                    yield break;
                }


                // Fit gravity feild on thruster shape
                if (firstCompute && AUTO_FIT_GRAVITY_FEILD)
                {
                    // pgr.Me.CustomData = "";
                    float fillingLimit = FILLING_OF_GRAVITY_FEILD < 1f ? 1f : (FILLING_OF_GRAVITY_FEILD > 100f ? 100f : FILLING_OF_GRAVITY_FEILD);
                    int nbGravityGen = allGravityGen.Count, currentGG = 0;

                    List<IMyVirtualMass> massBag = new List<IMyVirtualMass>(allGravityMass);
                    List<IMyGravityGenerator> ggBag = new List<IMyGravityGenerator>(allGravityGen);

                    List<int> arrDistToMass_axe = new List<int>(massBag.Count);
                    List<int> arrDistToGG_axe = new List<int>(ggBag.Count);

                    foreach (IMyGravityGenerator gg in allGravityGen)
                    {

                        ++currentGG;
                        // pgr.Me.CustomData += "\n-----------currentGG :  " + currentGG;

                        Vector3I currentGravityFeildPave = new Vector3I(1, 1, 1);
                        Vector3I currentGravityFeild = new Vector3I(1, 1, 1);
                        Vector3I oneStep = new Vector3I(1, 1, 1);

                        var Xgg = gg.Position.X;
                        var Ygg = gg.Position.Y;
                        var Zgg = gg.Position.Z;

                        int nbMass = 0;
                        int nbGg = 0;

                        // We will increase each gravity feild direction one by one, and keep incresing 
                        // if the new gravity feild is still enought filled. (see FILLING_OF_GRAVITY_FEILD variable)
                        // We stop the search when no one direction can be increased
                        byte nbAxesToTest = 3;
                        int _axe = 2;

                        int nbTry = 0;

                        //While there is some axes of gravity feild to increase
                        while (nbAxesToTest-- > 0)
                        {
                            _axe = (++_axe) % 3;

                            // pgr.Me.CustomData += "\n-----------------------------------------\n\n nbAxesToTest : " + (nbAxesToTest+1);
                            // pgr.Me.CustomData += "\n _axe : " + _axe;

                            int _horz1 = (_axe + 1) % 3, _horz2 = (_axe + 2) % 3;
                            int gravityFeild_horz1 = currentGravityFeild[_horz1], gravityFeild_horz2 = currentGravityFeild[_horz2];
                            int gravityFeildPave_horz1 = currentGravityFeildPave[_horz1], gravityFeildPave_horz2 = currentGravityFeildPave[_horz2];
                            int nextGravityFeild_axe = currentGravityFeild[_axe];
                            int ggPos1 = gg.Position[_horz1], ggPos2 = gg.Position[_horz2], ggPosAxe = gg.Position[_axe];


                            arrDistToMass_axe.Clear();
                            foreach (var mass in massBag)
                            {
                                if (Math.Abs(ggPos1 - mass.Position[_horz1]) < gravityFeild_horz1 &&
                                    Math.Abs(ggPos2 - mass.Position[_horz2]) < gravityFeild_horz2)
                                {
                                    arrDistToMass_axe.Add(Math.Abs(mass.Position[_axe] - ggPosAxe)+1);
                                }
                            }
                            // pgr.Me.CustomData += "\n arrAxeDistMass.Count : " + arrDistToMass_axe.Count;

                            if (arrDistToMass_axe.Count > 0)
                            {
                                arrDistToGG_axe.Clear();
                                foreach (var gg2 in ggBag)
                                {
                                    if (Math.Abs(ggPos1 - gg2.Position[_horz1]) < gravityFeild_horz1 &&
                                        Math.Abs(ggPos2 - gg2.Position[_horz2]) < gravityFeild_horz2)
                                    {
                                        arrDistToGG_axe.Add(Math.Abs(gg2.Position[_axe] - ggPosAxe)+1);
                                    }
                                }

                                arrDistToMass_axe.Sort();
                                arrDistToGG_axe.Sort();

                                // pgr.Me.CustomData += "\n arrAxeDistMass : ";
                                // arrDistToMass_axe.ForEach(dist => pgr.Me.CustomData += dist + ",");
                                // pgr.Me.CustomData += "\n arrDistToGG_axe : " ;
                                // arrDistToGG_axe.ForEach(dist => pgr.Me.CustomData += dist + ",");


                                nbGg = arrDistToGG_axe.Count;
                                nbMass = arrDistToMass_axe.Count;

                                while (nbMass > 0)
                                {

                                    // ((X-1)*2+1) * ((X-1)*2+1) = (2X -1)*(2Y - 1) = 4XY - 2X - 2Y +1
                                    nextGravityFeild_axe = arrDistToMass_axe[nbMass - 1];

                                    while (nbGg > 0 && arrDistToGG_axe[nbGg - 1] > nextGravityFeild_axe)
                                        --nbGg;


                                    var nextVolume = gravityFeildPave_horz1 * gravityFeildPave_horz2 * (nextGravityFeild_axe * 2 - 1);

                                    float filling = (100f * nbMass) / (nextVolume - nbGg);


                                    if (filling >= fillingLimit)
                                        break;

                                    --nbGg;
                                    while (nbMass > 0 && arrDistToMass_axe[nbMass - 1] == nextGravityFeild_axe)
                                        --nbMass;

                                } 


                            }

                            //We need to test the 2 next axes to be sure there is not better feild
                            if (nextGravityFeild_axe != currentGravityFeild[_axe])
                            {
                                nbAxesToTest = 2;

                                currentGravityFeild[_axe] = nextGravityFeild_axe;
                                currentGravityFeildPave[_axe] = nextGravityFeild_axe * 2 - 1;
                                //pgr.Me.CustomData += "\n new SIZE ! : " + nextGravityFeild_axe + ", " + currentGravityFeildPave[_axe];

                            }

                            #region old
                            //var axeMassBag = massBag.FindAll(mass =>
                            //    Math.Abs(ggPos1 - mass.Position[horz1]) < nextGravityFeildHorz1 &&
                            //    Math.Abs(ggPos2 - mass.Position[horz2]) < nextGravityFeildHorz2
                            //);





                            /*
                            KeepIncreasing increaseStep;
                            nextGravityFeild[axe] += oneStep[axe];
                            nextGravityFeildPave[axe] += oneStep[axe] * 2;

                            var nextVolume = nextGravityFeildPave.X * nextGravityFeildPave.Y * nextGravityFeildPave.Z;

                            //Counting of nb mass are added with this new gravity feild

                            //to do that, we count all graviton thruster ellement who are -> NOT <- in the gravity feild
                            int ggWidth = nextGravityFeild.X, ggHeight = nextGravityFeild.Y, ggLenght = nextGravityFeild.Z;
                            var smallerMassBag = massBag.FindAll(mass =>
                                Math.Abs(Xgg - mass.Position.X) >= ggWidth ||
                                Math.Abs(Ygg - mass.Position.Y) >= ggHeight ||
                                Math.Abs(Zgg - mass.Position.Z) >= ggLenght
                            );
                            var nbNewMass = massBag.Count - smallerMassBag.Count;

                            if (ARTIFICIAL_MASS_MUST_BE_CONNEX && nbNewMass == 0)
                            {
                                //if we have not found new mass, and if we cannot go threw no mass block, we refuse the increasing
                                increaseStep = KeepIncreasing.reject;

                            }
                            else
                            {
                                nbNextMass = nbMass + nbNewMass;
                                float filling = (100f * nbNextMass) / (nextVolume - nbGg);

                                //if we reach the filling limit, we substract the number of gravity generator and look again the fillingLimit
                                if (filling < fillingLimit)
                                {
                                    smallerGgBag = ggBag.FindAll(ggForeign =>
                                        Math.Abs(Xgg - ggForeign.Position.X) >= ggWidth ||
                                        Math.Abs(Ygg - ggForeign.Position.Y) >= ggHeight ||
                                        Math.Abs(Zgg - ggForeign.Position.Z) >= ggLenght
                                    );
                                    nbNextGg = nbGg + ggBag.Count - smallerGgBag.Count;
                                    filling = (100f * nbNextMass) / (nextVolume - nbNextGg);
                                }

                                // we decrease the filling limite at beginning
                                startingFillingLimit[axe] = (int) ( fillingLimit * ((100f * nextGravityFeildPave[axe]-2) / nextGravityFeildPave[axe]));


                                if (filling < startingFillingLimit[axe] / 100f)
                                    increaseStep = KeepIncreasing.reject;
                                else if (nbNewMass == 0 || filling < fillingLimit)
                                    increaseStep = KeepIncreasing.needToSeeFurther;
                                else
                                    increaseStep = KeepIncreasing.accept;
                            }

                            switch (increaseStep)
                            {
                                case KeepIncreasing.accept:
                                    atLeastOneAxisHasIncrease = true;
                                    currentGravityFeildPave = nextGravityFeildPave;

                                    massBag = smallerMassBag;
                                    nbMass = nbNextMass;

                                    ggBag = smallerGgBag;
                                    nbGg = nbNextGg;

                                    oneStep[axe] = 1;

                                    break;
                                case KeepIncreasing.reject:
                                    nextGravityFeild[axe] -= oneStep[axe];
                                    nextGravityFeildPave[axe] -= oneStep[axe] * 2;
                                    oneStep[axe] = 1;

                                    break;
                                case KeepIncreasing.needToSeeFurther:
                                    nextGravityFeild[axe] -= oneStep[axe];
                                    nextGravityFeildPave[axe] -= oneStep[axe] * 2;
                                    oneStep[axe] += 1;
                                    atLeastOneAxisHasIncrease = true;

                                    break;
                            }

                            */

                            #endregion

                            if (++nbTry % 20 == 0)
                                yield return ("AUTO_FIT_GRAVITY_FEILD\nGenerator " + currentGG + "/" + nbGravityGen); //Do a pause
                        }

                        Vector3 orienttedGravityFeild = new Vector3();

                        Matrix Bship_2_BGravityGenerator = default(Matrix);

                        gg.Orientation.GetMatrix(out Bship_2_BGravityGenerator);

                        Vector3 currentGravityFeildFloat = currentGravityFeildPave;
                        Bship_2_BGravityGenerator =  Matrix.Transpose(Bship_2_BGravityGenerator);

                        Vector3.RotateAndScale(ref currentGravityFeildFloat, ref Bship_2_BGravityGenerator, out orienttedGravityFeild);

                        orienttedGravityFeild.X = Math.Abs(orienttedGravityFeild.X) * 2.5f;
                        orienttedGravityFeild.Y = Math.Abs(orienttedGravityFeild.Y) * 2.5f;
                        orienttedGravityFeild.Z = Math.Abs(orienttedGravityFeild.Z) * 2.5f;

                        gg.FieldSize = orienttedGravityFeild;


                        gg.Orientation.GetMatrix(out Bship_2_BGravityGenerator);
                        yield return ("AUTO_FIT_GRAVITY_FEILD\nGenerator " + currentGG + "/" + nbGravityGen + "\n"); //Do a pause

                    }

                    firstCompute = false;
                }


                List<SharedMass> allShrGravityMass = new List<SharedMass>();
                allGravityMass.ForEach(mass => allShrGravityMass.Add(new SharedMass(mass)));



                //Sort gravity component

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

                if (RENAME_GRAVITY_GENERATOR)
                {
                    yield return ("");
                    RenameGravityGenerator();
                }
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

            #region computeShipState
            //find the good cockpit
            bool findCockpit()
            {
                List<IMyCockpit> allCockpit = new List<IMyCockpit>();

                pgr.GridTerminalSystem.GetBlocksOfType(allCockpit, cockpit => cockpit.CanControlShip && cockpit.ControlThrusters && cockpit.IsWorking);
                m_arrCockpit = new List<AdvanceCockpit>();

                if (allCockpit.Count == 0)
                {
                    LogError("No cockit who can control thrusters found. Look about Owner of the Programmable block.");
                    if(pgr.Me.OwnerId == 0)
                        LogMsg("WARNING ! Your programable block has \"Nobody\" owner. Try to set Programmable block on same owner as your cockpit, or set also your cockpit on \"Nobody\" owner");

                    pgr.GridTerminalSystem.GetBlocksOfType(allCockpit);
                    allCockpit.ForEach(cock => strLog.AppendLine().Append(cock.CustomName + ": ").Append(cock.CanControlShip ? "":"\ncan't controll ship").Append(cock.ControlThrusters ? "": "\ncan't controll thrusters").Append(cock.IsWorking ? "": "\nIs power off"));

                    return false;
                }
                else
                { //If there is at least 1 cockpits

                    IMyCockpit mainCockpit = allCockpit.FirstOrDefault(cockpit => cockpit.IsMainCockpit);
                    if (mainCockpit == null)
                    {   //If there is no "Main Cockpit", we take alls of them
                        allCockpit.ForEach(cockpit => m_arrCockpit.Add(new AdvanceCockpit(cockpit)));
                    }
                    else
                    {  //Else we take only the main cockpit
                        m_arrCockpit.Add(new AdvanceCockpit(mainCockpit));
                    }
                }
                return true;
            }


            //return true untile the computation of news optimal coefficients isn't finished 
            IEnumerator<bool> LaunchSimplexComputation(int nbStepPerTicks)
            {
                //Recording of distances :
                centerOfMass_Bship = m_arrCockpit[0].m_cockpit.CenterOfMass - pgr.Me.CubeGrid.GetPosition(); //ship's center of mass in the base of ship but oriented in Absolute/World base
                Vector3D.Rotate(ref centerOfMass_Bship, ref Babs_2_Bship, out centerOfMass_Bship);


                TorqueComposatorCalculator torqueComposator = new TorqueComposatorCalculator();


                torqueComposator.setThrusters(LeftRight_thruster_Bship, UpDown_thruster_Bship, BackForw_thruster_Bship, centerOfMass_Bship);
                yield return true;

                StringBuilder debugSimplex = new StringBuilder();
                IEnumerator<bool> SimplexNeedMoreComputeTime = torqueComposator.ComputeSolution(nbStepPerTicks, debugSimplex, USE_DEBUG);

                while (SimplexNeedMoreComputeTime.MoveNext())
                    yield return true;

                isReadyToUse = torqueComposator.success;
                if (!isReadyToUse)
                {
                    if (USE_DEBUG)
                    {
                        LogError("Cannot compute thruster balance, see custom data of program bloc for more info");

                        pgr.Me.CustomData = debugSimplex.ToString();
                    }
                    else
                    {
                        LogError("Cannot compute thruster balance.");
                        LogMsg("\nSet 'const bool USE_DEBUG = true' on the top of script and recompile to see more info");
                        pgr.Me.CustomData = strLog.ToString();
                    }


                    SimplexNeedMoreComputeTime.Dispose();

                    yield break;
                }


                shipMass = m_arrCockpit[0].m_cockpit.CalculateShipMass().TotalMass;


                ThrustFactorComposator_Bship_kN = torqueComposator.OptimalThrustPowerPerThruster_kN;


                // we set float.MaxValue by default to counter some division by 0
                maximumThrustPerSide_Bship_kN_noZero = new Vector3D(
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[0] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[1] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[2] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[2]
                    );
                maxSpeedBy10Ticks_Bship_ms_noZero = (maximumThrustPerSide_Bship_kN_noZero * 1000) / (shipMass * 6); // *6 because ther is 60 ticks pers second, so each 10Ticks is 1/6 seconds

                //Send thrust characteristics to user
                maximumThrustPerSide_Bcock_kN = m_arrCockpit[0].Bship_2_Bcock(new Vector3(torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                                                                torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                                                                torqueComposator.sumOptimalThrustPowerPerSide_kN[2]));

                logPerformances();


                SimplexNeedMoreComputeTime.Dispose();
            }
            #endregion

            public void RenameGravityGenerator()
            {

                if (RENAME_GRAVITY_GENERATOR)
                {
                    //LeftRight
                    var LR = m_arrCockpit[0].Bship_2_Bcock(new Vector3(1, 0, 0));
                    var UD = m_arrCockpit[0].Bship_2_Bcock(new Vector3(0, 1, 0));
                    var FB = m_arrCockpit[0].Bship_2_Bcock(new Vector3(0, 0, 1));

                    int i = 0;
                    if (LR.X != 0)
                        LeftRight_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen LR{i++}");
                    else if (LR.Y != 0)
                        LeftRight_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen UD{i++}");
                    else
                        LeftRight_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen FB{i++}");

                    i = 0;
                    if (UD.X != 0)
                        UpDown_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen LR{i++}");
                    else if (UD.Y != 0)
                        UpDown_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen UD{i++}");
                    else
                        UpDown_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen FB{i++}");

                    i = 0;
                    if (FB.X != 0)
                        BackForw_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen LR{i++}");
                    else if (FB.Y != 0)
                        BackForw_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen UD{i++}");
                    else
                        BackForw_thruster_Bship.ForEach(gravThr => gravThr.m_gravGen.CustomName = $"GravGen FB{i++}");
                }
            }

            #region logTools

            public void LogError(string errorMsg)
            {
                LogMsg("\nERROR: " + errorMsg);
            }


            // Print a message to user on Programmable block main screen
            public void LogMsg(string msg)
            {
                var maxCharPerLine = 43;
                int lastSpace = 0;
                int lastPrint = 0;
                for(int i=0; lastPrint + maxCharPerLine < msg.Length; ++i)
                {
                    if (msg[lastPrint + i] == '\n')
                    {
                        strLog.AppendLine(msg.Substring(lastPrint, i+1));
                        lastPrint += i+1;
                        i = 0;
                        continue;
                    }
                    else if (msg[lastPrint + i] == ' ')
                        lastSpace = i;

                    if (i >= maxCharPerLine)
                    {
                        strLog.AppendLine(msg.Substring(lastPrint, lastSpace+1));
                        lastPrint += lastSpace+1;
                        i -= lastSpace;
                    }

                }
                strLog.AppendLine(msg.Substring(lastPrint));
            }
            public void LogV3(Vector3D v, string title, string units)
            {
                var str = new StringBuilder(title).AppendLine();

                str.Append("X:" + (v.X > 0 ? " " : "") + numSi(v.X) + units);
                str.Append(" Y:" + (v.Y > 0 ? " " : "") + numSi(v.Y) + units);
                str.Append(" Z:" + (v.Z > 0 ? " " : "") + numSi(v.Z) + units);
                LogMsg(str.ToString());
            }

            void logPerformances()
            {
                strLog.Append($" Reset every {nbStepUsedToCompute / 6}sec\n");
                if (m_arrCockpit.Count > 1)
                    LogMsg("(X,Y,Z) seen from cockpit '" + m_arrCockpit[0].m_cockpit.CustomName + "'\n");

                LogV3(Vector3.Abs(maximumThrustPerSide_Bcock_kN * 1000 / shipMass), "Maximum Acceleration :", "m/s²");
                LogV3(Vector3.Abs(maximumThrustPerSide_Bcock_kN * 1000), "Maximum Thrust :", "N");

                Vector3 theoricMaximumThrust_Bship = new Vector3(LeftRight_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                                 UpDown_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                                 BackForw_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN));

                Vector3 thrusterPosition_efficiency_Bship = new Vector3(
                    theoricMaximumThrust_Bship.X == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.X / theoricMaximumThrust_Bship.X,
                    theoricMaximumThrust_Bship.Y == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Y / theoricMaximumThrust_Bship.Y,
                    theoricMaximumThrust_Bship.Z == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Z / theoricMaximumThrust_Bship.Z);

                LogV3(Vector3.Abs(m_arrCockpit[0].Bship_2_Bcock(thrusterPosition_efficiency_Bship)), "Position Efficiency :", "%");
            }
            public override string ToString()
            {
                return strLog.ToString();
            }
            #endregion
            #region debugTools

            public void DebugLn(string str)
            { strDebug.Append(str + "\n"); }


            public void DebugV3(Vector3 v, string title, string units = "")
            {
                DebugLn(title);
                strDebug.Append("X:" + (v.X > 0 ? " " : "") + numSi(v.X) + units);
                strDebug.Append(" Y:" + (v.Y > 0 ? " " : "") + numSi(v.Y) + units);
                DebugLn(" Z:" + (v.Z > 0 ? " " : "") + numSi(v.Z) + units);
            }

            public void DebugM3(MatrixD m, string title)
            {
                DebugLn(title);
                strDebug.Append(numSi(m.M11) + "_" + numSi(m.M12) + "_" + numSi(m.M13) + "\n");
                strDebug.Append(numSi(m.M21) + "_" + numSi(m.M22) + "_" + numSi(m.M23) + "\n");
                strDebug.Append(numSi(m.M31) + "_" + numSi(m.M32) + "_" + numSi(m.M33) + "\n");
            }
            public void DebugM3(Matrix m, string title)
            {
                DebugLn(title);
                strDebug.Append(m.M11 + "_" + m.M12 + "_" + m.M13 + "\n");
                strDebug.Append(m.M21 + "_" + m.M22 + "_" + m.M23 + "\n");
                strDebug.Append(m.M31 + "_" + m.M32 + "_" + m.M33 + "\n");
            }
            public void DebugM3(Matrix3x3 m, string title)
            {
                DebugLn(title);
                strDebug.Append(m.M11 + "_" + m.M12 + "_" + m.M13 + "\n");
                strDebug.Append(m.M21 + "_" + m.M22 + "_" + m.M23 + "\n");
                strDebug.Append(m.M31 + "_" + m.M32 + "_" + m.M33 + "\n");
            }

            public void DebugThrusters()
            {
                StringBuilder[] str3Debug_Bship = new StringBuilder[3];

                str3Debug_Bship[0] = new StringBuilder();
                foreach (MyGravitonThruster ggD in LeftRight_thruster_Bship)
                    str3Debug_Bship[0].Append(ggD.ToString() + "\n");

                str3Debug_Bship[1] = new StringBuilder();
                foreach (MyGravitonThruster ggD in UpDown_thruster_Bship)
                    str3Debug_Bship[1].Append(ggD.ToString() + "\n");

                str3Debug_Bship[2] = new StringBuilder();
                foreach (MyGravitonThruster ggD in BackForw_thruster_Bship)
                    str3Debug_Bship[2].Append(ggD.ToString() + "\n");

                m_arrCockpit.ForEach(advCock => advCock.DebugThrusters(str3Debug_Bship));


                return;
            }

            public void PrintDebug()
            {
                if (!USE_DEBUG)
                    return;

                m_arrCockpit.ForEach(advCock => advCock.PrintDebug(strDebug, strLog));
                strDebug.Clear();
            }
            #endregion
        }
    }
}
