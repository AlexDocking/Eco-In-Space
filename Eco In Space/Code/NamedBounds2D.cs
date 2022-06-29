using Eco.Shared.Math;
using Eco.World;

namespace EcoInSpace
{
    public struct NamedBounds2D : IBounds
    {
        public string Name { get => name; }
        public Vector2 TopCorner
        {
            get
            {
                return lowerLeftCorner + dimensions;
            }
        }
        public Vector2 WrappedTopCorner
        {
            get
            {
                return World.GetWrappedWorldPosition(TopCorner);
            }
        }
        private readonly string name;
        private Vector2 dimensions;
        private Vector2 lowerLeftCorner;
        public NamedBounds2D(float x, float y, float w, float h, string name)
        {
            this.lowerLeftCorner = new Vector2(x, y);
            this.dimensions = new Vector2(w, h);
            this.name = name;
        }
        public NamedBounds2D(Vector2 lowerLeftCorner, Vector2 dimensions, string name)
        {
            this.lowerLeftCorner = lowerLeftCorner;
            this.dimensions = dimensions;
            this.name = name;
        }
        public static implicit operator Bounds2D(NamedBounds2D namedBounds2D)
        {
            return new Bounds2D(namedBounds2D.lowerLeftCorner, namedBounds2D.dimensions);
        }
        public bool InBounds(Vector2 position)
        {
            bool inX = false;
            bool inY = false;

            if (WrappedTopCorner.x == TopCorner.x)
            {
                inX = (position.x >= lowerLeftCorner.x) && (position.x <= TopCorner.x);
            }
            else
            {
                inX = (position.x >= lowerLeftCorner.x) || (position.x <= WrappedTopCorner.x);
            }
            if (WrappedTopCorner.y == TopCorner.y)
            {
                inY = (position.y >= lowerLeftCorner.y) && (position.y <= TopCorner.y);
            }
            else
            {
                inY = (position.y >= lowerLeftCorner.y) || (position.y <= WrappedTopCorner.y);
            }
            return inX && inY;
        }
        public bool InBounds(Vector3 position)
        {
            return InBounds(position.XZ);
        }
    }
}