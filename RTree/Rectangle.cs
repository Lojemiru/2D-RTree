//   Rectangle.java version 1.0b2p1
//   Java Spatial Index Library
//   Copyright (C) 2002 Infomatiq Limited
//   Copyright (C) 2008 Aled Morris aled@sourceforge.net
//  
//  This library is free software; you can redistribute it and/or
//  modify it under the terms of the GNU Lesser General Public
//  License as published by the Free Software Foundation; either
//  version 2.1 of the License, or (at your option) any later version.
//  
//  This library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//  Lesser General Public License for more details.
//  
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA

// Ported to C# By Dror Gluska, April 9th, 2009

using System;
using System.Text;

namespace RTree;

/// <summary>
/// Currently hardcoded to 2 dimensions, but could be extended.
/// </summary>
public sealed class Rectangle
{
    /// <summary>
    /// Number of dimensions in a rectangle. In theory this
    /// could be extended to three or more dimensions.
    /// </summary>
    internal const int DIMENSIONS = 2;

    /// <summary>
    /// array containing the minimum value for each dimension; ie { min(x), min(y) }
    /// </summary>
    internal readonly int[] Max;

    /// <summary>
    /// array containing the maximum value for each dimension; ie { max(x), max(y) }
    /// </summary>
    internal readonly int[] Min;

    /// <summary>
    /// ctor
    /// </summary>
    /// <param name="x1">coordinate of any corner of the rectangle</param>
    /// <param name="y1">coordinate of any corner of the rectangle</param>
    /// <param name="x2">coordinate of the opposite corner</param>
    /// <param name="y2">coordinate of the opposite corner</param>
    public Rectangle(int x1, int y1, int x2, int y2)
    {
        Min = new int[DIMENSIONS];
        Max = new int[DIMENSIONS];
        Set(x1, y1, x2, y2);
    }

    /// <summary>
    /// ctor
    /// </summary>
    /// <param name="min">min array containing the minimum value for each dimension; ie { min(x), min(y) }</param>
    /// <param name="max">max array containing the maximum value for each dimension; ie { max(x), max(y) }</param>
    public Rectangle(int[] min, int[] max)
    {
        if (min.Length != DIMENSIONS || max.Length != DIMENSIONS)
        {
            throw new Exception("Error in Rectangle constructor: " +
                                "min and max arrays must be of length " + DIMENSIONS);
        }

        Min = new int[DIMENSIONS];
        Max = new int[DIMENSIONS];

        Set(min, max);
    }

    /// <summary>
    /// Sets the size of the rectangle.
    /// </summary>
    /// <param name="x1">coordinate of any corner of the rectangle</param>
    /// <param name="y1">coordinate of any corner of the rectangle</param>
    /// <param name="x2">coordinate of the opposite corner</param>
    /// <param name="y2">coordinate of the opposite corner</param>
    internal void Set(int x1, int y1, int x2, int y2)
    {
        Min[0] = Math.Min(x1, x2);
        Min[1] = Math.Min(y1, y2);
        Max[0] = Math.Max(x1, x2);
        Max[1] = Math.Max(y1, y2);
    }

    /// <summary>
    /// Retrieves dimensions from rectangle
    /// <para>probable dimensions:</para>
    /// <para>X = 0, Y = 1, Z = 2</para>
    /// </summary>
    public Dimension? Get(int dimension)
    {
        if ((Min.Length < dimension) || (Max.Length < dimension))
            return null;
        
        return new Dimension
        {
            Min = Min[dimension],
            Max = Max[dimension]
        };
    }

    /// <summary>
    /// Sets the size of the rectangle.
    /// </summary>
    /// <param name="min">min array containing the minimum value for each dimension; ie { min(x), min(y) }</param>
    /// <param name="max">max array containing the maximum value for each dimension; ie { max(x), max(y) }</param>
    internal void Set(int[] min, int[] max)
    {
        Array.Copy(min, 0, Min, 0, DIMENSIONS);
        Array.Copy(max, 0, Max, 0, DIMENSIONS);
    }


    /// <summary>
    /// Make a copy of this rectangle
    /// </summary>
    /// <returns>copy of this rectangle</returns>
    internal Rectangle Copy()
    {
        return new Rectangle(Min, Max);
    }

