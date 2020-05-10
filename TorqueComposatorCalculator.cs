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

        public class TorqueComposatorCalculator
        {
            public bool success;

            /* dist_thrust2CenterMass_bySide[side][axe][#thruster]
             * side = { leftRight = 0, upDown = 1, ForwBack = 2}
             * Axe = { X = 0, Y = 1, Z = 2}
             * #Trhuster = [0, ... , n-1], n = number of thruster by side
             */
            float[][][] dist_thrust2CenterMass_bySide = new float[3][][];
            
            // thrustPowerMax[side][#thruster]
            float[][] thrustPowerMax = new float[3][];


            // OptimalThrustPowerPerThruster_kN[side][#thruster] = new float[3][];
            public float[][] OptimalThrustPowerPerThruster_kN = new float[3][];
            // sumOptimalThrustPowerPerSide_kN[side];
            public float[] sumOptimalThrustPowerPerSide_kN = new float[3];

            public void printSimplex(ref StringBuilder str, ref double[] simplex, int nbColumn, int nbLine, int line_sel = -1, int col_sel = -1)
            {
                for (int line = 0; line < nbLine*nbColumn; line += nbColumn)
                {
                    for (int column = 0; column < nbColumn; ++column)
                    {
                        if (line == line_sel && column == col_sel)
                            str.Append($"[{simplex[line + column], 8:G3}]");
                            //str.AppendFormat($"[{0,7:G5}]", simplex[line + column]);
                        else
                            str.Append($"{simplex[line + column], 10:G3}");
                            //str.AppendFormat("{0,7:G5}", simplex[line + column]);
                    }
                    str.Append("\n");
                }
                str.Append("\n");

            }
            public void setThrusters(List<MyGravitonThruster> thrust_leftRight, List<MyGravitonThruster> thrust_UpDown, List<MyGravitonThruster> thrust_ForwBack,  Vector3D massCenter)
            {

                dist_thrust2CenterMass_bySide[0] = new float[3][];
                dist_thrust2CenterMass_bySide[1] = new float[3][];
                dist_thrust2CenterMass_bySide[2] = new float[3][];

                OptimalThrustPowerPerThruster_kN[0] = new float[(thrust_leftRight.Count)];
                OptimalThrustPowerPerThruster_kN[1] = new float[(thrust_UpDown.Count)];
                OptimalThrustPowerPerThruster_kN[2] = new float[(thrust_ForwBack.Count)];
                thrustPowerMax[0] = new float[(thrust_leftRight.Count)];
                thrustPowerMax[1] = new float[(thrust_UpDown.Count)];
                thrustPowerMax[2] = new float[(thrust_ForwBack.Count)];

                for (int axe = 0; axe < 3; ++axe)
                {
                    dist_thrust2CenterMass_bySide[0][axe] = new float[(thrust_leftRight.Count)];
                    dist_thrust2CenterMass_bySide[1][axe] = new float[(thrust_UpDown.Count)];
                    dist_thrust2CenterMass_bySide[2][axe] = new float[(thrust_ForwBack.Count)];
                }

                for (int i = 0; i< thrust_leftRight.Count; ++i)
                {
                    Vector3D vector3D = massCenter - thrust_leftRight[i].GetPosition();
                    dist_thrust2CenterMass_bySide[0][0][i] = (float)vector3D.X;
                    dist_thrust2CenterMass_bySide[0][1][i] = (float)vector3D.Y;
                    dist_thrust2CenterMass_bySide[0][2][i] = (float)vector3D.Z;
                    thrustPowerMax[0][i] = thrust_leftRight[i].m_maximumThrust_kN;
                }

                for (int i = 0; i < thrust_UpDown.Count; ++i)
                {
                    Vector3D vector3D = massCenter - thrust_UpDown[i].GetPosition();
                    dist_thrust2CenterMass_bySide[1][0][i] = (float)vector3D.X;
                    dist_thrust2CenterMass_bySide[1][1][i] = (float)vector3D.Y;
                    dist_thrust2CenterMass_bySide[1][2][i] = (float)vector3D.Z;
                    thrustPowerMax[1][i] = thrust_UpDown[i].m_maximumThrust_kN;
                }

                for (int i = 0; i < thrust_ForwBack.Count; ++i)
                {
                    Vector3D vector3D = massCenter - thrust_ForwBack[i].GetPosition();
                    dist_thrust2CenterMass_bySide[2][0][i] = (float)vector3D.X;
                    dist_thrust2CenterMass_bySide[2][1][i] = (float)vector3D.Y;
                    dist_thrust2CenterMass_bySide[2][2][i] = (float)vector3D.Z;
                    thrustPowerMax[2][i] = thrust_ForwBack[i].m_maximumThrust_kN;
                }
            }

            public IEnumerator<bool> ComputeSolution(int nbStepsPersTicks, StringBuilder strDebug, bool useDebug)
            {
                success = false;

                /* We have to solve a problem for each side of the ship. For the side UpDown (axe = 1), it look like:
                 * 
                 * 1/ { f1*z1 + f2*z2 + ... + fn*zn = 0
                 *    { f1*x1 + f2*x2 + ... + fn*xn = 0
                 * 
                 * with n = count of thruster on UpDown_Side
                 * fi = power of thruster #x on UpDown_Side, fi = [-fmi, fmi]
                 * fmi = maximum power of thruster i
                 * zi = distance between thruster #i to center_of_mass, projected on axe Z
                 * xi = distance between thruster #i to center_of_mass, projected on axe X
                 * 
                 * To solve this linear problem, we will use the Simplex algorithm to find 
                 * the solution S who maximise the power of ours thruster :
                 * 
                 * S = f1 + f2 + ... + fn
                 * 
                 * To understand how simplex work's :
                 * https://sites.math.washington.edu/~burke/crs/407/notes/section2.pdf
                 * in french : https://www2.mat.ulaval.ca/fileadmin/Cours/MAT-2920/Chapitre3.pdf
                 * 
                 * 
                 * To use Simplex methode, we must have our variables fi greater than or equal to 0.
                 * 
                 * { Fi = fi + fmi
                 * { -fmi <= fi <= fmi
                 * imply : { fi = Fi - fmi
                 *         { 0 <= Fi <= 2*fmi
                 * 
                 * we introduce F1 in 1/ and S :
                 * 
                 * 1/ => 2/ : { F1*z1 + F2*z2 + ... + Fn*zn = sum(fmi*zi)
                 *            { F1*x1 + F2*x2 + ... + Fn*xn = sum(fmi*x1)
                 *            
                 * S = F1 + F2 + ... + Fn = sum(fmi)           
                 *            
                 * So, now we can begin the simplex on 2/ with Fi like variable :
                 * 
                 * We will use an implementation of Simplex algorithm who like like that : 
                 * https://www.youtube.com/watch?v=upgpVkAkFkQ
                 * Example of a 5 thruster equation : 
                 * http://simplex.tode.cz/en/s7xh4f5qvbt
                 * 
                 */
                int nbStateComputed = 0;

                for (int axe = 0; axe < 3; ++axe)
                {
                    if (useDebug)
                        strDebug.AppendLine($"___ axe {axe} ___");


                    var z = dist_thrust2CenterMass_bySide[axe][(axe + 1) % 3];
                    var x = dist_thrust2CenterMass_bySide[axe][(axe + 2) % 3];
                    var fm = thrustPowerMax[axe];
                    int n = dist_thrust2CenterMass_bySide[axe][0].Count();

                    float sumZ = 0f, sumX = 0f;           // needed for later
                    float sum_fm_Z = 0f, sum_fm_X = 0f; // needed for later
                    float sumFm = 0f;                    // needed for later
                    for (int i = 0; i < n; ++i)
                    {
                        sumZ += z[i];
                        sumX += x[i];
                        sum_fm_Z += z[i] * fm[i];
                        sum_fm_X += x[i] * fm[i];
                        sumFm += fm[i];
                    }

                    //Now, let's make sumZ and sumX > 0
                    if (sumZ < 0 && sumX < 0)
                    {
                        sumZ *= -1;
                        sumX *= -1;
                        sum_fm_Z *= -1;
                        sum_fm_X *= -1;
                        for (int column = 0; column < n; ++column)
                        {
                            z[column] *= -1;
                            x[column] *= -1;
                        }
                    }
                    else if (sumZ < 0)
                    {
                        sumZ *= -1;
                        sum_fm_Z *= -1;
                        for (int column = 0; column < n; ++column)
                            z[column] *= -1;
                    }
                    else if (sumX < 0)
                    {
                        sumX *= -1;
                        sum_fm_X *= -1;
                        for (int column = 0; column < n; ++column)
                            x[column] *= -1;
                    }
                    /* _________
                    * 
                    * Step 1 : Set simplex ready
                    * _________
                    * 
                    * the system to solve in the simplex : wint [n] the number of graviton thruster 
                    * 
                    * 3/ : { F1*z1 + F2*z2 + ... + Fn*zn = sum(fmi*zi)
                    *      { F1*x1 + F2*x2 + ... + Fn*xn = sum(fmi*xi)
                    *      { F1                          <= 2*fm1
                    *      {         F2                  <= 2*fm2
                    *      {            ...              <=  ... 
                    *      {                       Fn    <= 2*fmn
                    *      { F1   + F2     + ... + Fn     = sum(fmi) = S
                    *      
                    *       
                    * 
                    * 
                    * Add slack variables (s1 ... sn) on 3/, and a line S2 = sum of equation in 2/:
                    * 
                    * 4/ : { F1                                         + s1                = 2*fm1
                    *      {              F2                                 + s2           = 2*fm2
                    *      {                           ...                         ...      =  ... 
                    *      {                                 Fn                        + sn = 2*fmn
                    *      { F1*z1      + F2*z2      + ... + Fn*zn                          = sum(fmi*zi)
                    *      { F1*x1      + F2*x2      + ... + Fn*xn                          = sum(fmi*xi)
                    *      { F1         + F2         + ... + Fn                             = sum(fmi) = S
                    *      { F1*(z1+x1) + F2*(z2+x2) + ... + Fn*(zn+xn)                     = sum(fmi*zi)+sum(fmi*xi) = S2
                    *      
                    *  
                    *  So, the simplex's matrix look like
                    *
                    * 
                    *                                                   col_res 
                    *               _ col_0  ...      col_n      ...    col_2n (nbcolumn = 2n+1) _
                    *              |    1   0  ... 0    1  0  0  ... 0   2*fm1                    | line_0
                    *              |    0   1  ... 0    0  1  0  ... 0   2*fm2                    | line_1
                    *              |    0   0  \   0    0  0  \  ... 0     |                      |
                    *              |    0   0   \  0    0  0  0  \.. 0     |                      |
                    *   Simplex =  |    0   0    \ 0    0  0  0  ..\ 0     |                      |
                    *              |    0   0 ...  1    0  0  0  ... 1   2*fmn                    | line_n-1
                    *              |    z1  z2 ... zn   0  0  0  ... 0   sum(fmi*zi)              | line_n   / line_z
                    *              |    x1  x2 ... xn   0  0  0  ... 0   sum(fmi*xi)              | line_n+1 / line_x
                    *              |    1   1 ...  1    0  0  0  ... 0   sum(fmi)                 | line_n+2 / line_S
                    *              |_ x1+z1  ... xn+zn  0  0  0  ... 0   sum(fmi*zi)+sum(fmi*xi) _| line_n+3 / line_S2 (so, there is n+4 lines)
                    *             
                    *   The line line_n+3 is the sum of line who have an artificial viraible ... withot the artificial variable himself,
                    *   so, line_n+3 = line_n + line_n+1 - a1 - a2
                    */

                    int nbColumn = n * 2 + 1;
                    int nbLine = n + 4;
                    int nbElem = nbColumn * nbLine;
                    var simplex = new double[ nbElem ]; // we will acceed to ellement of simplex with simplex[ line + column ]

                    //FullFillment from col_0 to col_2n-1
                    var line_z  = nbColumn * n;         // line_z  = line_n
                    var line_x  = line_z + nbColumn;    // line_x  = line_n+1
                    var line_S  = line_x + nbColumn;    // line_S  = line_n+2
                    var line_S2 = line_S + nbColumn;    // line_S2 = line_n+3
                    var col_res = 2 * n;                // col_res = col_2n

                    for (int column = 0; column < n; ++column)
                    {
                        //Diagonale : from line_0 to line_n-1
                        simplex[column*nbColumn + column   ] = 1;
                        simplex[column*nbColumn + column+n ] = 1;

                        //line_z
                        simplex[line_z + column] = z[column];
                        //line_x
                        simplex[line_x + column] = x[column];
                        //line_S
                        simplex[line_S + column]  = 1;
                        //line_S2
                        simplex[line_S2 + column] = x[column] + z[column];
                    }



                    //Fullfillment of the last column : col_res
                    // FullFillment of col_res, start at line2 end at line_n+1
                    for (int line = 0; line < line_z; line += nbColumn)
                        simplex[line + col_res] = 2*fm[line/nbColumn];

                    // 4 last values of last column :
                    simplex[line_z + col_res] = sum_fm_Z;             // sum(fmi*zi)            
                    simplex[line_x + col_res] = sum_fm_X;             // sum(fmi*xi)            
                    simplex[line_S + col_res] = sumFm;                // sum(fmi)               
                    simplex[line_S2 + col_res] = sum_fm_Z + sum_fm_X; // sum(fmi * zi) + sum(fmi * xi)

                    //Now, we will check that all coeff of col_res are > 0, and multiply the line by -1 if they are
                    if (sumZ < 0)
                        for (int column = 0; column < nbColumn; ++column)
                            simplex[line_z + column] *= -1;

                    if (sumX < 0)
                        for (int column = 0; column < nbColumn; ++column)
                            simplex[line_x + column] *= -1;
                    /* _________
                    * 
                    * Step 2 : Compute the simplex
                    * _________ 
                    */

                    int countNbPivot = 0;

                    var linkLineToThruster = new int[nbLine-2];
                    for (int i = 0; i < n; linkLineToThruster[i++] = n) ;


                    double zero_epsilon = 0;// .000001;
                    var line_S_current = line_S2;
                    while (countNbPivot < 100)
                    {
                        /* step 2.1 : find the column :
                        * 
                        * we will select the comlumn between [col_0, col-2n-1] who have the higher values at line_S2.
                        * If all line_S2 is 0 on these column, we will use line_S instead of line_S2, and never use line_S again. 
                        *  
                        *                  
                        */

                        if (++nbStateComputed >= nbStepsPersTicks)
                            yield return true;

                    FindTheColumn:

                        int col_sel = 0; //The id of column selected for the future gaussian pivot
                        double maxValue = simplex[line_S_current];
                        for(int column = 1; column < col_res; ++column)
                            if(maxValue < simplex[line_S_current + column])
                            {
                                col_sel = column;
                                maxValue = simplex[line_S_current + column];
                            }
                        if (maxValue <= zero_epsilon) //equivalent at "if maxValue is <= 0", but we can't use this operator because the bouble imprecision 
                        {
                            line_S_current -= nbColumn;
                            if (line_S_current < line_S) //then we set line_S ine the selector. BUT IF we was already on line_S, 
                                break; // then the simplex is finish
                            else
                                goto FindTheColumn; //We still need to find the highest value, but for line_S
                        }

                            /* step 2.2 : find the line :
                            * 
                            * we will select the line between [line_0, line_n-1] who have the lowest POSITIVE values on this operation :
                            *  lowest = simplex[line + col_res] / simplex[line + col_sel]
                            * 
                            */

                            int line_sel = 0;
                        double lowestValue = Double.MaxValue;

                        for (int line = 0; line < line_S; line += nbColumn)
                        {
                            if (simplex[line + col_sel] <= zero_epsilon)
                                continue;

                            var div = simplex[line + col_res] / simplex[line + col_sel];
                            if (div < lowestValue)
                            {
                                lowestValue = div;
                                line_sel = line;
                            }
                        }

                        /* step 2.3 : compute the gaussian pivot :
                        * 
                        *   
                        */
                        ++countNbPivot;
                        if (useDebug)
                        {
                            strDebug.AppendLine($"___ simplex {countNbPivot} ___");
                            printSimplex(ref strDebug, ref simplex, nbColumn, nbLine, line_sel, col_sel);
                        }

                        double elem_sel = simplex[line_sel + col_sel];
                        var dataColumn_sel = new double[nbLine];
                        for (int i = 0; i < nbLine; ++i)
                            dataColumn_sel[i] = simplex[i * nbColumn + col_sel];

                        for (int column = 0; column < nbColumn; ++column)
                        {
                            simplex[line_sel + column] /= elem_sel;

                            int line = 0;
                            for (; line < line_sel; line += nbColumn)
                                simplex[line + column] -= simplex[line_sel + column] * dataColumn_sel[line / nbColumn];

                            line += nbColumn; //We don't work on line selected

                            for (; line <= line_S_current; line += nbColumn)
                                simplex[line + column] -= simplex[line_sel + column] * dataColumn_sel[line /nbColumn];
                        }

                        /* step 2.4 : record the link between line and thruster associated:
                        * 
                        *   
                        */
                        linkLineToThruster[line_sel / nbColumn] = col_sel;

                    } //end compute simplex


                    for (int line = 0; line < nbLine - 2; ++line)
                    {
                        var idThruster = linkLineToThruster[line];
                        if (idThruster < n)
                        {
                            OptimalThrustPowerPerThruster_kN[axe][idThruster] = (float)(simplex[line * nbColumn + col_res]) - fm[idThruster];
                            sumOptimalThrustPowerPerSide_kN[axe] += OptimalThrustPowerPerThruster_kN[axe][idThruster];
                        }
                    }

                    if (useDebug)
                    {
                        strDebug.AppendLine("___ Result simplex ___");
                        printSimplex(ref strDebug, ref simplex, nbColumn, nbLine);

                        strDebug.Append("___ Solution ___\n");
                        for (int i = 0; i < n; ++i)
                        {
                            strDebug.Append($"{OptimalThrustPowerPerThruster_kN[axe][i]}kN  - {OptimalThrustPowerPerThruster_kN[axe][i] / fm[i]:P}\n");
                        }
                    }

                    if (++nbStateComputed >= nbStepsPersTicks)
                        yield return true;

                } // end for each axes

                success = true;
            }//end function compute
        }

       
    }
}



