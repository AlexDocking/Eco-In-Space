namespace EcoInSpace
{
    using Eco.Shared.Math;
    using Eco.Shared.Serialization;
    using Eco.World;

    public struct Bounds2D : IBounds
    {
        [BSONIgnore]
        public Vector2 TopCorner
        {
            get
            {
                return lowerLeftCorner + dimensions;
            }
        }
        [BSONIgnore]
        public Vector2 WrappedTopCorner
        {
            get
            {
                return World.GetWrappedWorldPosition(TopCorner);
            }
        }
        public Vector2 dimensions;
        public Vector2 lowerLeftCorner;
        public Bounds2D(float x, float y, float w, float h)
        {
            this.lowerLeftCorner = new Vector2(x, y);
            this.dimensions = new Vector2(w, h);
        }
        public Bounds2D(Vector2 lowerLeftCorner, Vector2 dimensions)
        {
            this.lowerLeftCorner = lowerLeftCorner;
            this.dimensions = dimensions;
        }
        public bool InBounds(Vector2 position)
        {
            bool inX = false;
            bool inY = false;
            Vector2 wrappedPosition = World.GetWrappedWorldPosition(position);

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