//   Node.java version 1.0b2p1
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
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

// Ported to C# By Dror Gluska, April 9th, 2009
namespace RTree;

/// <summary>
/// Used by RTree. There are no public methods in this class.
/// </summary>
internal sealed class Node<T>
{
    internal readonly int nodeId = 0;
    internal Rectangle mbr = null;
    internal readonly Rectangle[] entries = null;
    internal readonly int[] ids = null;
    internal readonly int level;
    internal int entryCount;

    internal Node(int nodeId, int level, int maxNodeEntries)
    {
        this.nodeId = nodeId;
        this.level = level;
        entries = new Rectangle[maxNodeEntries];
        ids = new int[maxNodeEntries];
    }

    internal void AddEntry(Rectangle r, int id)
    {
        ids[entryCount] = id;
        entries[entryCount] = r.Copy();
        entryCount++;
        if (mbr == null)
        {
            mbr = r.Copy();
        }
        else
        {
            mbr.Add(r);
        }
    }

    internal void AddEntryNoCopy(Rectangle r, int id)
    {
        ids[entryCount] = id;
        entries[entryCount] = r;
        entryCount++;
        if (mbr == null)
        {
            mbr = r.Copy();
        }
        else
        {
            mbr.Add(r);
        }
    }

    /// <summary>
    /// Return the index of the found entry, or -1 if not found
    /// </summary>
    internal int FindEntry(Rectangle r, int id)
    {
        for (var i = 0; i < entryCount; i++)
        {
            if (id == ids[i] && r.Equals(entries[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// delete entry. This is done by setting it to null and copying the last entry into its space.
    /// </summary>
    internal void DeleteEntry(int i, int minNodeEntries)
    {
        var lastIndex = entryCount - 1;
        var deletedRectangle = entries[i];
        entries[i] = null;
        if (i != lastIndex)
        {
            entries[i] = entries[lastIndex];
            ids[i] = ids[lastIndex];
            entries[lastIndex] = null;
        }
        entryCount--;

        // if there are at least minNodeEntries, adjust the MBR.
        // otherwise, don't bother, as the Node<T> will be 
        // eliminated anyway.
        if (entryCount >= minNodeEntries)
        {
            RecalculateMbr(deletedRectangle);
        }
    }

    /// <summary>
    /// oldRectangle is a rectangle that has just been deleted or made smaller.
    /// Thus, the MBR is only recalculated if the OldRectangle influenced the old MBR
    /// </summary>
    internal void RecalculateMbr(Rectangle deletedRectangle)
    {
        if (!mbr.EdgeOverlaps(deletedRectangle)) 
            return;
        
        mbr.Set(entries[0].min, entries[0].max);

        for (var i = 1; i < entryCount; i++)
        {
            mbr.Add(entries[i]);
        }
    }

    internal Rectangle GetEntry(int index)
    {
        return index < entryCount ? entries[index] : null;
    }

    /// <summary>
    /// eliminate null entries, move all entries to the start of the source node
    /// </summary>
    internal void Reorganize(RTree<T> rtree)
    {
        var countdownIndex = rtree.maxNodeEntries - 1;
        for (var index = 0; index < entryCount; index++)
        {
            if (entries[index] != null) 
                continue;
            
            while (entries[countdownIndex] == null && countdownIndex > index)
            {
                countdownIndex--;
            }
            entries[index] = entries[countdownIndex];
            ids[index] = ids[countdownIndex];
            entries[countdownIndex] = null;
        }
    }

    internal bool IsLeaf()
    {
        return (level == 1);
    }

    internal Rectangle GetMbr()
    {
        return mbr;
    }
}