    /// <summary>
    /// Determine whether an edge of this rectangle overlies the equivalent 
    /// edge of the passed rectangle
    /// </summary>
    internal bool EdgeOverlaps(Rectangle r)
    {
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (Min[i] == r.Min[i] || Max[i] == r.Max[i])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determine whether this rectangle intersects the passed rectangle
    /// </summary>
    /// <param name="r">The rectangle that might intersect this rectangle</param>
    /// <returns>true if the rectangles intersect, false if they do not intersect</returns>
    internal bool Intersects(Rectangle r)
    {
        // Every dimension must intersect. If any dimension
        // does not intersect, return false immediately.
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (Max[i] < r.Min[i] || Min[i] > r.Max[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Determine whether this rectangle contains the passed rectangle
    /// </summary>
    /// <param name="r">The rectangle that might be contained by this rectangle</param>
    /// <returns>true if this rectangle contains the passed rectangle, false if it does not</returns>
    internal bool Contains(Rectangle r)
    {
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (Max[i] < r.Max[i] || Min[i] > r.Min[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Return the distance between this rectangle and the passed point.
    /// If the rectangle contains the point, the distance is zero.
    /// </summary>
    /// <param name="p">Point to find the distance to</param>
    /// <returns>distance between this rectangle and the passed point.</returns>
    internal int Distance(Point p)
    {
        var distanceSquared = 0;
        for (var i = 0; i < DIMENSIONS; i++)
        {
            var greatestMin = Math.Max(Min[i], p.Coordinates[i]);
            var leastMax = Math.Min(Max[i], p.Coordinates[i]);
            if (greatestMin > leastMax)
            {
                distanceSquared += ((greatestMin - leastMax) * (greatestMin - leastMax));
            }
        }
        return (int)Math.Sqrt(distanceSquared);
    }

    /// <summary>
    /// Calculate the area by which this rectangle would be enlarged if
    /// added to the passed rectangle. Neither rectangle is altered.
    /// </summary>
    /// <param name="r">
    /// Rectangle to union with this rectangle, in order to
    /// compute the difference in area of the union and the
    /// original rectangle
    /// </param>
    internal int Enlargement(Rectangle r)
    {
        var enlargedArea = (Math.Max(Max[0], r.Max[0]) - Math.Min(Min[0], r.Min[0])) *
                           (Math.Max(Max[1], r.Max[1]) - Math.Min(Min[1], r.Min[1]));

        return enlargedArea - GetArea();
    }

    /// <summary>
    /// Compute the area of this rectangle.
    /// </summary>
    /// <returns> The area of this rectangle</returns>
    internal int GetArea()
    {
        return (Max[0] - Min[0]) * (Max[1] - Min[1]);
    }


    /// <summary>
    /// Computes the union of this rectangle and the passed rectangle, storing
    /// the result in this rectangle.
    /// </summary>
    /// <param name="r">Rectangle to add to this rectangle</param>
    internal void Add(Rectangle r)
    {
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (r.Min[i] < Min[i])
            {
                Min[i] = r.Min[i];
            }
            if (r.Max[i] > Max[i])
            {
                Max[i] = r.Max[i];
            }
        }
    }

    private static bool CompareArrays(int[] a1, int[] a2)
    {
        if ((a1 == null) || (a2 == null))
            return false;
        if (a1.Length != a2.Length)
            return false;

        for (var i = 0; i < a1.Length; i++)
            if (a1[i] != a2[i])
                return false;
        return true;
    }

    /// <summary>
    /// Determine whether this rectangle is equal to a given object.
    /// Equality is determined by the bounds of the rectangle.
    /// </summary>
    /// <param name="obj">The object to compare with this rectangle</param>
    /// <returns></returns>
    public override bool Equals(object obj)
    {
        if (obj is not Rectangle rectangle) 
            return false;
        
        return CompareArrays(rectangle.Min, Min) && CompareArrays(rectangle.Max, Max);
    }


    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    /// <summary>
    /// Return a string representation of this rectangle, in the form
    /// (1.2,3.4,5.6), (7.8, 9.10,11.12)
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();

        // min coordinates
        sb.Append('(');
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(Min[i]);
        }
        sb.Append("), (");

        // max coordinates
        for (var i = 0; i < DIMENSIONS; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(Max[i]);
        }
        sb.Append(')');
        return sb.ToString();
    }
}