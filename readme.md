A fork of https://github.com/drorgl/cspatialindexrt, specifically tuned for 2D usage with integer coordinates. My typical uses for R-trees are high-performance scenarios and I needed a version of this library that didn't run overhead for the third dimension and floating-point arithmetic. Maybe somebody else can find these modifications useful as well.

---

# Original readme:


C# Porting from https://sourceforge.net/projects/jsi/

Basic usage


Create a new instance:
RTree.RTree<T> tree = new RTree.RTree<T>();
            
Create a rectangle:
RTree.Rectangle rect = new RTree.Rectangle(1, 2, 3, 4, 5, 6);


Add a new rectangle to the RTree:
tree.Add(rect, object);


Check which objects are inside the rectangle:
var objects = tree.Contains(rect);


Count how many items in the RTree:
var i = tree.Count;


Check which objects intersect with the rectangle:
var objects = tree.Intersects(rect);


Create a point:
RTree.Point point = new RTree.Point(1, 2, 3);


Get a list of rectangles close to the point with maximum distance:
var objects = tree.Nearest(point, 10);
