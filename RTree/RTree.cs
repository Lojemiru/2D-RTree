//   RTree.java
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

//  Ported to C# By Dror Gluska, April 9th, 2009


using System.Collections.Generic;
using System;
using System.Threading;

namespace RTree;

/// <summary>
/// This is a lightweight RTree implementation, specifically designed 
/// for the following features (in order of importance): 
///
/// Fast intersection query performance. To achieve this, the RTree 
/// uses only main memory to store entries. Obviously this will only improve
/// performance if there is enough physical memory to avoid paging.
/// Low memory requirements.
/// Fast add performance.
///
///
/// The main reason for the high speed of this RTree implementation is the 
/// avoidance of the creation of unnecessary objects, mainly achieved by using
/// primitive collections from the trove4j library.
/// author aled@sourceforge.net
/// version 1.0b2p1
/// Ported to C# By Dror Gluska, April 9th, 2009
/// </summary>
/// <typeparam name="T"></typeparam>
public sealed class RTree<T>
{
    private const int LOCKING_TIMEOUT = 10000;

    private readonly ReaderWriterLock locker = new();
    private const string VERSION = "1.0.0";

    // parameters of the tree
    private const int DEFAULT_MAX_NODE_ENTRIES = 10;
    internal int MaxNodeEntries;
    private int minNodeEntries;

    // map of nodeId -&gt; Node&lt;T&gt; object
    // [x] TODO eliminate this map - it should not be needed. Nodes
    // can be found by traversing the tree.
    //private TIntObjectHashMap nodeMap = new TIntObjectHashMap();
    private readonly Dictionary<int, Node<T>> nodeMap = new();

    // used to mark the status of entries during a Node&lt;T&gt; split
    private const int ENTRY_STATUS_ASSIGNED = 0;
    private const int ENTRY_STATUS_UNASSIGNED = 1;
    private byte[] entryStatus;
    private byte[] initialEntryStatus;

    // stacks used to store nodeId and entry index of each Node&lt;T&gt;
    // from the root down to the leaf. Enables fast lookup
    // of nodes when a split is propagated up the tree.
    //private TIntStack parents = new TIntStack();
    private readonly Stack<int> parents = new();
    //private TIntStack parentsEntry = new TIntStack();
    private readonly Stack<int> parentsEntry = new();

    // initialisation
    private int treeHeight = 1; // leaves are always level 1
    private int rootNodeId = 0;
    private int mSize = 0;

    // Enables creation of new nodes
    //private int highestUsedNodeId = rootNodeId; 
    private int highestUsedNodeId = 0;

    // Deleted Node&lt;T&gt; objects are retained in the nodeMap, 
    // so that they can be reused. Store the IDs of nodes
    // which can be reused.
    //private TIntStack deletedNodeIds = new TIntStack();
    private readonly Stack<int> deletedNodeIds = new();

    // List of nearest rectangles. Use a member variable to
    // avoid recreating the object each time nearest() is called.
    //private TIntArrayList nearestIds = new TIntArrayList();
    //List<int> nearestIds = new List<int>();

    //Added dictionaries to support generic objects..
    //possibility to change the code to support objects without dictionaries.
    private readonly Dictionary<int, T> idsToItems = new();
    private readonly Dictionary<T, int> itemsToIds = new();
    private volatile int idCounter = int.MinValue;

    /// <summary>
    /// Initialize implementation dependent properties of the RTree.
    /// </summary>
    public RTree()
    {
        Init();
    }

    /// <summary>
    /// Initialize implementation dependent properties of the RTree.
    /// </summary>
    /// <param name="maxNodeEntries">his specifies the maximum number of entries
    ///in a node. The default value is 10, which is used if the property is
    ///not specified, or is less than 2.</param>
    /// <param name="minNodeEntries">This specifies the minimum number of entries
    ///in a node. The default value is half of the MaxNodeEntries value (rounded
    ///down), which is used if the property is not specified or is less than 1.
    ///</param>
    public RTree(int maxNodeEntries, int minNodeEntries)
    {
        this.minNodeEntries = minNodeEntries;
        this.MaxNodeEntries = maxNodeEntries;
        Init();
    }

