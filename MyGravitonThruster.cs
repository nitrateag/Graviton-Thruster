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
        public class MyGravitonThruster
        {
            public IMyGravityGenerator m_gravGen;
            List<IMyVirtualMass> m_mass = new List<IMyVirtualMass>(26);
            public Base6Directions.Axis axe;
            public readonly float m_artificialMass;
            public readonly float m_maximumThrust;
            short m_orientationThrusterCorrection;

            float m_thrust;
            bool m_enabled;

            public bool Enabled
            {
                get
                {
                    return m_enabled;
                }

                set
                {
                    if (m_enabled == value)
                        return;

                    m_enabled = value;
                    m_gravGen.Enabled = m_enabled;
                    foreach (IMyVirtualMass mass in m_mass)
                        mass.Enabled = m_enabled;
                }
            }

            public float Thrust
            {
                get
                {
                    return m_thrust;
                }

                set
                {
                    m_thrust = value;
                    Enabled = m_thrust != 0f;
                    m_gravGen.GravityAcceleration = m_orientationThrusterCorrection * m_thrust ;
                    //m_gravGen.GravityAcceleration = m_orientationThrusterCorrection * m_thrust / m_artificialMass;
                }
            }

            public MyGravitonThruster(IMyGravityGenerator gravGen, List<IMyVirtualMass> allMass)
            {
                m_gravGen = gravGen;
                m_enabled = m_gravGen.Enabled;

                //Correction de l'orientation du thruster (pour qu'ils poussent tous dans la même direction
                switch (m_gravGen.Orientation.Up)
                {
                    case Base6Directions.Direction.Right:
                        axe = Base6Directions.Axis.LeftRight;
                        m_orientationThrusterCorrection = -1;
                        break;
                    case Base6Directions.Direction.Left:
                        axe = Base6Directions.Axis.LeftRight;
                        m_orientationThrusterCorrection = 1;
                        break;
                    case Base6Directions.Direction.Up:
                        axe = Base6Directions.Axis.UpDown;
                        m_orientationThrusterCorrection = -1;
                        break;
                    case Base6Directions.Direction.Down:
                        axe = Base6Directions.Axis.UpDown;
                        m_orientationThrusterCorrection = 1;
                        break;
                    case Base6Directions.Direction.Forward:
                        axe = Base6Directions.Axis.ForwardBackward;
                        m_orientationThrusterCorrection = 1;
                        break;
                    case Base6Directions.Direction.Backward:
                        axe = Base6Directions.Axis.ForwardBackward;
                        m_orientationThrusterCorrection = -1;
                        break;
                }

                //On récupère la liste des masses artificielles qui sont dans le champ de gravité
                m_gravGen.FieldSize = new Vector3(6, 6, 6);
                Vector3 gravityFeild = m_gravGen.FieldSize / 5f; // new Vector3(m_gravGen.FieldSize.X, m_gravGen.FieldSize.Y, m_gravGen.FieldSize.Z);
                Vector3 distance = new Vector3();

                //gravityFeild /= 5; //On divise par 2.5 car 1 bloc fait 2.5 de coté, et aussi par 2, car on souhaite avoir le rayon à la place du diamètre diamètre. Donc 5

                foreach (IMyVirtualMass mass in allMass)
                {
                    distance = mass.Position - m_gravGen.Position;
                    //if (Math.Abs(distance.X) <= gravityFeild.X
                    //            && Math.Abs(distance.X) <= gravityFeild.X
                    //            && Math.Abs(distance.Z) <= gravityFeild.Z)
                    //{
                    //    m_mass.Add(mass);
                    //}
                    if (distance.LengthSquared() <= gravityFeild.LengthSquared())
                    {
                        m_mass.Add(mass);
                    }
                }

                m_artificialMass = m_mass.Sum(mass => mass.VirtualMass);
                //m_maximumThrust = 9.81f * m_artificialMass;
                m_maximumThrust = 9.81f;// * m_artificialMass;
            }

            public override string ToString()
            {
                var eff = Math.Round(m_thrust / m_maximumThrust * 10);
                string str = "[";
                for(int i = -10; i <= 10; ++i)
                {
                    if (i == eff)
                        str += "|";
                    else if (i == 0)
                        str += ":";
                    else
                        str += "-";
                }

                return str += "] " + m_thrust + "/" + m_maximumThrust;
            }

            public Vector3D GetPosition() { return m_gravGen.Position*2.5D; }
        }
    }
}
