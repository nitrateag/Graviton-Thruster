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


            public List<AdvanceCockpit> advCockpit = null;

            public Vector3D centerOfMass_Bship;

            public float[][] ThrustFactorComposator_Bship_kN;

            // strLog is for user information 
            StringBuilder strLog = new StringBuilder();
            // strDebug is for software developer information, seen if USE_DEBUG = true 
            StringBuilder strDebug = new StringBuilder();




            // Manage the computation of the new state of ship
            public IEnumerator<bool> ComputeNewStateMachine_OverTime(int nbStepsPerTikcs)
            {
                strLog.Clear().Append("::GRAVITY THRUSTER::");
                isReadyToUse = false;

                if (!findCockpit())
                    yield break;// it's impossible to continue without cockpits, so we wait to have one

                LeftRight_thruster_Bship.Clear();
                UpDown_thruster_Bship.Clear();
                BackForw_thruster_Bship.Clear();

                if (findAllGravityThruster())
                    yield return true; //Do a pause
                else
                    yield break;// If we fail to found thrusters, we retry from beginings

                IEnumerator<bool> SimplexNeedMoreComputationTime = LaunchSimplexComputation(nbStepsPerTikcs);


                while (SimplexNeedMoreComputationTime.MoveNext())
                {
                    yield return false;
                }
                SimplexNeedMoreComputationTime.Dispose();

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
                advCockpit = new List<AdvanceCockpit>();

                if (allCockpit.Count == 0)
                {
                    LogError("No cockit who can control thrusters found");
                    return false;
                }
                else
                { //If there is at least 2 cockpits

                    IMyCockpit mainCockpit = allCockpit.FirstOrDefault(cockpit => cockpit.IsMainCockpit);
                    if (mainCockpit == null)
                    {   //If there is no "Main Cockpit", we take alls of them
                        allCockpit.ForEach(cockpit => advCockpit.Add(new AdvanceCockpit(cockpit)));
                    }
                    else
                    {  //Else we take only the main cockpit
                        advCockpit.Add(new AdvanceCockpit(mainCockpit));
                    }
                }
                return true;
            }

            //Update all gravity thruters and compute news optimal coefficients
            bool findAllGravityThruster()
            {
                //Search all thruster component
                IMyBlockGroup allThruster = pgr.GridTerminalSystem.GetBlockGroupWithName(FILTER_GRAVITY_COMPONENTS);
                List<IMyVirtualMass> allGravityMass = new List<IMyVirtualMass>(200);
                List<IMyGravityGenerator> allGravityGen = new List<IMyGravityGenerator>(50);

                if (allThruster != null)
                {
                    allThruster.GetBlocksOfType(allGravityGen);
                    allThruster.GetBlocksOfType(allGravityMass);
                }

                if (allGravityGen.Count == 0)
                    pgr.GridTerminalSystem.GetBlocksOfType(allGravityGen);

                if (allGravityMass.Count == 0)
                    pgr.GridTerminalSystem.GetBlocksOfType(allGravityMass);

                if (allGravityMass.Count == 0 || allGravityGen.Count == 0)
                {
                    LogError($"We didn't found yours thruster component : \n - gravity generator found = {allGravityGen.Count}\n - artificial mass found = {allGravityMass.Count}\nTry to set all yours gravity thrusters component in a same group \"{FILTER_GRAVITY_COMPONENTS}\"");
                    return false;
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
                return true;
            }

            //return true untile the computation of news optimal coefficients isn't finished 
            IEnumerator<bool> LaunchSimplexComputation(int nbStepPerTicks)
            {
                //Recording of distances :
                centerOfMass_Bship = advCockpit[0].m_cockpit.CenterOfMass - pgr.Me.CubeGrid.GetPosition(); //ship's center of mass in the base of ship but oriented in Absolute/World base
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
                        LogError("Cannot compute thruster balance\nSet 'const bool USE_DEBUG = true' \non the top of script and recompile\n to see more info");
                        pgr.Me.CustomData = strLog.ToString();
                    }


                    SimplexNeedMoreComputeTime.Dispose();

                    yield break;
                }


                shipMass = advCockpit[0].m_cockpit.CalculateShipMass().TotalMass;


                ThrustFactorComposator_Bship_kN = torqueComposator.OptimalThrustPowerPerThruster_kN;


                // we set float.MaxValue by default to counter some division by 0
                maximumThrustPerSide_Bship_kN_noZero = new Vector3D(
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[0] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[1] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                    torqueComposator.sumOptimalThrustPowerPerSide_kN[2] == 0 ? float.MaxValue : torqueComposator.sumOptimalThrustPowerPerSide_kN[2]
                    );
                maxSpeedBy10Ticks_Bship_ms_noZero = (maximumThrustPerSide_Bship_kN_noZero * 1000) / (shipMass * 6); // *6 because ther is 60 ticks pers second, so each 10Ticks is 1/6 seconds

                //Send thrust characteristics to user
                maximumThrustPerSide_Bcock_kN = advCockpit[0].Bship_2_Bcock(new Vector3(torqueComposator.sumOptimalThrustPowerPerSide_kN[0],
                                                                torqueComposator.sumOptimalThrustPowerPerSide_kN[1],
                                                                torqueComposator.sumOptimalThrustPowerPerSide_kN[2]));

                logPerformances();


                SimplexNeedMoreComputeTime.Dispose();
            }
            #endregion


            #region logTools

            public void LogError(string errorMsg)
            {
                LogMsg("\nERROR: " + errorMsg);
            }
            public void LogMsg(string msg)
            {
                var partSize = 40;
                var parts = Enumerable.Range(0, (msg.Length + partSize - 1) / partSize)
                    .Select(i => msg.Substring(i * partSize, Math.Min(msg.Length - i * partSize, partSize)));

                foreach (string str in parts)
                    strLog.Append(str).AppendLine();
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
                if(advCockpit.Count > 1)
                    LogMsg("(X,Y,Z) seen from cockpit '" + advCockpit[0].m_cockpit.CustomName + "'\n");

                LogV3(Vector3.Abs(maximumThrustPerSide_Bcock_kN * 1000 / shipMass), "Maximum Acceleration :", "m/s²");
                LogV3(Vector3.Abs(maximumThrustPerSide_Bcock_kN * 1000), "Maximum Thrust :", "N");

                Vector3 theoricMaximumThrust_Bship = new Vector3(LeftRight_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                                 UpDown_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN),
                                                                 BackForw_thruster_Bship.Sum(gravThrust => gravThrust.m_maximumThrust_kN));

                Vector3 thrusterPosition_efficiency_Bship = new Vector3(
                    theoricMaximumThrust_Bship.X == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.X / theoricMaximumThrust_Bship.X,
                    theoricMaximumThrust_Bship.Y == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Y / theoricMaximumThrust_Bship.Y,
                    theoricMaximumThrust_Bship.Z == 0 ? 0 : 100 * maximumThrustPerSide_Bship_kN_noZero.Z / theoricMaximumThrust_Bship.Z);

                LogV3(Vector3.Abs(advCockpit[0].Bship_2_Bcock(thrusterPosition_efficiency_Bship)), "Position Efficiency :", "%");
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

                advCockpit.ForEach(advCock => advCock.DebugThrusters(str3Debug_Bship));


                return;
            }

            public void PrintDebug()
            {
                if (!USE_DEBUG)
                    return;

                advCockpit.ForEach(advCock => advCock.PrintDebug(strDebug, strLog));
                strDebug.Clear();
            }
            #endregion
        }
    }
}
