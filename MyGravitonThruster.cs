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
            public readonly float m_artificialMass_kg;  //In tonne or Mega grammes
            public readonly float m_maximumThrust_kN;   //In Mega Newtown
            short m_orientationThrusterCorrection;
            Vector3D m_position;


            float m_thrust_MN; //In Mega Newtown
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
                    return m_thrust_MN;
                }

                set
                {
                    m_thrust_MN = value;
                    Enabled = m_thrust_MN != 0f;
                    m_gravGen.GravityAcceleration = m_orientationThrusterCorrection * m_thrust_MN / m_artificialMass_kg*1000;
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


                Vector3 gravityFeild = new Vector3(); //On réoriente le vector "Gravity feild" pour que sa zone corresponde aux coordonées des autres elements de la grille
                switch (axe)
                {
                    case Base6Directions.Axis.LeftRight:
                        gravityFeild.X = m_gravGen.FieldSize.Y / 5f; //On divise par 2.5 car 1 bloc fait 2.5 de coté, et aussi par 2, car on souhaite avoir le rayon à la place du diamètre diamètre. Donc 5 
                        if (m_gravGen.Orientation.Forward <= Base6Directions.Direction.Backward) // ForwardBackward
                        {
                            gravityFeild.Y = m_gravGen.FieldSize.X / 5f;
                            gravityFeild.Z = m_gravGen.FieldSize.Z / 5f;
                        }
                        else // UpDown
                        {
                            gravityFeild.Y = m_gravGen.FieldSize.Z / 5f;
                            gravityFeild.Z = m_gravGen.FieldSize.X / 5f;
                        }
                        break;
                    case Base6Directions.Axis.UpDown:
                        gravityFeild.Y = m_gravGen.FieldSize.Y / 5f;
                        if (m_gravGen.Orientation.Forward <= Base6Directions.Direction.Backward) // ForwardBackward
                        {
                            gravityFeild.X = m_gravGen.FieldSize.X / 5f;
                            gravityFeild.Z = m_gravGen.FieldSize.Z / 5f;
                        }
                        else // LeftRight
                        {
                            gravityFeild.X = m_gravGen.FieldSize.Z / 5f;
                            gravityFeild.Z = m_gravGen.FieldSize.X / 5f;
                        }
                        break;
                    case Base6Directions.Axis.ForwardBackward:
                        gravityFeild.Z = m_gravGen.FieldSize.Y / 5f;
                        if (m_gravGen.Orientation.Forward <= Base6Directions.Direction.Right) // LeftRight
                        {
                            gravityFeild.X = m_gravGen.FieldSize.X / 5f;
                            gravityFeild.Y = m_gravGen.FieldSize.Z / 5f;
                        }
                        else // UpDown
                        {
                            gravityFeild.X = m_gravGen.FieldSize.Z / 5f;
                            gravityFeild.Y = m_gravGen.FieldSize.X / 5f;
                        }
                        break;
                }

                //On récupère la liste des masses artificielles qui sont dans le champ de gravité
                Vector3 distance = new Vector3();
                m_position = new Vector3D(0, 0, 0);

                foreach (IMyVirtualMass mass in allMass)
                {
                    distance = mass.Position - m_gravGen.Position;
                    if (Math.Abs(distance.X) <= gravityFeild.X
                         && Math.Abs(distance.Y) <= gravityFeild.Y
                         && Math.Abs(distance.Z) <= gravityFeild.Z)
                    {
                        m_mass.Add(mass);
                        m_position += mass.Position * mass.VirtualMass;
                    }
                }

                m_artificialMass_kg = m_mass.Sum(mass => mass.VirtualMass);
                m_maximumThrust_kN = 9.81f * m_artificialMass_kg / 1000;

                if (m_artificialMass_kg > 0)
                    m_position = (m_position * 2.5D) / m_artificialMass_kg;
                else
                    m_position = m_gravGen.Position * 2.5D;

            }

            public override string ToString()
            {
                var eff = Math.Round(m_thrust_MN / m_maximumThrust_kN * 10);
                string str = "[";

                for (int i = -10; i <= 10; ++i)
                {
                    if (i == eff)
                        str += "|";
                    else if (i == 0)
                        str += ":";
                    else
                        str += "-";
                }

                return str += "]  " + m_thrust_MN + "/" + m_maximumThrust_kN + " " + m_mass.Count;
            }

            public Vector3D GetPosition() { return m_position; }
        }
    }
}
