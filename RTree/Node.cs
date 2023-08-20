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
    internal readonly int NodeId;
    internal Rectangle Mbr;
    internal readonly Rectangle[] Entries;
    internal readonly int[] Ids;
    internal readonly int Level;
    internal int EntryCount;

    internal Node(int nodeId, int level, int maxNodeEntries)
    {
        NodeId = nodeId;
        Level = level;
        Entries = new Rectangle[maxNodeEntries];
        Ids = new int[maxNodeEntries];
    }

    internal void AddEntry(Rectangle r, int id)
    {
        Ids[EntryCount] = id;
        Entries[EntryCount] = r.Copy();
        EntryCount++;
        if (Mbr == null)
        {
            Mbr = r.Copy();
        }
        else
        {
            Mbr.Add(r);
        }
    }

    internal void AddEntryNoCopy(Rectangle r, int id)
    {
        Ids[EntryCount] = id;
        Entries[EntryCount] = r;
        EntryCount++;
        if (Mbr == null)
        {
            Mbr = r.Copy();
        }
        else
        {
            Mbr.Add(r);
        }
    }

    /// <summary>
    /// Return the index of the found entry, or -1 if not found
    /// </summary>
    internal int FindEntry(Rectangle r, int id)
    {
        for (var i = 0; i < EntryCount; i++)
        {
            if (id == Ids[i] && r.Equals(Entries[i]))
            {
                return i;
            }
        }
        return -1;
    }

    // delete entry. This is done by setting it to null and copying the last entry into its space.

    /// <summary>
    /// delete entry. This is done by setting it to null and copying the last entry into its space.
    /// </summary>
    internal void DeleteEntry(int i, int minNodeEntries)
    {
        var lastIndex = EntryCount - 1;
        var deletedRectangle = Entries[i];
        Entries[i] = null;
        if (i != lastIndex)
        {
            Entries[i] = Entries[lastIndex];
            Ids[i] = Ids[lastIndex];
            Entries[lastIndex] = null;
        }
        EntryCount--;

        // if there are at least minNodeEntries, adjust the MBR.
        // otherwise, don't bother, as the Node<T> will be 
        // eliminated anyway.
        if (EntryCount >= minNodeEntries)
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
        if (!Mbr.EdgeOverlaps(deletedRectangle)) 
            return;
        
        Mbr.Set(Entries[0].Min, Entries[0].Max);

        for (var i = 1; i < EntryCount; i++)
        {
            Mbr.Add(Entries[i]);
        }
    }

    internal Rectangle GetEntry(int index)
    {
        return index < EntryCount ? Entries[index] : null;
    }

    /// <summary>
    /// eliminate null entries, move all entries to the start of the source node
    /// </summary>
    internal void Reorganize(RTree<T> rtree)
    {
        var countdownIndex = rtree.MaxNodeEntries - 1;
        for (var index = 0; index < EntryCount; index++)
        {
            if (Entries[index] != null) 
                continue;
            
            while (Entries[countdownIndex] == null && countdownIndex > index)
            {
                countdownIndex--;
            }
            Entries[index] = Entries[countdownIndex];
            Ids[index] = Ids[countdownIndex];
            Entries[countdownIndex] = null;
        }
    }

    internal bool IsLeaf()
    {
        return (Level == 1);
    }
}