    private void Init()
    {
        locker.AcquireWriterLock(LOCKING_TIMEOUT);
        // Obviously a Node&lt;T&gt; with less than 2 entries cannot be split.
        // The Node&lt;T&gt; splitting algorithm will work with only 2 entries
        // per node, but will be inefficient.
        if (MaxNodeEntries < 2)
        {
            MaxNodeEntries = DEFAULT_MAX_NODE_ENTRIES;
        }

        // The MinNodeEntries must be less than or equal to (int) (MaxNodeEntries / 2)
        if (minNodeEntries < 1 || minNodeEntries > MaxNodeEntries / 2)
        {
            minNodeEntries = MaxNodeEntries / 2;
        }

        entryStatus = new byte[MaxNodeEntries];
        initialEntryStatus = new byte[MaxNodeEntries];

        for (var i = 0; i < MaxNodeEntries; i++)
        {
            initialEntryStatus[i] = ENTRY_STATUS_UNASSIGNED;
        }

        var root = new Node<T>(rootNodeId, 1, MaxNodeEntries);
        nodeMap.Add(rootNodeId, root);
            
        locker.ReleaseWriterLock();
    }

    /// <summary>
    /// Adds an item to the spatial index
    /// </summary>
    /// <param name="r"></param>
    /// <param name="item"></param>
    public void Add(Rectangle r, T item)
    {
        locker.AcquireWriterLock(LOCKING_TIMEOUT);
        idCounter++;
        var id = idCounter;

        idsToItems.Add(id, item);
        itemsToIds.Add(item, id);

        Add(r, id);
        locker.ReleaseWriterLock();
    }

    private void Add(Rectangle r, int id)
    {

        Add(r.Copy(), id, 1);

        mSize++;
    }

    /// <summary>
    /// Adds a new entry at a specified level in the tree
    /// </summary>
    /// <param name="r"></param>
    /// <param name="id"></param>
    /// <param name="level"></param>
    private void Add(Rectangle r, int id, int level)
    {
        // I1 [Find position for new record] Invoke ChooseLeaf to select a 
        // leaf Node&lt;T&gt; L in which to place r
        var n = ChooseNode(r, level);
        Node<T> newLeaf = null;

        // I2 [Add record to leaf node] If L has room for another entry, 
        // install E. Otherwise invoke SplitNode to obtain L and LL containing
        // E and all the old entries of L
        if (n.EntryCount < MaxNodeEntries)
        {
            n.AddEntryNoCopy(r, id);
        }
        else
        {
            newLeaf = SplitNode(n, r, id);
        }

        // I3 [Propagate changes upwards] Invoke AdjustTree on L, also passing LL
        // if a split was performed
        var newNode = AdjustTree(n, newLeaf);

        // I4 [Grow tree taller] If Node&lt;T&gt; split propagation caused the root to 
        // split, create a new root whose children are the two resulting nodes.
        if (newNode == null) 
            return;
        
        var oldRootNodeId = rootNodeId;
        var oldRoot = GetNode(oldRootNodeId);

        rootNodeId = GetNextNodeId();
        treeHeight++;
        var root = new Node<T>(rootNodeId, treeHeight, MaxNodeEntries);
        root.AddEntry(newNode.Mbr, newNode.NodeId);
        root.AddEntry(oldRoot.Mbr, oldRoot.NodeId);
        nodeMap.Add(rootNodeId, root);
    }

    /// <summary>
    /// Deletes an item from the spatial index
    /// </summary>
    /// <param name="r"></param>
    /// <param name="item"></param>
    /// <returns></returns>
    public bool Delete(Rectangle r, T item)
    {
        locker.AcquireWriterLock(LOCKING_TIMEOUT);
        var id = itemsToIds[item];

        var success = InternalDelete(r, id);
        if (success)
        {
            idsToItems.Remove(id);
            itemsToIds.Remove(item);
        }
        locker.ReleaseWriterLock();
        return success;
    }

