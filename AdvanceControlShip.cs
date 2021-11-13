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
        public class AdvanceControlShip
        {
            public IMyShipController m_shipControl;

            public IMyCockpit m_cockpit;
            public IMyRemoteControl m_remote;

            //Matrice to change a vector in base absolute to base of cockpit
            public MatrixD Babs_2_Bcockpit
            {
                get
                {
                    return MatrixD.Transpose(m_shipControl.WorldMatrix); //Transpose is quicker than invert, and equivalent in this case
                }
            } 

            MatrixD rotation_Bcockpit_2_Bship; //Matrice to change a vector base of cockpit to base of ship grid
            MatrixD rotation_Bship_2_Bcockpit; //Matrice to change a vector base of ship grid to base of cockpit 

            StringBuilder strDebug = new StringBuilder();
            StringBuilder strDebugThrust = new StringBuilder();
            IMyTextSurface lcd1;
            IMyTextSurface lcd2;
            IMyTextSurface lcd3;

            public AdvanceControlShip(IMyCockpit cockpit)
            {
                m_shipControl = cockpit;
                m_cockpit = cockpit;
                m_remote = null;

                Matrix cockOrientation = new Matrix();
                m_shipControl.Orientation.GetMatrix(out cockOrientation);
                rotation_Bcockpit_2_Bship = cockOrientation;
                rotation_Bship_2_Bcockpit = MatrixD.Transpose(rotation_Bcockpit_2_Bship); //Transpose is quicker than invert, and equivalent in this case

                if (USE_DEBUG)
                {
                    lcd1 = m_cockpit.GetSurface(0);
                    setFont(lcd1);
                    if (m_cockpit.SurfaceCount > 1)
                    {
                        lcd2 = m_cockpit.GetSurface(1);
                        setFont(lcd2);
                    }
                    if (m_cockpit.SurfaceCount > 2)
                    {
                        lcd3 = m_cockpit.GetSurface(2);
                        setFont(lcd3);
                    }
                }
            }

            public AdvanceControlShip(IMyRemoteControl remote)
            {
                m_shipControl = remote;
                m_cockpit = null;
                m_remote = remote;

                Matrix cockOrientation = new Matrix();
                m_shipControl.Orientation.GetMatrix(out cockOrientation);
                rotation_Bcockpit_2_Bship = cockOrientation;
                rotation_Bship_2_Bcockpit = MatrixD.Transpose(rotation_Bcockpit_2_Bship); //Transpose is quicker than invert, and equivalent in this case

                lcd1 = null;
            }

            public Vector3 getMoveIndicator_Bship()
            {
                return Bcock_2_Bship(m_shipControl.MoveIndicator);
            }

            #region debugTools
            public void DebugSpeed()
            {
                Vector3D speed_Bship = m_shipControl.GetShipVelocities().LinearVelocity;

                var rotBase = Babs_2_Bcockpit;
                Vector3D speed_Bcock;
                Vector3D.Rotate(ref speed_Bship, ref rotBase, out speed_Bcock);
                DebugV3(speed_Bcock, "Speed Cockpit:", "m/s");
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

            public void DebugLn(string str)
            { strDebug.Append(str + "\n"); }

            public void DebugV3(Vector3 v, string title, string units = "")
            {
                DebugLn(title);
                strDebug.Append("X:" + (v.X > 0 ? " " : "") + numSi(v.X) + units);
                strDebug.Append(" Y:" + (v.Y > 0 ? " " : "") + numSi(v.Y) + units);
                DebugLn(" Z:" + (v.Z > 0 ? " " : "") + numSi(v.Z) + units);
            }

            public void PrintDebug(StringBuilder additionalDebug, StringBuilder strLog)
            {
                if (!USE_DEBUG || lcd1 == null)
                    return;

                strDebug.Append(additionalDebug).Append(strDebugThrust);
                const float nbLineMax = 17;

                lcd1.WriteText("");

                if (lcd2 == null)
                {
                    lcd1.WriteText(strDebug.ToString());
                }
                else
                {
                    lcd2.WriteText("");

                    var multiLine = strDebug.ToString().Split('\n');
                    for (int i = 0; i < multiLine.Length; ++i)
                    {
                        if (i < nbLineMax)
                            lcd1.WriteText(multiLine[i] + '\n', true);
                        else
                            lcd2.WriteText(multiLine[i] + '\n', true);

                    }
                }
                if (lcd3 != null)
                    lcd3.WriteText(strLog.ToString());


                strDebug.Clear();
                strDebugThrust.Clear();
            }

            public void DebugThrusters(StringBuilder[] str3Debug_Bship)
            {
                Vector3 orientation = Bship_2_Bcock(new Vector3(0.1, 1, 2));
                Vector3 orderSide = Vector3.Abs(orientation);

                strDebugThrust.Append(orientation.X > 0 ? "Left <-> Right :\n" : "Right <-> Left :\n");
                strDebugThrust.Append(str3Debug_Bship[(int)orderSide.X]);
                strDebugThrust.Append(orientation.Y > 0 ? "Down <-> Up :\n" : "Up <-> Down:\n");
                strDebugThrust.Append(str3Debug_Bship[(int)orderSide.Y]);
                strDebugThrust.Append(orientation.Z > 0 ? "FrontWard <-> Backward :\n" : "Backward <-> FrontWard:\n");
                strDebugThrust.Append(str3Debug_Bship[(int)orderSide.Z]);
            }
            #endregion
        }
    }
}
