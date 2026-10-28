using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Keeps an object on a point (the rider on the Lox's seat), the way the game moves a riding player each frame. Parenting to the seat
    /// would take on its scale too: the Lox's "attachpoint" is stretched (about 1 x 2 x 2), and the rider came out bigger.
    /// With <see cref="Bone"/> set (the goods), it is strapped to that bone instead: placed where the offsets put it with the Lox at rest,
    /// then moved and turned with the bone every frame, so the load rocks and tilts with his back as he walks.
    /// </summary>
    internal sealed class Follow : MonoBehaviour
    {
        public Transform Target;
        /// <summary>Metres up and back from the point, measured along <see cref="Frame"/> (the Lox itself: the seat's own axes are tilted).</summary>
        public float Up, Back, Right;
        public Transform Frame;
        /// <summary>Turned with <see cref="Frame"/> (the Lox) rather than the point (saddle bags hang level, not tilted like the seat).</summary>
        public bool FrameRotation;

        /// <summary>Strapped to this bone (see the class).</summary>
        public Transform Bone;
        /// <summary>With the Lox at rest, measured from the Lox itself: where the seat is, and where the bone is and how it's turned.</summary>
        public Vector3 RestSeat, RestBone;
        public Quaternion RestBoneTurn = Quaternion.identity;

        private void LateUpdate()
        {
            if (Target == null) { Destroy(gameObject); return; }
            if (Bone != null)
            {
                // where it sits on the Lox at rest, then carried by the bone from its rest to where it is now
                Vector3 atRest = RestSeat + Vector3.up * Up - Vector3.forward * Back + Vector3.right * Right;
                Quaternion fromRest = Bone.rotation * Quaternion.Inverse(RestBoneTurn);
                transform.SetPositionAndRotation(Bone.position + fromRest * (atRest - RestBone), fromRest);
                return;
            }
            Vector3 up = Frame ? Frame.up : Vector3.up, forward = Frame ? Frame.forward : Target.forward, right = Frame ? Frame.right : Target.right;
            transform.SetPositionAndRotation(Target.position + up * Up - forward * Back + right * Right,
                FrameRotation && Frame ? Frame.rotation : Target.rotation);
        }
    }
}