    private bool InternalDelete(Rectangle r, int id)
    {
        // FindLeaf algorithm inlined here. Note the "official" algorithm 
        // searches all overlapping entries. This seems inefficient to me, 
        // as an entry is only worth searching if it contains (NOT overlaps)
        // the rectangle we are searching for.
        //
        // Also the algorithm has been changed so that it is not recursive.

        // FL1 [Search subtrees] If root is not a leaf, check each entry 
        // to determine if it contains r. For each entry found, invoke
        // findLeaf on the Node&lt;T&gt; pointed to by the entry, until r is found or
        // all entries have been checked.
        parents.Clear();
        parents.Push(rootNodeId);

        parentsEntry.Clear();
        parentsEntry.Push(-1);
        Node<T> n = null;
        var foundIndex = -1;  // index of entry to be deleted in leaf

        while (foundIndex == -1 && parents.Count > 0)
        {
            n = GetNode(parents.Peek());
            var startIndex = parentsEntry.Peek() + 1;

            if (!n.IsLeaf())
            {
                var contains = false;
                for (var i = startIndex; i < n.EntryCount; i++)
                {
                    if (!n.Entries[i].Contains(r)) 
                        continue;
                    
                    parents.Push(n.Ids[i]);
                    parentsEntry.Pop();
                    parentsEntry.Push(i); // this becomes the start index when the child has been searched
                    parentsEntry.Push(-1);
                    contains = true;
                    break; // ie go to next iteration of while()
                }
                if (contains)
                {
                    continue;
                }
            }
            else
            {
                foundIndex = n.FindEntry(r, id);
            }

            parents.Pop();
            parentsEntry.Pop();
        } // while not found

        if (foundIndex != -1)
        {
            n.DeleteEntry(foundIndex, minNodeEntries);
            CondenseTree(n);
            mSize--;
        }

        // shrink the tree if possible (i.e. if root Node&lt;T%gt; has exactly one entry,and that 
        // entry is not a leaf node, delete the root (it's entry becomes the new root)
        var root = GetNode(rootNodeId);
        while (root.EntryCount == 1 && treeHeight > 1)
        {
            root.EntryCount = 0;
            rootNodeId = root.Ids[0];
            treeHeight--;
            root = GetNode(rootNodeId);
        }

        return (foundIndex != -1);
    }

    /// <summary>
    /// Retrieve nearest items to a point in radius furthestDistance
    /// </summary>
    /// <param name="p">Point of origin</param>
    /// <param name="furthestDistance">maximum distance</param>
    /// <returns>List of items</returns>
    public List<T> Nearest(Point p, float furthestDistance)
    {
        var retval = new List<T>();
        locker.AcquireReaderLock(LOCKING_TIMEOUT);
        Nearest(p,  (id) =>
        {
            retval.Add(idsToItems[id]);
        }, furthestDistance);
        locker.ReleaseReaderLock();
        return retval;
    }


    private void Nearest(Point p, Action<int> v, float furthestDistance)
    {
        var rootNode = GetNode(rootNodeId);

        var nearestIds = new List<int>();

        Nearest(p, rootNode, nearestIds, furthestDistance);

        foreach (var id in nearestIds)
            v(id);
        
        nearestIds.Clear();
    }

    /// <summary>
    /// Retrieve items which intersect with Rectangle r
    /// </summary>
    /// <param name="r"></param>
    /// <returns></returns>
    public List<T> Intersects(Rectangle r)
    {
        var retval = new List<T>();
        locker.AcquireReaderLock(LOCKING_TIMEOUT);
        Intersects(r, (int id)=>
        {
            retval.Add(idsToItems[id]);
        });
        locker.ReleaseReaderLock();
        return retval;
    }


    private void Intersects(Rectangle r, Action<int> v)
    {
        var rootNode = GetNode(rootNodeId);
        Intersects(r, v, rootNode);
    }

    /// <summary>
    /// find all rectangles in the tree that are contained by the passed rectangle
    /// written to be non-recursive (should model other searches on this?)</summary>
    /// <param name="r"></param>
    /// <returns></returns>
    public List<T> Contains(Rectangle r)
    {
        var retval = new List<T>();
        locker.AcquireReaderLock(LOCKING_TIMEOUT);
        Contains(r, (id) =>
        {
            retval.Add(idsToItems[id]);
        });

        locker.ReleaseReaderLock();
        return retval;
    }