//var ker = new double[n-2,n];

//    /*the ker of UpDown_Side look like
//     * 
//     * | z3*x2-x3*z2    z1*x3-z3*x1    z2*x1-z1*x2     0           ... 0 |
//     * | z4*x2-x4*z2    z1*x4-z4*x1        0       z2*x1-z1x2    0 ... 0 |
//     * |      .              .             0           0    \          0 |
//     * |      .              .             0           0     \         0 |                                                          
//     * |      .              .             0           0      \        0 |
//     * | zn*x2-xn*z2    z1*xn-zn*x1        0           0  ... z2*x1-z1x2 |  
//    */
//    var diagonalValue = z[1]*x[0] - x[1]*z[0];

//    for (int i = 0; i < n - 2; ++i)
//    {
//        ker[i, 0] = z[i + 2] * x[1] - x[i + 2] * z[1];
//        ker[i, 1] = x[i + 2] * z[0] - z[i + 2] * x[0];
//        ker[i, i + 2] = diagonalValue;
//    }

//    //So, now we have a base of the espace of all solution. 
//    // we will use Simplex algorithm to find the best solution (wo maximise the sum of thruster_power)
//    // https://sites.math.washington.edu/~burke/crs/407/notes/section2.pdf
//    // in french : https://www2.mat.ulaval.ca/fileadmin/Cours/MAT-2920/Chapitre3.pdf
//    /* 
//     * The prbleme to solve is to find the linar combinason of all ker solutions who
//     * maximise the thrust T :
//     * 
//     * => T = sum(vec_T) //the sum of each element in vector vec_T
//     * vec_T =  | F1 |
//     *          | F2 |
//     *          | .  |
//     *          | Fn |
//     * 
//     * T = F1 + F2 + ... + Fn 
//     * with Fi = Power of the thruster n°i by studied Side, n the number of thruster
//     * But now, we do not work with linear conbination of Fn value, but with linear combinason of ker's line
//     * 
//     * vec_T = w1 * ker_l1 + w2 * ker_l2 + ... wm * ker_ln
//     *    with "ker_lm" the line number "m" of ker, and "wn" the coefficient to determind with the simplex
//     *    with m = n-2
//     *    
//     * vec_T = transpose(ker) * w,  with w = ( w1, w2, w3, w4 ... wm)
//     * ___________________________________________________________________________    
//     * |                                                                          |
//     * |   => vec_T =                                                             |
//     * |   | w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wm * zn*x2-xn*z2 |       |
//     * |   | w1 * z1*x3-z3*x1 + w2 * z1*x4-z4*x1 + ... + wm * z1*xn-zn*x1 |       |
//     * |   | w1 * z2*x1-z1*x2                                             |       |
//     * |   | w2 * z2*x1-z1*x2                                             |       |
//     * |   | .                                                            |       |
//     * |   | .                                                            |       |         
//     * |   | wm * z2*x1-z1*x2                                             |       |
//     * |                                                                          |
//     * |  with m = n-2, caution, #vect_T = n                                      |
//     * |__________________________________________________________________________|
//     * 
//     * and T the objective to maximise : 
//     * T = w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wn * zn*x2-xn*z2 
//     *      + w1 * z1*x3-z3*x1 + w2 * z1*x4-z4*x1 + ... + wn * z1*xn-zn*x1
//     *      + w1 * z2*x1-z1*x2 + w2 * z2*x1-z1*x2 + ... + wm * z2*x1-z1*x2
//     * ______________________________________________________________     
//     * | <=> T = w1( z3*x2-x3*z2 + z1*x3-z3*x1 + z2*x1-z1*x2 )      |
//     * |         + w2( z4*x2-x4*z2 + z1*x4-z4*x1 + z2*x1-z1x2 )     |
//     * |         + ...                                              |
//     * |         + wm *(zn*x2-xn*z2 + z1*xn-zn*x1 + z2*x1-z1x2)     |
//     * |____with m = n-2____________________________________________|
//     * 
//     * we know we can assigne a value to F between -9.81 and 9.81, so
//     * 
//     * vec_T = 
//     * | w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wn * zn*x2-xn*z2 |   | 9.81 |
//     * | w1 * z1*x3-z3*x1 + w2 * z1*x4-z4*x1 + ... + wn * z1*xn-zn*x1 |   | 9.81 |
//     * | w1 * z2*x1-z1*x2                                             |   | 9.81 |
//     * | w2 * z2*x1-z1*x2                                             | < | 9.81 |
//     * | .                                                            |   | 9.81 |
//     * | .                                                            |   | 9.81 |                                                          
//     * | wm * z2*x1-z1*x2                                             |   | 9.81 |
//     * 
//     * 
//     * So, we introduce slack variables Wn :
//     * 
//     * W1 = 9.81 - (w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wn * zn*x2-xn*z2)
//     * W2 = 9.81 - (w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wn * zn*x2-xn*z2)
//     * W3 = 9.81 - (w1 * z2*x1-z1*x2)
//     * W4 = 9.81 - (w2 * z2*x1-z1*x2)
//     *   ...
//     * Wn = 9.81 - (wm * z2*x1-z1*x2)
//     * 
//     * 
//     * The equation become :
//     * 
//     * 
//     * | w1 * z3*x2-x3*z2 + w2 * z4*x2-x4*z2 + ... + wn * zn*x2-xn*z2  + W1 |   | 9.81 |
//     * | w1 * z1*x3-z3*x1 + w2 * z1*x4-z4*x1 + ... + wn * z1*xn-zn*x1  + W2 |   | 9.81 |
//     * | w1 * z2*x1-z1*x2                                              + W3 |   | 9.81 |
//     * | w2 * z2*x1-z1*x2                                              + W4 | = | 9.81 |
//     * | .                                                             + .  |   | 9.81 |
//     * | .                                                             + .  |   | 9.81 |                                                          
//     * | wm * z2*x1-z1*x2                                              + Wn |   | 9.81 |
//     * 
//     * So, now, the table of simplex :
//     * 
//     * | z3*x2-x3*z2  z4*x2-x4*z2 ... zn*x2-xn*z2   1  0  0 ... 0 | 9.81 |
//     * | z1*x3-z3*x1  z1*x4-z4*x1 ... z1*xn-zn*x1   0  1  0 ... 0 | 9.81 |
//     * | z2*x1-z1*x2  0           ... 0             0  0  1 ... 0 | 9.81 |
//     * | 0            z2*x1-z1*x2 ... 0             0  0  0 \   0 | 9.81 |
//     * | 0            0           z2*x1-z1*x2   ... 0  0  0  \  0 | 9.81 |
//     * | ...                                                  \ 0 | 9.81 |
//     * | 0              ...                         0  ...   0  1 | 9.81 |
//     * |----------------------------------------------------------|------|
//     * | sum(ker_l1)  sum(ker_l2)) ... sum(ker_ln)  0  ...      0 | 0    |
//     * 
//     * or 
//     * 
//     * |                                            1  0  0 ... 0 | 9.81 |
//     * |                                            0             | 9.81 |
//     * |                                            0             | 9.81 |
//     * |          transpose(ker)                    0    Id       | 9.81 |
//     * |                                            0             | 9.81 |
//     * |                                            0             | 9.81 |
//     * |                                            0             | 9.81 |
//     * |----------------------------------------------------------|------|
//     * | sum(ker_l1)  sum(ker_l2)) ... sum(ker_ln)  0  ...      0 | 0    |
//     * 
//     * 
//     * So, now, go build this table :
//    */