    private void Contains(Rectangle r, Action<int> v)
    {
        var _parents = new Stack<int>();
        //private TIntStack parentsEntry = new TIntStack();
        var _parentsEntry = new Stack<int>();


        // find all rectangles in the tree that are contained by the passed rectangle
        // written to be non-recursive (should model other searches on this?)

        _parents.Clear();
        _parents.Push(rootNodeId);

        _parentsEntry.Clear();
        _parentsEntry.Push(-1);

        // TODO: possible shortcut here - could test for intersection with the 
        // MBR of the root node. If no intersection, return immediately.

        while (_parents.Count > 0)
        {
            var n = GetNode(_parents.Peek());
            var startIndex = _parentsEntry.Peek() + 1;

            if (!n.IsLeaf())
            {
                // go through every entry in the index Node<T> to check
                // if it intersects the passed rectangle. If so, it 
                // could contain entries that are contained.
                var intersects = false;
                for (var i = startIndex; i < n.EntryCount; i++)
                {
                    if (!r.Intersects(n.Entries[i])) 
                        continue;
                    
                    _parents.Push(n.Ids[i]);
                    _parentsEntry.Pop();
                    _parentsEntry.Push(i); // this becomes the start index when the child has been searched
                    _parentsEntry.Push(-1);
                    intersects = true;
                    break; // ie go to next iteration of while()
                }
                if (intersects)
                {
                    continue;
                }
            }
            else
            {
                // go through every entry in the leaf to check if 
                // it is contained by the passed rectangle
                for (var i = 0; i < n.EntryCount; i++)
                {
                    if (r.Contains(n.Entries[i]))
                    {
                        v(n.Ids[i]);
                    }
                }
            }
            _parents.Pop();
            _parentsEntry.Pop();
        }
    }

    /// <summary>
    /// Returns the bounds of all the entries in the spatial index, or null if there are no entries.
    /// </summary>
    public Rectangle GetBounds()
    {
        Rectangle bounds = null;

        locker.AcquireReaderLock(LOCKING_TIMEOUT);
        Node<T> n = GetNode(GetRootNodeId());
        if (n != null && n.Mbr != null)
        {
            bounds = n.Mbr.Copy();
        }
        locker.ReleaseReaderLock();
        return bounds;
    }

    /// <summary>
    /// Returns a string identifying the type of spatial index, and the version number
    /// </summary>
    public string GetVersion()
    {
        return "RTree-" + VERSION;
    }
    //-------------------------------------------------------------------------
    // end of SpatialIndex methods
    //-------------------------------------------------------------------------


    /// <summary>
    /// Get the next available Node&lt;T&gt; ID. Reuse deleted Node&lt;T&gt; IDs if
    /// possible
    /// </summary>
    private int GetNextNodeId()
    {
        var nextNodeId = 0;
        if (deletedNodeIds.Count > 0)
        {
            nextNodeId = deletedNodeIds.Pop();
        }
        else
        {
            nextNodeId = 1 + highestUsedNodeId++;
        }
        return nextNodeId;
    }





    /// <summary>
    /// Get a Node&lt;T&gt; object, given the ID of the node.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    private Node<T> GetNode(int index)
    {
        return nodeMap[index];
    }

    /// <summary>
    /// Get the root Node&lt;T&gt; ID
    /// </summary>
    /// <returns></returns>
    public int GetRootNodeId()
    {
        return rootNodeId;
    }

    /// <summary>
    /// Split a node. Algorithm is taken pretty much verbatim from
    /// Guttman's original paper.
    /// </summary>
    /// <param name="n"></param>
    /// <param name="newRect"></param>
    /// <param name="newId"></param>
    /// <returns>return new Node&lt;T&gt; object.</returns>
    private Node<T> SplitNode(Node<T> n, Rectangle newRect, int newId)
    {
        // [Pick first entry for each group] Apply algorithm pickSeeds to 
        // choose two entries to be the first elements of the groups. Assign
        // each to a group.

        Array.Copy(initialEntryStatus, 0, entryStatus, 0, MaxNodeEntries);

        Node<T> newNode = null;
        newNode = new Node<T>(GetNextNodeId(), n.Level, MaxNodeEntries);
        nodeMap.Add(newNode.NodeId, newNode);

        PickSeeds(n, newRect, newId, newNode); // this also sets the entryCount to 1

        // [Check if done] If all entries have been assigned, stop. If one
        // group has so few entries that all the rest must be assigned to it in 
        // order for it to have the minimum number m, assign them and stop. 
        while (n.EntryCount + newNode.EntryCount < MaxNodeEntries + 1)
        {
            if (MaxNodeEntries + 1 - newNode.EntryCount == minNodeEntries)
            {
                // assign all remaining entries to original node
                for (var i = 0; i < MaxNodeEntries; i++)
                {
                    if (entryStatus[i] != ENTRY_STATUS_UNASSIGNED) 
                        continue;
                    
                    entryStatus[i] = ENTRY_STATUS_ASSIGNED;
                    n.Mbr.Add(n.Entries[i]);
                    n.EntryCount++;
                }
                break;
            }
            if (MaxNodeEntries + 1 - n.EntryCount == minNodeEntries)
            {
                // assign all remaining entries to new node
                for (var i = 0; i < MaxNodeEntries; i++)
                {
                    if (entryStatus[i] != ENTRY_STATUS_UNASSIGNED) 
                        continue;
                    
                    entryStatus[i] = ENTRY_STATUS_ASSIGNED;
                    newNode.AddEntryNoCopy(n.Entries[i], n.Ids[i]);
                    n.Entries[i] = null;
                }
                break;
            }

            // [Select entry to assign] Invoke algorithm pickNext to choose the
            // next entry to assign. Add it to the group whose covering rectangle 
            // will have to be enlarged least to accommodate it. Resolve ties
            // by adding the entry to the group with smaller area, then to the 
            // the one with fewer entries, then to either. Repeat from S2
            PickNext(n, newNode);
        }

        n.Reorganize(this);

        return newNode;
    }

    /// <summary>
    /// Pick the seeds used to split a node.
    /// Select two entries to be the first elements of the groups
    /// </summary>
    /// <param name="n"></param>
    /// <param name="newRect"></param>
    /// <param name="newId"></param>
    /// <param name="newNode"></param>
    private void PickSeeds(Node<T> n, Rectangle newRect, int newId, Node<T> newNode)
    {
        // Find extreme rectangles along all dimension. Along each dimension,
        // find the entry whose rectangle has the highest low side, and the one 
        // with the lowest high side. Record the separation.
        float maxNormalizedSeparation = 0;
        var highestLowIndex = 0;
        var lowestHighIndex = 0;

        // for the purposes of picking seeds, take the MBR of the Node&lt;T&gt; to include
        // the new rectangle aswell.
        n.Mbr.Add(newRect);

        for (var d = 0; d < Rectangle.DIMENSIONS; d++)
        {
            float tempHighestLow = newRect.Min[d];
            var tempHighestLowIndex = -1; // -1 indicates the new rectangle is the seed

            float tempLowestHigh = newRect.Max[d];
            var tempLowestHighIndex = -1;

            for (var i = 0; i < n.EntryCount; i++)
            {
                float tempLow = n.Entries[i].Min[d];
                if (tempLow >= tempHighestLow)
                {
                    tempHighestLow = tempLow;
                    tempHighestLowIndex = i;
                }
                else
                {  // ensure that the same index cannot be both lowestHigh and highestLow
                    float tempHigh = n.Entries[i].Max[d];
                    if (tempHigh <= tempLowestHigh)
                    {
                        tempLowestHigh = tempHigh;
                        tempLowestHighIndex = i;
                    }
                }

                // PS2 [Adjust for shape of the rectangle cluster] Normalize the separations
                // by dividing by the widths of the entire set along the corresponding
                // dimension
                var normalizedSeparation = (tempHighestLow - tempLowestHigh) / (n.Mbr.Max[d] - n.Mbr.Min[d]);

                // PS3 [Select the most extreme pair] Choose the pair with the greatest
                // normalized separation along any dimension.
                if (!(normalizedSeparation > maxNormalizedSeparation)) 
                    continue;
                
                maxNormalizedSeparation = normalizedSeparation;
                highestLowIndex = tempHighestLowIndex;
                lowestHighIndex = tempLowestHighIndex;
            }
        }

        // highestLowIndex is the seed for the new node.
        if (highestLowIndex == -1)
        {
            newNode.AddEntry(newRect, newId);
        }
        else
        {
            newNode.AddEntryNoCopy(n.Entries[highestLowIndex], n.Ids[highestLowIndex]);
            n.Entries[highestLowIndex] = null;

            // move the new rectangle into the space vacated by the seed for the new node
            n.Entries[highestLowIndex] = newRect;
            n.Ids[highestLowIndex] = newId;
        }

        // lowestHighIndex is the seed for the original node. 
        if (lowestHighIndex == -1)
        {
            lowestHighIndex = highestLowIndex;
        }

        entryStatus[lowestHighIndex] = ENTRY_STATUS_ASSIGNED;
        n.EntryCount = 1;
        n.Mbr.Set(n.Entries[lowestHighIndex].Min, n.Entries[lowestHighIndex].Max);
    }




    /// <summary>
    /// Pick the next entry to be assigned to a group during a Node&lt;T&gt; split.
    /// [Determine cost of putting each entry in each group] For each 
    /// entry not yet in a group, calculate the area increase required
    /// in the covering rectangles of each group  
    /// </summary>
    /// <param name="n"></param>
    /// <param name="newNode"></param>
    /// <returns></returns>
    private void PickNext(Node<T> n, Node<T> newNode)
    {
        var maxDifference = float.NegativeInfinity;
        var next = 0;
        var nextGroup = 0;

        for (var i = 0; i < MaxNodeEntries; i++)
        {
            if (entryStatus[i] != ENTRY_STATUS_UNASSIGNED) 
                continue;
            
            float nIncrease = n.Mbr.Enlargement(n.Entries[i]);
            float newNodeIncrease = newNode.Mbr.Enlargement(n.Entries[i]);
            var difference = Math.Abs(nIncrease - newNodeIncrease);

            if (!(difference > maxDifference)) 
                continue;
            
            next = i;

            if (nIncrease < newNodeIncrease)
            {
                nextGroup = 0;
            }
            else if (newNodeIncrease < nIncrease)
            {
                nextGroup = 1;
            }
            else if (n.Mbr.GetArea() < newNode.Mbr.GetArea())
            {
                nextGroup = 0;
            }
            else if (newNode.Mbr.GetArea() < n.Mbr.GetArea())
            {
                nextGroup = 1;
            }
            else if (newNode.EntryCount < MaxNodeEntries / 2)
            {
                nextGroup = 0;
            }
            else
            {
                nextGroup = 1;
            }
            
            maxDifference = difference;
        }

        entryStatus[next] = ENTRY_STATUS_ASSIGNED;

        if (nextGroup == 0)
        {
            n.Mbr.Add(n.Entries[next]);
            n.EntryCount++;
        }
        else
        {
            // move to new node.
            newNode.AddEntryNoCopy(n.Entries[next], n.Ids[next]);
            n.Entries[next] = null;
        }
    }


    /// <summary>
    /// Recursively searches the tree for the nearest entry. Other queries
    /// call execute() on an IntProcedure when a matching entry is found; 
    /// however nearest() must store the entry Ids as it searches the tree,
    /// in case a nearer entry is found.
    /// Uses the member variable nearestIds to store the nearest
    /// entry IDs.
    /// </summary>
    /// <remarks>TODO rewrite this to be non-recursive?</remarks>
    /// <param name="p"></param>
    /// <param name="n"></param>
    /// <param name="nearestIds"></param>
    /// <param name="nearestDistance"></param>
    /// <returns></returns>
    private float Nearest(Point p, Node<T> n, List<int> nearestIds, float nearestDistance)
    {
        for (var i = 0; i < n.EntryCount; i++)
        {
            float tempDistance = n.Entries[i].Distance(p);
            if (n.IsLeaf())
            { // for leaves, the distance is an actual nearest distance 
                if (tempDistance < nearestDistance)
                {
                    nearestDistance = tempDistance;
                    nearestIds.Clear();
                }
                if (tempDistance <= nearestDistance)
                {
                    nearestIds.Add(n.Ids[i]);
                }
            }
            else
            { // for index nodes, only go into them if they potentially could have
                // a rectangle nearer than actualNearest
                if (tempDistance <= nearestDistance)
                {
                    // search the child node
                    nearestDistance = Nearest(p, GetNode(n.Ids[i]), nearestIds, nearestDistance);
                }
            }
        }
        return nearestDistance;
    }


    /// <summary>
    /// Recursively searches the tree for all intersecting entries.
    /// Immediately calls execute() on the passed IntProcedure when 
    /// a matching entry is found.
    /// [x] TODO rewrite this to be non-recursive? Make sure it
    /// doesn't slow it down.
    /// </summary>
    /// <param name="r"></param>
    /// <param name="v"></param>
    /// <param name="n"></param>
    private void Intersects(Rectangle r, Action<int> v, Node<T> n)
    {
        for (var i = 0; i < n.EntryCount; i++)
        {
            if (!r.Intersects(n.Entries[i])) 
                continue;
            
            if (n.IsLeaf())
            {
                v(n.Ids[i]);
            }
            else
            {
                var childNode = GetNode(n.Ids[i]);
                Intersects(r, v, childNode);
            }
        }
    }

    private readonly Rectangle oldRectangle = new(0, 0, 0, 0);

    /// <summary>
    /// Used by delete(). Ensures that all nodes from the passed node
    /// up to the root have the minimum number of entries.
    /// <para>
    /// Note that the parent and parentEntry stacks are expected to
    /// contain the nodeIds of all parents up to the root.
    /// </para>
    /// </summary>
    private void CondenseTree(Node<T> l)
    {
        // CT1 [Initialize] Set n=l. Set the list of eliminated
        // nodes to be empty.
        var n = l;

        //TIntStack eliminatedNodeIds = new TIntStack();
        var eliminatedNodeIds = new Stack<int>();

        // CT2 [Find parent entry] If N is the root, go to CT6. Otherwise 
        // let P be the parent of N, and let En be N's entry in P  
        while (n.Level != treeHeight)
        {
            var parent = GetNode(parents.Pop());
            var parentEntry = parentsEntry.Pop();

            // CT3 [Eliminiate under-full node] If N has too few entries,
            // delete En from P and add N to the list of eliminated nodes
            if (n.EntryCount < minNodeEntries)
            {
                parent.DeleteEntry(parentEntry, minNodeEntries);
                eliminatedNodeIds.Push(n.NodeId);
            }
            else
            {
                // CT4 [Adjust covering rectangle] If N has not been eliminated,
                // adjust EnI to tightly contain all entries in N
                if (!n.Mbr.Equals(parent.Entries[parentEntry]))
                {
                    oldRectangle.Set(parent.Entries[parentEntry].Min, parent.Entries[parentEntry].Max);
                    parent.Entries[parentEntry].Set(n.Mbr.Min, n.Mbr.Max);
                    parent.RecalculateMbr(oldRectangle);
                }
            }
            // CT5 [Move up one level in tree] Set N=P and repeat from CT2
            n = parent;
        }

        // CT6 [Reinsert orphaned entries] Reinsert all entries of nodes in set Q.
        // Entries from eliminated leaf nodes are reinserted in tree leaves as in 
        // Insert(), but entries from higher level nodes must be placed higher in 
        // the tree, so that leaves of their dependent subtrees will be on the same
        // level as leaves of the main tree
        while (eliminatedNodeIds.Count > 0)
        {
            var e = GetNode(eliminatedNodeIds.Pop());
            for (var j = 0; j < e.EntryCount; j++)
            {
                Add(e.Entries[j], e.Ids[j], e.Level);
                e.Entries[j] = null;
            }
            e.EntryCount = 0;
            deletedNodeIds.Push(e.NodeId);
            nodeMap.Remove(e.NodeId);
        }
    }

    /// <summary>
    /// Used by add(). Chooses a leaf to add the rectangle to.
    /// </summary>
    private Node<T> ChooseNode(Rectangle r, int level)
    {
        // CL1 [Initialize] Set N to be the root node
        var n = GetNode(rootNodeId);
        parents.Clear();
        parentsEntry.Clear();

        // CL2 [Leaf check] If N is a leaf, return N
        while (true)
        {
            if (n.Level == level)
            {
                return n;
            }

            // CL3 [Choose subtree] If N is not at the desired level, let F be the entry in N 
            // whose rectangle FI needs least enlargement to include EI. Resolve
            // ties by choosing the entry with the rectangle of smaller area.
            float leastEnlargement = n.GetEntry(0).Enlargement(r);
            var index = 0; // index of rectangle in subtree
            for (var i = 1; i < n.EntryCount; i++)
            {
                var tempRectangle = n.GetEntry(i);
                float tempEnlargement = tempRectangle.Enlargement(r);
                if ((tempEnlargement < leastEnlargement) ||
                    ((tempEnlargement == leastEnlargement) &&
                     (tempRectangle.GetArea() < n.GetEntry(index).GetArea())))
                {
                    index = i;
                    leastEnlargement = tempEnlargement;
                }
            }

            parents.Push(n.NodeId);
            parentsEntry.Push(index);

            // CL4 [Descend until a leaf is reached] Set N to be the child Node&lt;T&gt; 
            // pointed to by Fp and repeat from CL2
            n = GetNode(n.Ids[index]);
        }
    }

    /// <summary>
    /// Ascend from a leaf Node&lt;T&gt; L to the root, adjusting covering rectangles and
    /// propagating Node&lt;T&gt; splits as necessary.
    /// </summary>
    private Node<T> AdjustTree(Node<T> n, Node<T> nn)
    {
        // AT1 [Initialize] Set N=L. If L was split previously, set NN to be 
        // the resulting second node.

        // AT2 [Check if done] If N is the root, stop
        while (n.Level != treeHeight)
        {
            // AT3 [Adjust covering rectangle in parent entry] Let P be the parent 
            // Node<T> of N, and let En be N's entry in P. Adjust EnI so that it tightly
            // encloses all entry rectangles in N.
            var parent = GetNode(parents.Pop());
            var entry = parentsEntry.Pop();

            if (!parent.Entries[entry].Equals(n.Mbr))
            {
                parent.Entries[entry].Set(n.Mbr.Min, n.Mbr.Max);
                parent.Mbr.Set(parent.Entries[0].Min, parent.Entries[0].Max);
                for (var i = 1; i < parent.EntryCount; i++)
                {
                    parent.Mbr.Add(parent.Entries[i]);
                }
            }

            // AT4 [Propagate Node<T> split upward] If N has a partner NN resulting from 
            // an earlier split, create a new entry Enn with Ennp pointing to NN and 
            // Enni enclosing all rectangles in NN. Add Enn to P if there is room. 
            // Otherwise, invoke splitNode to produce P and PP containing Enn and
            // all P's old entries.
            Node<T> newNode = null;
            if (nn != null)
            {
                if (parent.EntryCount < MaxNodeEntries)
                {
                    parent.AddEntry(nn.Mbr, nn.NodeId);
                }
                else
                {
                    newNode = SplitNode(parent, nn.Mbr.Copy(), nn.NodeId);
                }
            }

            // AT5 [Move up to next level] Set N = P and set NN = PP if a split 
            // occurred. Repeat from AT2
            n = parent;
            nn = newNode;
        }

        return nn;
    }

    public int Count
    {
        get
        {
            locker.AcquireReaderLock(LOCKING_TIMEOUT);

            var size = this.mSize;

            locker.ReleaseReaderLock();

            return size;
        }
    }

}